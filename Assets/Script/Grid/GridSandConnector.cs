using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages automatic connections between GridSandSource and GridSandReceiver.
/// Supports multi-cell sources: Each cell of a source can independently connect
/// to a separate receiver below or adjacent to it.
/// </summary>
public class GridSandConnector : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private bool _autoConnectOnPlaced = true;

    private static readonly Vector2Int[] CardinalOffsets =
    {
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right,
        Vector2Int.up
    };


    private void Start()
    {
        GridManager.Instance.OnObjectPlaced += HandleObjectPlaced;
        GridManager.Instance.OnObjectMoved += HandleObjectMoved;
        GridManager.Instance.OnObjectRemoved += HandleObjectRemoved;
        SubscribeToPickupEvents();
        EvaluateAllGridConnections();
    }

    private void OnDestroy()
    {
        GridManager.Instance.OnObjectPlaced -= HandleObjectPlaced;
        GridManager.Instance.OnObjectMoved -= HandleObjectMoved;
        GridManager.Instance.OnObjectRemoved -= HandleObjectRemoved;
        UnsubscribeFromPickupEvents();
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        GridSandSource[] sources = FindObjectsByType<GridSandSource>(FindObjectsSortMode.None);
        for (int i = 0; i < sources.Length; i++)
        {
            GridSandSource source = sources[i];
            if (!source.HasActiveConnections) continue;

            int amount = source.Tick(dt);
            if (amount <= 0) continue;

            // Transfer to each connected receiver independently
            foreach (var pair in source.ActiveConnections)
            {
                pair.Value.AddAmount(amount);
            }

            // Consume from source proportional to all receivers receiving
            source.ConsumeCurrentLayer(amount * source.ActiveConnectionCount);
        }
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

        foreach (var offset in source.GridObject.OccupiedOffsets)
        {
            Vector2Int sourceCell = origin + offset;
            if (source.IsCellConnected(sourceCell)) continue;

            for (int i = 0; i < CardinalOffsets.Length; i++)
            {
                Vector2Int neighborCell = sourceCell + CardinalOffsets[i];
                GridObject neighborObj = GridManager.Instance.GetObjectAt(neighborCell);

                if (!neighborObj || neighborObj == source.GridObject) continue;

                if (neighborObj.TryGetComponent(out GridSandReceiver receiver) && !receiver.IsConnected && source.CanConnect(receiver) && receiver.CanConnect(source))
                {
                    EstablishConnection(source, sourceCell, receiver);
                    break;
                }
            }
        }
    }

    public void EvaluateReceiverConnections(GridSandReceiver receiver)
    {
        if (receiver.IsConnected) return;

        Vector2Int origin = receiver.GridObject.GridPosition;

        foreach (var offset in receiver.GridObject.OccupiedOffsets)
        {
            Vector2Int receiverCell = origin + offset;

            for (int i = 0; i < CardinalOffsets.Length; i++)
            {
                Vector2Int neighborCell = receiverCell + CardinalOffsets[i];
                GridObject neighborObj = GridManager.Instance.GetObjectAt(neighborCell);

                if (!neighborObj || neighborObj == receiver.GridObject) continue;

                if (neighborObj.TryGetComponent(out GridSandSource source) && !source.IsCellConnected(neighborCell) && source.CanConnect(receiver) && receiver.CanConnect(source))
                {
                    EstablishConnection(source, neighborCell, receiver);
                    return;
                }
            }
        }
    }

    public bool IsCellAdjacentToObject(Vector2Int cell, GridObject obj)
    {
        Vector2Int origin = obj.GridPosition;

        foreach (var offset in obj.OccupiedOffsets)
        {
            Vector2Int objCell = origin + offset;
            for (int i = 0; i < CardinalOffsets.Length; i++)
            {
                if (objCell + CardinalOffsets[i] == cell)
                    return true;
            }
        }

        return false;
    }

    public bool AreObjectsAdjacent(GridObject a, GridObject b)
    {
        Vector2Int originA = a.GridPosition;
        Vector2Int originB = b.GridPosition;

        foreach (var offA in a.OccupiedOffsets)
        {
            Vector2Int cellA = originA + offA;
            foreach (var offB in b.OccupiedOffsets)
            {
                Vector2Int cellB = originB + offB;
                for (int i = 0; i < CardinalOffsets.Length; i++)
                {
                    if (cellA + CardinalOffsets[i] == cellB)
                        return true;
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

    private void OnDrawGizmos()
    {
        GridSandSource[] sources = FindObjectsByType<GridSandSource>(FindObjectsSortMode.None);
        foreach (var source in sources)
        {
            if (!source.HasActiveConnections) continue;

            foreach (var pair in source.ActiveConnections)
            {
                Vector2Int sourceCell = pair.Key;
                GridSandReceiver receiver = pair.Value;

                // World center of the source cell
                Vector3 fromWorld = GridManager.Instance.CellToWorldPosition(sourceCell);
                // World center of the receiver's pivot cell
                Vector3 toWorld = GridManager.Instance.CellToWorldPosition(receiver.GridObject.GridPosition);

                // Draw connection line
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(fromWorld, toWorld);

                // Draw endpoints
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(fromWorld, 0.08f);
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(toWorld, 0.08f);
            }
        }
    }
}
