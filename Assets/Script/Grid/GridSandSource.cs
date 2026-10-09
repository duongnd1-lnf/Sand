using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Source component for sand containers on the grid.
/// Can occupy multiple cells and contain multiple stacked sand layers.
/// Coordinates grid connections and sand transfer, delegating rendering and simulation to GridSandSourceVisual.
/// </summary>
public class GridSandSource : MonoBehaviour
{
    [Serializable]
    public class SandLayer
    {
        public SandColor color;
        public int amount = 100;
        [NonSerialized] public int initialAmount = 100;
        [NonSerialized] public int initialPixels = 0;
        [NonSerialized] public int typeId = 1;
    }

    [Header("Sand Layers")]
    [SerializeField] private List<SandLayer> _layers = new List<SandLayer>();
    [SerializeField] private bool _topToBottom = false;
    [SerializeField] private bool _simulatePhysics = false;

    [Header("Transfer Settings")]
    [SerializeField] private float _transferRate = 10f;
    [Range(1, 4)] [SerializeField] private int _simStepsPerTick = 2;

    [Header("Visual Reference")]
    [SerializeField] private GridSandSourceVisual _visual;

    private GridObject _gridObject;
    private float _frameBudgetAccumulator = 0f;
    private float _drainedPixelAccumulator = 0f;

    private readonly Dictionary<Vector2Int, GridSandReceiver> _cellToReceiver = new Dictionary<Vector2Int, GridSandReceiver>();
    private readonly Dictionary<GridSandReceiver, Vector2Int> _receiverToCell = new Dictionary<GridSandReceiver, Vector2Int>();

    public event Action<Vector2Int, GridSandReceiver> OnCellConnected;
    public event Action<Vector2Int, GridSandReceiver> OnCellDisconnected;
    public event Action<GridSandReceiver> OnConnected;
    public event Action OnDisconnected;

    public GridObject GridObject => _gridObject;
    public IReadOnlyList<SandLayer> Layers => _layers;
    public bool TopToBottom => _topToBottom;
    public bool HasActiveConnections => _cellToReceiver.Count > 0;
    public int ActiveConnectionCount => _cellToReceiver.Count;
    public IReadOnlyDictionary<Vector2Int, GridSandReceiver> ActiveConnections => _cellToReceiver;
    public bool IsConnected => HasActiveConnections;

    public SandLayer CurrentLayer => _layers.Count > 0 ? _layers[0] : null;
    public SandColor CurrentColor => CurrentLayer?.color;
    public bool HasContent => _layers.Count > 0 && CurrentLayer.amount > 0;
    public float TransferRate => _transferRate;
    public int TotalAmount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _layers.Count; i++) total += _layers[i].amount;
            return total;
        }
    }

    private void Awake()
    {
        _gridObject = GetComponent<GridObject>();
        EnsureVisual();
    }

    private void Start()
    {
        EnsureVisual();
    }

    private void EnsureVisual()
    {
        if (_visual == null)
        {
            _visual = GetComponent<GridSandSourceVisual>();
            if (_visual == null)
            {
                _visual = gameObject.AddComponent<GridSandSourceVisual>();
            }
        }
        _visual.BindSource(this);
    }

    private void Update()
    {
        if (HasActiveConnections)
        {
            ProcessTransfer(Time.deltaTime);
            _visual.StepSimulation(_simStepsPerTick);
        }
        else if (_simulatePhysics)
        {
            _visual.StepSimulation(1);
        }
    }

    public void ProcessTransfer(float deltaTime)
    {
        if (!HasActiveConnections) return;

        SandLayer currentLayer = CurrentLayer;
        float pixelsPerUnit = currentLayer != null && currentLayer.initialAmount > 0
            ? (float)currentLayer.initialPixels / currentLayer.initialAmount
            : (_visual.width * _visual.height / 100f);

        float budgetFloat = _transferRate * deltaTime * pixelsPerUnit;
        _frameBudgetAccumulator += budgetFloat;
        int frameBudget = Mathf.FloorToInt(_frameBudgetAccumulator);
        if (frameBudget <= 0) return;
        _frameBudgetAccumulator -= frameBudget;

        var connections = new List<KeyValuePair<Vector2Int, GridSandReceiver>>(_cellToReceiver);
        for (int i = 0; i < connections.Count; i++)
        {
            Vector2Int cell = connections[i].Key;
            GridSandReceiver receiver = connections[i].Value;
            if (receiver == null || !receiver.IsConnected || receiver.IsFull) continue;

            int targetTypeId = _visual.GetTypeId(receiver.TargetColor);
            if (targetTypeId <= 0) continue;

            SandLayer targetLayer = FindLayerByColor(receiver.TargetColor);

            // 1. Cát phải rút trước! Rút từ lỗ đáy và đếm số lượng hạt thực tế bị xóa khỏi hồ
            int actuallyDrained = _visual.DrainHole(cell, receiver, frameBudget, targetTypeId, _gridObject);
            if (actuallyDrained <= 0)
            {
                // Kiểm tra xem tại ô này còn hạt cát nào màu này không
                if (!_visual.HasSandInCell(cell, targetTypeId, _gridObject, 1))
                {
                    Disconnect(receiver);
                    receiver.Disconnect();
                }
                continue;
            }

            // 2. Tạo hạt particle tương ứng với số cát thực tế đã rút bay vào receiver
            _visual.EmitStreamParticles(cell, receiver, actuallyDrained, receiver.TargetColor);

            // 3. Cập nhật amount cho receiver và source tương ứng số cát đã rút
            _drainedPixelAccumulator += actuallyDrained;
            float amountFloat = _drainedPixelAccumulator / pixelsPerUnit;
            int amountToAdd = Mathf.FloorToInt(amountFloat);
            if (amountToAdd > 0)
            {
                _drainedPixelAccumulator -= amountToAdd * pixelsPerUnit;
                int space = receiver.Capacity - receiver.CurrentAmount;
                int transferAmount = Mathf.Min(amountToAdd, space);
                if (targetLayer != null)
                {
                    transferAmount = Mathf.Min(transferAmount, targetLayer.amount);
                    targetLayer.amount -= transferAmount;
                    if (targetLayer.amount <= 0)
                    {
                        PurgeLayerAndAdvance(targetLayer, targetTypeId);
                    }
                }

                if (transferAmount > 0)
                {
                    receiver.AddAmount(transferAmount);
                }
            }
        }
    }

    private void PurgeLayerAndAdvance(SandLayer layer, int resolvedType)
    {
        _visual.PurgeLayer(resolvedType);
        _layers.Remove(layer);

        if (_layers.Count == 0)
        {
            _visual.ClearAll();
            DisconnectAll();
        }
    }

    public void PurgeCurrentLayerAndAdvance(int resolvedType)
    {
        if (CurrentLayer != null)
        {
            PurgeLayerAndAdvance(CurrentLayer, resolvedType);
        }
    }

    public SandLayer FindLayerByColor(SandColor color)
    {
        for (int i = 0; i < _layers.Count; i++)
        {
            if (_layers[i].color == color) return _layers[i];
        }
        return null;
    }

    public bool HasRealSandFor(Vector2Int sourceCell, GridSandReceiver receiver)
    {
        if (_visual == null || !_visual.Ready) return false;
        int targetTypeId = _visual.GetTypeId(receiver.TargetColor);
        if (targetTypeId <= 0) return false;

        return _visual.HasSandInCell(sourceCell, targetTypeId, _gridObject);
    }

    public bool IsCellConnected(Vector2Int sourceCell) => _cellToReceiver.ContainsKey(sourceCell);
    public bool IsConnectedTo(GridSandReceiver receiver) => _receiverToCell.ContainsKey(receiver);

    public bool CanConnect(GridSandReceiver receiver)
    {
        if (receiver.IsConnected || receiver.IsFull) return false;
        int targetTypeId = _visual.GetTypeId(receiver.TargetColor);
        return targetTypeId > 0 && FindLayerByColor(receiver.TargetColor) != null;
    }

    public bool CanConnect(Vector2Int sourceCell, GridSandReceiver receiver)
    {
        if (!CanConnect(receiver)) return false;
        if (IsCellConnected(sourceCell)) return false;
        return HasRealSandFor(sourceCell, receiver);
    }

    public void Connect(Vector2Int sourceCell, GridSandReceiver receiver)
    {
        if (_cellToReceiver.ContainsKey(sourceCell)) return;
        if (_receiverToCell.ContainsKey(receiver)) return;

        _cellToReceiver[sourceCell] = receiver;
        _receiverToCell[receiver] = sourceCell;

        _visual.CreateStream(sourceCell, receiver);

        OnCellConnected?.Invoke(sourceCell, receiver);
        OnConnected?.Invoke(receiver);
    }

    public void Disconnect(GridSandReceiver receiver)
    {
        if (!_receiverToCell.TryGetValue(receiver, out Vector2Int sourceCell)) return;

        _cellToReceiver.Remove(sourceCell);
        _receiverToCell.Remove(receiver);

        _visual.DestroyStream(sourceCell);

        OnCellDisconnected?.Invoke(sourceCell, receiver);
        if (_cellToReceiver.Count == 0)
        {
            OnDisconnected?.Invoke();
        }
    }

    public void DisconnectCell(Vector2Int sourceCell)
    {
        if (!_cellToReceiver.TryGetValue(sourceCell, out GridSandReceiver receiver)) return;
        Disconnect(receiver);
    }

    public void DisconnectAll()
    {
        _visual.DestroyAllStreams();

        if (_cellToReceiver.Count == 0) return;

        var receivers = new List<GridSandReceiver>(_receiverToCell.Keys);
        for (int i = 0; i < receivers.Count; i++)
        {
            Disconnect(receivers[i]);
            receivers[i].Disconnect();
        }
    }

    public void InitializeLayers(IEnumerable<SandLayer> layers)
    {
        _layers.Clear();
        foreach (var l in layers)
        {
            _layers.Add(new SandLayer { color = l.color, amount = l.amount });
        }

        if (_visual != null && _visual.Ready)
        {
            _visual.InitializeSandLayers(_layers, _topToBottom);
        }
    }

    public void ConsumeCurrentLayer(int amount)
    {
        if (_layers.Count == 0) return;

        CurrentLayer.amount -= amount;
        if (CurrentLayer.amount <= 0)
        {
            PurgeCurrentLayerAndAdvance(CurrentLayer.typeId);
        }
    }

    public int Tick(float deltaTime)
    {
        if (!HasContent || !HasActiveConnections) return 0;
        int unitsPerTick = Mathf.Max(1, Mathf.RoundToInt(_transferRate));
        return Mathf.Min(unitsPerTick, CurrentLayer.amount);
    }

    private void OnDestroy()
    {
        DisconnectAll();
    }
}
