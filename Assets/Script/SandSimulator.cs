using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Dynamic sand flow simulator (pipe/stream) connecting a SandPool to a SandReceiverPool.
/// Extracts sand from the source, advances particles forward in lanes using Pipe.compute,
/// renders the flowing stream, and consumes particles at the tail into the receiver.
/// </summary>
public class SandSimulator : MonoBehaviour
{
    [Header("Simulation Parameters")]
    public int length = 64;
    public int lanes = 16;
    public int speed = 2;
    public int sourceDepth = 4;
    public Color emptyColor = new Color(0f, 0f, 0f, 0f);

    [Header("References")]
    public SandPool sourcePool;
    public SandReceiverPool receiverPool;
    public SandPool.Side sourceSide = SandPool.Side.Bottom;
    public int sourceOffset = 0;
    public bool sourceFlip = false;

    public ComputeShader shader;
    public MeshRenderer view;
    public SandPalette palette;

    private ComputeBuffer _buffer;
    private ComputeBuffer _consumeCounterBuffer;
    private RenderTexture _tex;

    private int _kAdvance = -1;
    private int _kRender = -1;
    private int _kConsumeTail = -1;

    private bool _readbackPending = false;
    private int[] _counterData = new int[2];
    private bool _isInitialized = false;

    public bool IsInitialized => _isInitialized;

    public void Init(
        SandPool source,
        SandReceiverPool receiver,
        SandPool.Side fromSide,
        int offA,
        bool flipA,
        int streamLength,
        int streamLanes,
        int transferSpeed,
        ComputeShader computeShader,
        SandPalette sandPalette,
        MeshRenderer meshRenderer,
        Color backgroundEmptyColor)
    {
        sourcePool = source;
        receiverPool = receiver;
        sourceSide = fromSide;
        sourceOffset = offA;
        sourceFlip = flipA;
        length = Mathf.Max(4, streamLength);
        lanes = Mathf.Max(1, streamLanes);
        speed = Mathf.Max(1, transferSpeed);
        shader = computeShader != null ? Instantiate(computeShader) : null;
        palette = sandPalette;
        view = meshRenderer;
        emptyColor = backgroundEmptyColor;

        if (shader == null)
        {
            Debug.LogError("[SandSimulator] Missing Pipe ComputeShader.", this);
            return;
        }

        _kAdvance = shader.FindKernel("Advance");
        _kRender = shader.FindKernel("Render");
        _kConsumeTail = shader.FindKernel("ConsumeTail");

        _buffer?.Release();
        _buffer = new ComputeBuffer(length * lanes, sizeof(uint));
        _buffer.SetData(new uint[length * lanes]);

        _consumeCounterBuffer?.Release();
        _consumeCounterBuffer = new ComputeBuffer(2, sizeof(int));
        _consumeCounterBuffer.SetData(new int[] { 0, 0 });

        if (_tex != null) _tex.Release();
        _tex = new RenderTexture(length, lanes, 0, RenderTextureFormat.ARGB32);
        _tex.enableRandomWrite = true;
        _tex.filterMode = FilterMode.Point;
        _tex.wrapMode = TextureWrapMode.Clamp;
        _tex.Create();

        if (view != null && view.material != null)
        {
            Material mat = view.material;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", _tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", _tex);
        }

        shader.SetInt("Length", length);
        shader.SetInt("Lanes", lanes);
        shader.SetVector("EmptyColor", emptyColor);

        if (palette != null)
        {
            palette.Apply(shader);
        }

        _isInitialized = true;
    }

    void Update()
    {
        if (!_isInitialized || _buffer == null || sourcePool == null || !sourcePool.Ready || receiverPool == null)
            return;

        int targetType = receiverPool.GetAcceptedType();
        int groups = Mathf.CeilToInt(lanes / 64f);

        // 1) Consume particles at the tail into receiver if receiver has capacity
        if (!receiverPool.IsFull && _kConsumeTail >= 0)
        {
            int remaining = receiverPool.Capacity - receiverPool.ReceivedCount;
            _consumeCounterBuffer.SetData(new int[] { 0, remaining });

            shader.SetInt("AcceptType", targetType);
            shader.SetBuffer(_kConsumeTail, "Buf", _buffer);
            shader.SetBuffer(_kConsumeTail, "ConsumeCounter", _consumeCounterBuffer);
            shader.Dispatch(_kConsumeTail, groups, 1, 1);

            ReadbackConsumeCounter(targetType);
        }

        // 2) Advance particles along the simulator Quad towards the receiver
        for (int i = 0; i < speed; i++)
        {
            shader.SetBuffer(_kAdvance, "Buf", _buffer);
            shader.Dispatch(_kAdvance, groups, 1, 1);
        }

        // 3) Extract new particles from the source pool into the head of the simulator
        if (!receiverPool.IsFull)
        {
            sourcePool.Extract(_buffer, length, lanes, sourceSide, sourceOffset, sourceFlip, sourceDepth, targetType);
            sourcePool.Step();
        }

        // 4) Render stream to texture
        shader.SetBuffer(_kRender, "Buf", _buffer);
        shader.SetTexture(_kRender, "Result", _tex);
        shader.Dispatch(_kRender, Mathf.CeilToInt(length / 8f), Mathf.CeilToInt(lanes / 8f), 1);
    }

    private void ReadbackConsumeCounter(int targetType)
    {
        if (_consumeCounterBuffer == null) return;

        if (SystemInfo.supportsAsyncGPUReadback)
        {
            if (!_readbackPending)
            {
                _readbackPending = true;
                AsyncGPUReadback.Request(_consumeCounterBuffer, req => OnReadbackComplete(req, targetType));
            }
        }
        else
        {
            _consumeCounterBuffer.GetData(_counterData);
            if (_counterData[0] > 0 && receiverPool != null)
            {
                receiverPool.ReceiveSand(_counterData[0], targetType);
            }
        }
    }

    private void OnReadbackComplete(AsyncGPUReadbackRequest req, int targetType)
    {
        _readbackPending = false;
        if (!req.hasError && _consumeCounterBuffer != null && receiverPool != null)
        {
            var data = req.GetData<int>();
            int count = data[0];
            if (count > 0)
            {
                receiverPool.ReceiveSand(count, targetType);
            }
        }
    }

    private void OnDestroy()
    {
        _buffer?.Release();
        _consumeCounterBuffer?.Release();
        if (_tex != null) _tex.Release();
        if (shader != null) Destroy(shader);
    }
}
