using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Source component for sand containers on the grid.
/// Contains sand layers, reference to GridSand simulation, and tracks connected receivers.
/// </summary>
public class GridSandSource : MonoBehaviour
{
    [Serializable]
    public class SandLayer
    {
        public SandColor color;
        public int amount = 100;
    }

    [Header("Sand Layers")]
    [SerializeField] private List<SandLayer> _layers = new();

    [Header("Sand Simulation")]
    [SerializeField] private GridSand _sand;

#if UNITY_EDITOR
    [Header("Debug")]
    [SerializeField] private bool _drawGizmos = true;
#endif

    [Header("Grid Object Reference")]
    [SerializeField] private GridObject _gridObject;

    [Header("Simulation Settings")]
    [SerializeField] private int _stepsPerFrame = 1;

    // Connected receivers and their contact border points on this source
    private readonly Dictionary<GridSandReceiver, List<GridConnectionPoint>> _connections = new();

    public event Action<GridSandReceiver, IReadOnlyList<GridConnectionPoint>> OnConnected;
    public event Action<GridSandReceiver> OnDisconnected;

    public IReadOnlyList<SandLayer> Layers => _layers;
    public GridSand Sand => _sand;
    public GridObject GridObject { get => _gridObject; set => _gridObject = value; }
    public IReadOnlyDictionary<GridSandReceiver, List<GridConnectionPoint>> Connections => _connections;
    public IReadOnlyCollection<GridSandReceiver> ConnectedReceivers => _connections.Keys;
    public bool HasConnections => _connections.Count > 0;
    public int StepsPerFrame { get => _stepsPerFrame; set => _stepsPerFrame = value; }

    private void Awake()
    {
        EnsureSand();
        if (_gridObject == null) _gridObject = GetComponent<GridObject>();
    }

    private void Start()
    {
        EnsureSand();
        if (_layers.Count > 0)
        {
            _sand.Init(_layers);
        }
    }

    private void Update()
    {
        if (_sand == null || !_sand.Ready) return;

        if (HasConnections)
        {
            foreach (var pair in _connections)
            {
                var points = pair.Value;
                for (int i = 0; i < points.Count; i++)
                {
                    RectInt bounds = _sand.GetConnectionPixelBounds(points[i], _gridObject);
                    _sand.EraseRegion(bounds);
                }
            }
        }

        _sand.StepSimulation(_stepsPerFrame);
    }

    private void EnsureSand()
    {
        if (_sand == null)
        {
            _sand = GetComponent<GridSand>();
            if (_sand == null)
            {
                _sand = gameObject.AddComponent<GridSand>();
            }
        }
    }

    public void InitializeLayers(IEnumerable<SandLayer> layers)
    {
        _layers.Clear();
        if (layers != null)
        {
            foreach (var l in layers)
            {
                _layers.Add(new SandLayer { color = l.color, amount = l.amount });
            }
        }

        if (_layers.Count > 0)
        {
            _sand.Init(_layers);
        }
    }

    public bool IsConnectedTo(GridSandReceiver receiver)
    {
        return _connections.ContainsKey(receiver);
    }

    public IReadOnlyList<GridConnectionPoint> GetConnectionPoints(GridSandReceiver receiver)
    {
        if (_connections.TryGetValue(receiver, out var points))
            return points;
        return Array.Empty<GridConnectionPoint>();
    }

    public void ConnectReceiver(GridSandReceiver receiver, List<GridConnectionPoint> connectionPoints)
    {
        _connections[receiver] = connectionPoints;
        OnConnected?.Invoke(receiver, connectionPoints);
    }

    public void DisconnectReceiver(GridSandReceiver receiver)
    {
        if (_connections.Remove(receiver))
        {
            OnDisconnected?.Invoke(receiver);
        }
    }

    public void DisconnectAllReceivers()
    {
        var receivers = new List<GridSandReceiver>(_connections.Keys);
        for (int i = 0; i < receivers.Count; i++)
        {
            receivers[i].Disconnect();
        }
        _connections.Clear();
    }

    private void OnDestroy()
    {
        DisconnectAllReceivers();
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        if (!_drawGizmos || _connections == null || _connections.Count == 0) return;

        Vector3 sourcePos = transform.position;
        Gizmos.color = Color.cyan;

        foreach (var receiver in _connections.Keys)
        {
            if (receiver != null)
            {
                Gizmos.DrawLine(sourcePos, receiver.transform.position);
            }
        }
    }
#endif
}
