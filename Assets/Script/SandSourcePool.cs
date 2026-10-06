using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Source sand pool: Initialized full of sand with stacked color layers.
/// Multiple output directions can be selected (allowedFromSides).
/// Target connection is controlled externally by another controller.
/// </summary>
public class SandSourcePool : SandPool
{
    [Serializable]
    public class SandLayer
    {
        public SandColor colorAsset;

        [Range(1, 15)] public int sandType = 1;

        [Min(1)] public int pixelCount = 1000;
    }

    [Header("Source Layers (Top to Bottom)")]
    public List<SandLayer> layers = new List<SandLayer>();

    public bool autoFillFullPool = true;

    public bool simulatePhysics = false;

    [Header("Allowed Output Directions")]
    public List<Side> allowedFromSides = new List<Side> { Side.Bottom };

    [Header("Active Transfer (Controlled Externally)")]
    [SerializeField] private SandReceiverPool _activeReceiver;
    [SerializeField] private Side _activeFromSide = Side.Bottom;
    [SerializeField] private Side _activeToSide = Side.Top;
    [SerializeField] private bool _isTransferring = false;

    [Header("Transfer Parameters")]
    [Range(1, 64)] public int transferLanes = 16;
    [Range(1, 10)] public int transferSpeed = 2;

    public SandReceiverPool ActiveReceiver => _activeReceiver;
    public Side ActiveFromSide => _activeFromSide;
    public Side ActiveToSide => _activeToSide;
    public bool IsTransferring { get => _isTransferring; set => _isTransferring = value; }

    ComputeBuffer transferBuffer;

    protected override void OnPoolInitialized()
    {
        InitializeFullSand();
    }

    /// <summary>
    /// Checks if a direction is included in the allowed output sides list.
    /// </summary>
    public bool IsDirectionAllowed(Side side)
    {
        return allowedFromSides != null && allowedFromSides.Contains(side);
    }

    /// <summary>
    /// External API to connect this pool to a receiver on specified sides.
    /// Returns true if fromSide is allowed and connection is established.
    /// </summary>
    public bool Connect(SandReceiverPool receiver, Side from, Side to)
    {
        if (receiver == null)
        {
            Disconnect();
            return false;
        }

        if (!IsDirectionAllowed(from))
        {
            Debug.LogWarning($"[SandSourcePool] Direction '{from}' is not in allowedFromSides on {name}.", this);
            return false;
        }

        _activeReceiver = receiver;
        _activeFromSide = from;
        _activeToSide = to;
        _isTransferring = true;
        return true;
    }

    /// <summary>
    /// External API to disconnect the active receiver.
    /// </summary>
    public void Disconnect()
    {
        _activeReceiver = null;
        _isTransferring = false;
    }

    /// <summary>
    /// Fills the pool with configured sand layers from top to bottom.
    /// </summary>
    [ContextMenu("Rebuild Sand")]
    public void InitializeFullSand()
    {
        if (current == null) return;

        int totalPixels = width * height;
        uint[] data = new uint[totalPixels];

        if (layers != null && layers.Count > 0)
        {
            int totalConfiguredPixels = 0;
            foreach (var l in layers)
            {
                if (l != null) totalConfiguredPixels += Mathf.Max(0, l.pixelCount);
            }

            int currentRow = height - 1;
            int currentCol = 0;

            for (int i = 0; i < layers.Count; i++)
            {
                SandLayer layer = layers[i];
                if (layer == null) continue;

                int count = layer.pixelCount;
                if (autoFillFullPool && totalConfiguredPixels > 0)
                {
                    count = Mathf.RoundToInt((float)layer.pixelCount / totalConfiguredPixels * totalPixels);
                }

                int resolvedType = GetResolvedSandType(layer);

                for (int p = 0; p < count; p++)
                {
                    if (currentRow < 0) break;

                    uint shade = (uint)UnityEngine.Random.Range(0, SandPalette.Shades);
                    uint pixelValue = (uint)resolvedType | (shade << 4);

                    data[currentRow * width + currentCol] = pixelValue;

                    currentCol++;
                    if (currentCol >= width)
                    {
                        currentCol = 0;
                        currentRow--;
                    }
                }
            }

            // Fill remainder with last layer to ensure 100% full
            if (autoFillFullPool && currentRow >= 0 && layers.Count > 0)
            {
                int lastType = GetResolvedSandType(layers[layers.Count - 1]);
                while (currentRow >= 0)
                {
                    uint shade = (uint)UnityEngine.Random.Range(0, SandPalette.Shades);
                    data[currentRow * width + currentCol] = (uint)lastType | (shade << 4);

                    currentCol++;
                    if (currentCol >= width)
                    {
                        currentCol = 0;
                        currentRow--;
                    }
                }
            }
        }

        current.SetData(data);
        next.SetData(data);
        RenderTextureUpdate();
    }

    private int GetResolvedSandType(SandLayer layer)
    {
        if (layer.colorAsset != null && palette != null && palette.types != null)
        {
            int idx = Array.IndexOf(palette.types, layer.colorAsset);
            if (idx >= 0) return idx + 1;
        }
        return Mathf.Clamp(layer.sandType, 1, 15);
    }

    /// <summary>
    /// Checks whether this source has any layer configured with the specified sand type.
    /// </summary>
    public bool HasSandType(int sandType)
    {
        if (layers == null || layers.Count == 0) return false;
        foreach (var layer in layers)
        {
            if (layer != null && GetResolvedSandType(layer) == sandType)
                return true;
        }
        return false;
    }

    void Update()
    {
        if (current == null) return;

        // Sand transfer active
        if (_isTransferring && _activeReceiver != null && _activeReceiver.Ready)
        {
            if (_activeReceiver.IsFull)
            {
                _isTransferring = false;
                return;
            }

            EnsureTransferBuffer();

            int cellsA = EdgeCells(_activeFromSide);
            int cellsB = _activeReceiver.EdgeCells(_activeToSide);
            int n = Mathf.Max(1, Mathf.Min(transferLanes, Mathf.Min(cellsA, cellsB)));

            int offA = EdgeOffset(_activeFromSide, 0.5f, n);
            int offB = _activeReceiver.EdgeOffset(_activeToSide, 0.5f, n);
            int targetType = _activeReceiver.GetAcceptedType();

            for (int i = 0; i < transferSpeed; i++)
            {
                Extract(transferBuffer, 1, n, _activeFromSide, offA, false, 4, filterType: targetType);
                _activeReceiver.Inject(transferBuffer, 1, n, _activeToSide, offB, false, acceptType: targetType);
            }

            // Step both pools so sand sinks in source and stacks up in receiver
            Step();
            _activeReceiver.Step();

            if (_activeReceiver.IsFull)
            {
                _isTransferring = false;
            }
        }

        if (simulatePhysics)
        {
            for (int i = 0; i < stepsPerFrame; i++)
                Step();
        }

        RenderTextureUpdate();
    }

    private void EnsureTransferBuffer()
    {
        if (transferBuffer == null || transferBuffer.count != transferLanes)
        {
            transferBuffer?.Release();
            transferBuffer = new ComputeBuffer(transferLanes, sizeof(uint));
            transferBuffer.SetData(new uint[transferLanes]);
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        transferBuffer?.Release();
    }
}
