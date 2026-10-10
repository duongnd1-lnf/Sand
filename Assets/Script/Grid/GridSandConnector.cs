using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates connections across the grid between GridSandSource and GridSandReceiver.
/// Scans adjacent grid cells when objects are placed or moved, and manages connections.
/// </summary>
public class GridSandConnector : MonoBehaviour
{
    public static GridSandConnector Instance { get; private set; }

    private static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up,
        Vector2Int.down,
        Vector2Int.left,
        Vector2Int.right
    };

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (GridManager.Instance != null)
        {
            GridManager.Instance.OnObjectPlaced += HandleObjectPlaced;
            GridManager.Instance.OnObjectMoved += HandleObjectMoved;
            GridManager.Instance.OnObjectRemoved += HandleObjectRemoved;
        }
    }

    private void OnDestroy()
    {
        if (GridManager.Instance != null)
        {
            GridManager.Instance.OnObjectPlaced -= HandleObjectPlaced;
            GridManager.Instance.OnObjectMoved -= HandleObjectMoved;
            GridManager.Instance.OnObjectRemoved -= HandleObjectRemoved;
        }
        if (Instance == this) Instance = null;
    }

    private void HandleObjectPlaced(GridObject obj, Vector2Int cell)
    {
        if (obj.IsReceiver)
        {
            EvaluateReceiverConnections(obj.Receiver);
        }
    }

    private void HandleObjectMoved(GridObject obj, Vector2Int oldCell, Vector2Int newCell)
    {
        if (obj.IsReceiver)
        {
            EvaluateReceiverConnections(obj.Receiver);
        }
    }

    private void HandleObjectRemoved(GridObject obj)
    {
        if (obj.IsReceiver)
        {
            obj.Receiver.Disconnect();
        }
    }

    public void EvaluateAllGridConnections()
    {
        GridManager gm = GridManager.Instance;
        if (gm == null) return;

        foreach (var obj in gm.PlacedObjects)
        {
            if (obj.IsReceiver)
            {
                EvaluateReceiverConnections(obj.Receiver);
            }
        }
    }

    public void EvaluateSourceConnections(GridSandSource source)
    {
        EvaluateAllGridConnections();
    }

    public void EvaluateReceiverConnections(GridSandReceiver receiver)
    {
        GridManager gm = GridManager.Instance;
        if (gm == null || receiver == null || receiver.GridObject == null) return;

        GridObject gridObj = receiver.GridObject;
        Dictionary<GridSandSource, HashSet<GridConnectionPoint>> sourceToPoints = new Dictionary<GridSandSource, HashSet<GridConnectionPoint>>();

        foreach (var offset in gridObj.OccupiedOffsets)
        {
            Vector2Int rPos = gridObj.GridPosition + offset;
            foreach (var dir in CardinalDirections)
            {
                Vector2Int neighborPos = rPos + dir;
                GridObject neighborObj = gm.GetObjectAt(neighborPos);
                if (neighborObj != null && neighborObj != gridObj && neighborObj.IsSource)
                {
                    GridSandSource source = neighborObj.Source;
                    if (!sourceToPoints.TryGetValue(source, out var points))
                    {
                        points = new HashSet<GridConnectionPoint>();
                        sourceToPoints[source] = points;
                    }

                    // Direction from source cell to receiver cell determines the contact side on the source
                    Vector2Int dirFromSource = rPos - neighborPos;
                    Side sideOnSource = DirectionToSide(dirFromSource);
                    points.Add(new GridConnectionPoint(neighborPos, sideOnSource));
                }
            }
        }

        if (sourceToPoints.Count > 0)
        {
            using var enumerator = sourceToPoints.GetEnumerator();
            enumerator.MoveNext();
            var targetSource = enumerator.Current.Key;
            var connectionPoints = new List<GridConnectionPoint>(enumerator.Current.Value);

            receiver.Connect(targetSource, connectionPoints);
        }
        else
        {
            receiver.Disconnect();
        }
    }

    public static Side DirectionToSide(Vector2Int dir)
    {
        if (dir == Vector2Int.down) return Side.Bottom;
        if (dir == Vector2Int.up) return Side.Top;
        if (dir == Vector2Int.left) return Side.Left;
        if (dir == Vector2Int.right) return Side.Right;
        return Side.Bottom;
    }
}
