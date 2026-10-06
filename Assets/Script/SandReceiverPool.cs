using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

/// <summary>
/// Receiver sand pool:
/// - Initialized empty.
/// - Strictly accepts only 1 specific color type (acceptedColor or acceptedSandType).
/// - Has a configurable capacity (e.g. 1000 particles).
/// - Automatically covers/fills up proportional to the percentage of sand received (0% -> 100%).
/// - Halts receiving when full (100%) and fires the onPoolFull event.
/// </summary>
public class SandReceiverPool : SandPool
{
    [Header("Accepted Color (Single Color Filter)")]
    public SandColor acceptedColor;

    [Range(1, 15)] public int acceptedSandType = 1;

    [Header("Capacity & Target Fill")]
    [Min(1)] public int capacity = 1000;

    [SerializeField] private int _receivedCount = 0;

    [Header("Receiver Physics")]
    public bool simulatePhysics = true;

    [Header("Events")]
    public UnityEvent onPoolFull;

    public int ReceivedCount => _receivedCount;
    public float FillPercentage => Mathf.Clamp01((float)_receivedCount / Mathf.Max(1, capacity));
    public bool IsFull => _receivedCount >= capacity;

    private ComputeBuffer _counterBuffer;
    private int _kFillReceiver = -1;
    private bool _readbackPending = false;
    private int[] _counterData = new int[2];
    private int _lastFilledRow = 0;
    private bool _fullEventFired = false;

    /// <summary>
    /// Returns the resolved sand type ID (1..15) accepted by this pool.
    /// </summary>
    public int GetAcceptedType()
    {
        if (acceptedColor != null && palette != null && palette.types != null)
        {
            int idx = Array.IndexOf(palette.types, acceptedColor);
            if (idx >= 0) return idx + 1;
        }
        return Mathf.Clamp(acceptedSandType, 1, 15);
    }

    /// <summary>
    /// Checks whether this receiver accepts a given sand type.
    /// </summary>
    public bool AcceptsSandType(int type)
    {
        return type == GetAcceptedType();
    }

    protected override void OnPoolInitialized()
    {
        _kFillReceiver = shader.FindKernel("FillReceiver");
        EnsureCounterBuffer();
        ResetPool();
    }

    private void EnsureCounterBuffer()
    {
        if (_counterBuffer == null)
        {
            _counterBuffer = new ComputeBuffer(2, sizeof(int));
            _counterBuffer.SetData(new int[] { _receivedCount, Mathf.Max(1, capacity) });
        }
    }

    /// <summary>
    /// Injects sand into this receiver.
    /// Rejects particles if already full or if color does not match accepted type.
    /// </summary>
    public override void Inject(ComputeBuffer pipe, int pipeLength, int lanes, Side side, int offset, bool flip, int acceptType = 0)
    {
        if (IsFull) return;

        EnsureCounterBuffer();
        shader.SetBuffer(kInject, "PortCounter", _counterBuffer);
        base.Inject(pipe, pipeLength, lanes, side, offset, flip, GetAcceptedType());
    }

    void Update()
    {
        if (current == null) return;

        SyncCounterFromGPU();
        UpdateFillVisual();

        if (simulatePhysics)
        {
            for (int i = 0; i < stepsPerFrame; i++)
                Step();
        }

        RenderTextureUpdate();
    }

    private void SyncCounterFromGPU()
    {
        if (_counterBuffer == null) return;

        if (SystemInfo.supportsAsyncGPUReadback)
        {
            if (!_readbackPending)
            {
                _readbackPending = true;
                AsyncGPUReadback.Request(_counterBuffer, OnReadbackComplete);
            }
        }
        else
        {
            _counterBuffer.GetData(_counterData);
            ApplyReceivedCount(_counterData[0]);
        }
    }

    private void OnReadbackComplete(AsyncGPUReadbackRequest req)
    {
        _readbackPending = false;
        if (!req.hasError && _counterBuffer != null)
        {
            var data = req.GetData<int>();
            ApplyReceivedCount(data[0]);
        }
    }

    private void ApplyReceivedCount(int count)
    {
        int clamped = Mathf.Clamp(count, 0, capacity);
        if (clamped != _receivedCount)
        {
            _receivedCount = clamped;
        }

        if (IsFull && !_fullEventFired)
        {
            _fullEventFired = true;
            onPoolFull?.Invoke();
        }
    }

    /// <summary>
    /// Updates the sand buffer to automatically cover the pool proportional to FillPercentage.
    /// </summary>
    private void UpdateFillVisual()
    {
        if (_kFillReceiver < 0) return;

        float p = FillPercentage;
        int targetRow = p >= 1f ? height : Mathf.FloorToInt(p * height);
        if (p > 0f && targetRow == 0) targetRow = 1;
        targetRow = Mathf.Min(targetRow, height);

        if (targetRow > _lastFilledRow)
        {
            shader.SetInt("TargetRow", targetRow);
            shader.SetInt("FillType", GetAcceptedType());
            shader.SetBuffer(_kFillReceiver, "Current", current);
            shader.Dispatch(_kFillReceiver, gx, gy, 1);
            _lastFilledRow = targetRow;
        }
    }

    /// <summary>
    /// Resets the receiver pool to completely empty.
    /// </summary>
    [ContextMenu("Reset Receiver")]
    public void ResetPool()
    {
        _receivedCount = 0;
        _lastFilledRow = 0;
        _fullEventFired = false;
        if (_counterBuffer != null)
        {
            _counterBuffer.SetData(new int[] { 0, Mathf.Max(1, capacity) });
        }
        ClearAll();
        RenderTextureUpdate();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (Application.isPlaying && _counterBuffer != null)
        {
            _counterBuffer.SetData(new int[] { _receivedCount, Mathf.Max(1, capacity) });
        }
    }
#endif

    protected override void OnDestroy()
    {
        base.OnDestroy();
        _counterBuffer?.Release();
    }
}
