using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages automatic connections between GridSandSource and GridSandReceiver.
/// Supports multi-cell sources: Each cell of a source can independently connect
/// to a separate receiver below or adjacent to it.
/// </summary>
public class GridSandConnector : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private GridManager _gridManager;

    [Header("Settings")]
    [SerializeField] private bool _autoConnectOnPlaced = true;

    private static readonly Vector2Int[] CardinalOffsets =
    {
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right,
        Vector2Int.up
    };

    private void Awake()
    {
        _gridManager = GridManager.Instance;
    }

    private void OnEnable()
    {
        _gridManager.OnObjectPlaced += HandleObjectPlaced;
        _gridManager.OnObjectMoved += HandleObjectMoved;
        _gridManager.OnObjectRemoved += HandleObjectRemoved;
        SubscribeToPickupEvents();
    }

    private void OnDisable()
    {
        _gridManager.OnObjectPlaced -= HandleObjectPlaced;
        _gridManager.OnObjectMoved -= HandleObjectMoved;
        _gridManager.OnObjectRemoved -= HandleObjectRemoved;
        UnsubscribeFromPickupEvents();
    }

    private void Start()
    {
        EvaluateAllGridConnections();
    }

    private void SubscribeToPickupEvents()
    {
        GridObject[] objects = FindObjectsByType<GridObject>(FindObjectsSortMode.None);
        for (int i = 0; i < objects.Length; i++)
        {
            objects[i].OnPickup += HandleObjectPickup;
        }
    }

    private void UnsubscribeFromPickupEvents()
    {
        GridObject[] objects = FindObjectsByType<GridObject>(FindObjectsSortMode.None);
        for (int i = 0; i < objects.Length; i++)
        {
            objects[i].OnPickup -= HandleObjectPickup;
        }
    }

    private void HandleObjectPickup(GridObject obj)
    {
        if (obj.TryGetComponent(out GridSandSource source) && source.HasActiveConnections)
        {
            source.DisconnectAll();
            return;
        }

        if (obj.TryGetComponent(out GridSandReceiver receiver) && receiver.IsConnected)
        {
            BreakReceiverConnection(receiver);
        }
    }

    private void HandleObjectPlaced(GridObject obj, Vector2Int cell)
    {
        if (!_autoConnectOnPlaced) return;
        EvaluateObjectConnections(obj);
    }

    private void HandleObjectMoved(GridObject obj, Vector2Int oldCell, Vector2Int newCell)
    {
        if (obj.TryGetComponent(out GridSandSource source) && source.HasActiveConnections)
        {
            var activePairs = new List<KeyValuePair<Vector2Int, GridSandReceiver>>(source.ActiveConnections);
            for (int i = 0; i < activePairs.Count; i++)
            {
                Vector2Int sourceCell = activePairs[i].Key;
                GridSandReceiver receiver = activePairs[i].Value;

                if (!IsCellAdjacentToObject(sourceCell, receiver.GridObject))
                {
                    source.Disconnect(receiver);
                    receiver.Disconnect();
                }
            }
        }

        if (obj.TryGetComponent(out GridSandReceiver receiverComp) && receiverComp.IsConnected)
        {
            if (!AreObjectsAdjacent(receiverComp.ConnectedSource.GridObject, receiverComp.GridObject))
            {
                BreakReceiverConnection(receiverComp);
            }
        }

        if (_autoConnectOnPlaced)
        {
            EvaluateObjectConnections(obj);
        }
    }

    private void HandleObjectRemoved(GridObject obj)
    {
        HandleObjectPickup(obj);
    }

    [ContextMenu("Evaluate All Connections")]
    public void EvaluateAllGridConnections()
    {
        GridSandSource[] sources = FindObjectsByType<GridSandSource>(FindObjectsSortMode.None);
        for (int i = 0; i < sources.Length; i++)
        {
            EvaluateSourceConnections(sources[i]);
        }
    }

    public void EvaluateObjectConnections(GridObject obj)
    {
        if (obj.TryGetComponent(out GridSandSource source))
        {
            EvaluateSourceConnections(source);
            return;
        }

        if (obj.TryGetComponent(out GridSandReceiver receiver) && !receiver.IsConnected)
        {
            EvaluateReceiverConnections(receiver);
        }
    }

    public void EvaluateSourceConnections(GridSandSource source)
    {
        Vector2Int origin = source.GridObject.GridPosition;
        Vector2Int size = source.GridObject.Size;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Vector2Int sourceCell = origin + new Vector2Int(x, y);
                if (source.IsCellConnected(sourceCell)) continue;

                for (int i = 0; i < CardinalOffsets.Length; i++)
                {
                    Vector2Int neighborCell = sourceCell + CardinalOffsets[i];
                    GridObject neighborObj = _gridManager.GetObjectAt(neighborCell);

                    if (!neighborObj || neighborObj == source.GridObject) continue;

                    if (neighborObj.TryGetComponent(out GridSandReceiver receiver) && !receiver.IsConnected && source.CanConnect(receiver) && receiver.CanConnect(source))
                    {
                        EstablishConnection(source, sourceCell, receiver);
                        break;
                    }
                }
            }
        }
    }

    public void EvaluateReceiverConnections(GridSandReceiver receiver)
    {
        if (receiver.IsConnected) return;

        Vector2Int origin = receiver.GridObject.GridPosition;
        Vector2Int size = receiver.GridObject.Size;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Vector2Int receiverCell = origin + new Vector2Int(x, y);

                for (int i = 0; i < CardinalOffsets.Length; i++)
                {
                    Vector2Int neighborCell = receiverCell + CardinalOffsets[i];
                    GridObject neighborObj = _gridManager.GetObjectAt(neighborCell);

                    if (!neighborObj || neighborObj == receiver.GridObject) continue;

                    if (neighborObj.TryGetComponent(out GridSandSource source) && !source.IsCellConnected(neighborCell) && source.CanConnect(receiver) && receiver.CanConnect(source))
                    {
                        EstablishConnection(source, neighborCell, receiver);
                        return;
                    }
                }
            }
        }
    }

    public bool IsCellAdjacentToObject(Vector2Int cell, GridObject obj)
    {
        Vector2Int origin = obj.GridPosition;
        Vector2Int size = obj.Size;

        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                Vector2Int objCell = origin + new Vector2Int(x, y);
                for (int i = 0; i < CardinalOffsets.Length; i++)
                {
                    if (objCell + CardinalOffsets[i] == cell)
                        return true;
                }
            }
        }

        return false;
    }

    public bool AreObjectsAdjacent(GridObject a, GridObject b)
    {
        Vector2Int originA = a.GridPosition;
        Vector2Int sizeA = a.Size;
        Vector2Int originB = b.GridPosition;
        Vector2Int sizeB = b.Size;

        for (int ax = 0; ax < sizeA.x; ax++)
        {
            for (int ay = 0; ay < sizeA.y; ay++)
            {
                Vector2Int cellA = originA + new Vector2Int(ax, ay);
                for (int bx = 0; bx < sizeB.x; bx++)
                {
                    for (int by = 0; by < sizeB.y; by++)
                    {
                        Vector2Int cellB = originB + new Vector2Int(bx, by);
                        for (int i = 0; i < CardinalOffsets.Length; i++)
                        {
                            if (cellA + CardinalOffsets[i] == cellB)
                                return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private void EstablishConnection(GridSandSource source, Vector2Int sourceCell, GridSandReceiver receiver)
    {
        source.Connect(sourceCell, receiver);
        receiver.Connect(source);
        Debug.Log($"[GridSandConnector] Connected Source '{source.name}' at cell {sourceCell} ---> Receiver '{receiver.name}'");
    }

    private void BreakReceiverConnection(GridSandReceiver receiver)
    {
        GridSandSource source = receiver.ConnectedSource;
        receiver.Disconnect();
        source.Disconnect(receiver);
        Debug.Log($"[GridSandConnector] Disconnected Receiver '{receiver.name}'");
    }
}
