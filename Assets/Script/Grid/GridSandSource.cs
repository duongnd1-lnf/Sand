using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Source component for sand containers on the grid.
/// Can occupy multiple cells and contain multiple stacked sand layers.
/// Each occupied cell of the source can independently connect to a separate receiver below it.
/// </summary>
[RequireComponent(typeof(GridObject))]
public class GridSandSource : MonoBehaviour
{
    [Serializable]
    public class SandLayer
    {
        public SandColor color;
        public int amount = 100;
    }

    [Header("Sand Layers (Top to Bottom)")]
    [SerializeField] private List<SandLayer> _layers = new List<SandLayer>();

    private GridObject _gridObject;

    // Multi-cell connections: sourceCell -> connected receiver
    private readonly Dictionary<Vector2Int, GridSandReceiver> _cellToReceiver = new Dictionary<Vector2Int, GridSandReceiver>();
    private readonly Dictionary<GridSandReceiver, Vector2Int> _receiverToCell = new Dictionary<GridSandReceiver, Vector2Int>();

    public event Action<Vector2Int, GridSandReceiver> OnCellConnected;
    public event Action<Vector2Int, GridSandReceiver> OnCellDisconnected;
    public event Action<GridSandReceiver> OnConnected;
    public event Action OnDisconnected;

    public GridObject GridObject => _gridObject;
    public IReadOnlyList<SandLayer> Layers => _layers;
    public bool HasActiveConnections => _cellToReceiver.Count > 0;
    public int ActiveConnectionCount => _cellToReceiver.Count;
    public IReadOnlyDictionary<Vector2Int, GridSandReceiver> ActiveConnections => _cellToReceiver;
    public bool IsConnected => HasActiveConnections;

    public SandLayer CurrentLayer => _layers.Count > 0 ? _layers[0] : null;
    public SandColor CurrentColor => CurrentLayer.color;
    public bool HasContent => _layers.Count > 0 && CurrentLayer.amount > 0;

    private void Awake()
    {
        _gridObject = GetComponent<GridObject>();
    }

    public bool IsCellConnected(Vector2Int sourceCell)
    {
        return _cellToReceiver.ContainsKey(sourceCell);
    }

    public bool IsConnectedTo(GridSandReceiver receiver)
    {
        return _receiverToCell.ContainsKey(receiver);
    }

    public bool CanConnect(GridSandReceiver receiver)
    {
        if (receiver.IsConnected || receiver.IsFull) return false;
        if (!HasContent) return false;
        if (receiver.TargetColor != CurrentColor) return false;

        return true;
    }

    public void Connect(Vector2Int sourceCell, GridSandReceiver receiver)
    {
        if (_cellToReceiver.ContainsKey(sourceCell)) return;
        if (_receiverToCell.ContainsKey(receiver)) return;

        _cellToReceiver[sourceCell] = receiver;
        _receiverToCell[receiver] = sourceCell;

        OnCellConnected?.Invoke(sourceCell, receiver);
        OnConnected?.Invoke(receiver);
    }

    public void Disconnect(GridSandReceiver receiver)
    {
        if (!_receiverToCell.TryGetValue(receiver, out Vector2Int sourceCell)) return;

        _cellToReceiver.Remove(sourceCell);
        _receiverToCell.Remove(receiver);

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
    }

    public void ConsumeCurrentLayer(int amount)
    {
        if (_layers.Count == 0) return;

        CurrentLayer.amount -= amount;
        if (CurrentLayer.amount <= 0)
        {
            _layers.RemoveAt(0);
            SandColor nextColor = CurrentColor;

            var receivers = new List<GridSandReceiver>(_receiverToCell.Keys);
            for (int i = 0; i < receivers.Count; i++)
            {
                GridSandReceiver r = receivers[i];
                if (_layers.Count == 0 || r.TargetColor != nextColor)
                {
                    Disconnect(r);
                    r.Disconnect();
                }
            }
        }
    }
}
