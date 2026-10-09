using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages automatic proximity-based connections between GridSandSource and GridSandReceiver.
/// Continuously searches within a configurable radius around each receiver for matching sand colors.
/// Connects to the nearest candidate source cell and streams sand dynamically as objects move.
/// </summary>
public class GridSandConnector : MonoBehaviour
{
    [Header("Connection Range Settings")]
    [SerializeField] private float _connectionRange = 2.0f;
    [SerializeField] private float _disconnectBuffer = 0.35f;
    [SerializeField] private bool _continuousEvaluation = true;
    [SerializeField] private bool _autoConnectOnPlaced = true;

    public float ConnectionRange => _connectionRange;

    private void Start()
    {
        GridManager.Instance.OnObjectPlaced += HandleObjectPlaced;
        GridManager.Instance.OnObjectMoved += HandleObjectMoved;
        GridManager.Instance.OnObjectRemoved += HandleObjectRemoved;
        EvaluateAllGridConnections();
    }

    private void OnDestroy()
    {
        GridManager.Instance.OnObjectPlaced -= HandleObjectPlaced;
        GridManager.Instance.OnObjectMoved -= HandleObjectMoved;
        GridManager.Instance.OnObjectRemoved -= HandleObjectRemoved;
    }

    private void Update()
    {
        if (_continuousEvaluation)
        {
            EvaluateActiveConnections();
            EvaluatePendingConnections();
        }
    }

    private void HandleObjectPlaced(GridObject obj, Vector2Int cell)
    {
        if (!_autoConnectOnPlaced) return;
        EvaluateAllGridConnections();
    }

    private void HandleObjectMoved(GridObject obj, Vector2Int oldCell, Vector2Int newCell)
    {
        EvaluateAllGridConnections();
    }

    private void HandleObjectRemoved(GridObject obj)
    {
        if (obj.TryGetComponent(out GridSandSource source))
        {
            source.DisconnectAll();
        }

        if (obj.TryGetComponent(out GridSandReceiver receiver) && receiver.IsConnected)
        {
            BreakReceiverConnection(receiver);
        }
    }

    [ContextMenu("Evaluate All Connections")]
    public void EvaluateAllGridConnections()
    {
        EvaluateActiveConnections();
        EvaluatePendingConnections();
    }

    /// <summary>
    /// Validates existing connections and breaks them if out of range, full, empty, or no sand left in simulation.
    /// </summary>
    public void EvaluateActiveConnections()
    {
        GridSandSource[] sources = FindObjectsByType<GridSandSource>(FindObjectsSortMode.None);
        float maxAllowedDist = (_connectionRange + _disconnectBuffer) * GetCellStepMagnitude();

        for (int s = 0; s < sources.Length; s++)
        {
            GridSandSource source = sources[s];
            if (!source.HasActiveConnections) continue;

            var activePairs = new List<KeyValuePair<Vector2Int, GridSandReceiver>>(source.ActiveConnections);
            for (int i = 0; i < activePairs.Count; i++)
            {
                Vector2Int sourceCell = activePairs[i].Key;
                GridSandReceiver receiver = activePairs[i].Value;

                bool shouldDisconnect = receiver.IsFull
                                     || !source.CanConnect(receiver)
                                     || GetMinDistance(source, sourceCell, receiver) > maxAllowedDist
                                     || !source.HasRealSandFor(sourceCell, receiver);

                if (shouldDisconnect)
                {
                    source.Disconnect(receiver);
                    receiver.Disconnect();
                }
            }
        }
    }

    /// <summary>
    /// Searches for matching sources with real sand in simulation within _connectionRange around unconnected receivers and connects the closest one.
    /// </summary>
    public void EvaluatePendingConnections()
    {
        GridSandReceiver[] receivers = FindObjectsByType<GridSandReceiver>(FindObjectsSortMode.None);
        GridSandSource[] sources = FindObjectsByType<GridSandSource>(FindObjectsSortMode.None);
        float maxRangeDist = _connectionRange * GetCellStepMagnitude();

        for (int r = 0; r < receivers.Length; r++)
        {
            GridSandReceiver receiver = receivers[r];
            if (receiver.IsConnected || receiver.IsFull) continue;

            GridSandSource bestSource = null;
            Vector2Int bestSourceCell = Vector2Int.zero;
            float minDistance = maxRangeDist;

            for (int s = 0; s < sources.Length; s++)
            {
                GridSandSource source = sources[s];
                if (!source.CanConnect(receiver)) continue;

                Vector2Int origin = source.GridObject.GridPosition;
                foreach (var offset in source.GridObject.OccupiedOffsets)
                {
                    Vector2Int sourceCell = origin + offset;
                    if (source.IsCellConnected(sourceCell)) continue;

                    float dist = GetMinDistance(source, sourceCell, receiver);
                    if (dist <= minDistance)
                    {
                        if (!source.HasRealSandFor(sourceCell, receiver)) continue;

                        minDistance = dist;
                        bestSource = source;
                        bestSourceCell = sourceCell;
                    }
                }
            }

            if (bestSource != null)
            {
                EstablishConnection(bestSource, bestSourceCell, receiver);
            }
        }
    }

    public void EvaluateObjectConnections(GridObject obj)
    {
        EvaluateAllGridConnections();
    }

    public void EvaluateSourceConnections(GridSandSource source)
    {
        EvaluateAllGridConnections();
    }

    public void EvaluateReceiverConnections(GridSandReceiver receiver)
    {
        EvaluateAllGridConnections();
    }

    public float GetMinDistance(GridSandSource source, Vector2Int sourceCell, GridSandReceiver receiver)
    {
        Vector3 sourceWorldPos = source.GridObject.GetCellWorldPosition(sourceCell);
        Vector2Int receiverOrigin = receiver.GridObject.GridPosition;
        float minDistance = float.MaxValue;

        foreach (var offset in receiver.GridObject.OccupiedOffsets)
        {
            Vector2Int receiverCell = receiverOrigin + offset;
            Vector3 receiverWorldPos = receiver.GridObject.GetCellWorldPosition(receiverCell);
            float dist = Vector2.Distance(sourceWorldPos, receiverWorldPos);
            if (dist < minDistance)
            {
                minDistance = dist;
            }
        }

        return minDistance;
    }

    private float GetCellStepMagnitude()
    {
        GridManager gm = GridManager.Instance;
        return gm != null ? gm.CellSize.x : 1f;
    }

    private void EstablishConnection(GridSandSource source, Vector2Int sourceCell, GridSandReceiver receiver)
    {
        source.Connect(sourceCell, receiver);
        receiver.Connect(source);
    }

    private void BreakReceiverConnection(GridSandReceiver receiver)
    {
        GridSandSource source = receiver.ConnectedSource;
        receiver.Disconnect();
        if (source != null)
        {
            source.Disconnect(receiver);
        }
    }

    private void OnDrawGizmos()
    {
        float cellSize = GetCellStepMagnitude();
        float rangeWorld = _connectionRange * cellSize;

        // Draw detection radii around receivers
        GridSandReceiver[] receivers = FindObjectsByType<GridSandReceiver>(FindObjectsSortMode.None);
        for (int i = 0; i < receivers.Length; i++)
        {
            GridSandReceiver r = receivers[i];
            Vector3 center = r.GridObject != null
                ? r.GridObject.GetCellWorldPosition(r.GridObject.GridPosition)
                : r.transform.position;

            Color gizmoColor = r.TargetColor != null ? r.TargetColor.Get(0) : Color.cyan;
            Gizmos.color = new Color(gizmoColor.r, gizmoColor.g, gizmoColor.b, 0.25f);
            Gizmos.DrawWireSphere(center, rangeWorld);
        }

        // Draw active connections
        GridSandSource[] sources = FindObjectsByType<GridSandSource>(FindObjectsSortMode.None);
        for (int i = 0; i < sources.Length; i++)
        {
            GridSandSource source = sources[i];
            if (!source.HasActiveConnections) continue;

            foreach (var pair in source.ActiveConnections)
            {
                Vector2Int sourceCell = pair.Key;
                GridSandReceiver receiver = pair.Value;

                Vector3 fromWorld = source.GridObject != null
                    ? source.GridObject.GetCellWorldPosition(sourceCell)
                    : source.transform.position;

                Vector3 toWorld = receiver.GridObject != null
                    ? receiver.GridObject.GetCellWorldPosition(receiver.GridObject.GridPosition)
                    : receiver.transform.position;

                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(fromWorld, toWorld);

                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(fromWorld, 0.08f);
                Gizmos.color = Color.green;
                Gizmos.DrawSphere(toWorld, 0.08f);
            }
        }
    }
}
