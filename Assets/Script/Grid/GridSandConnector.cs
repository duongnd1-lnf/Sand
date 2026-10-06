using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controller responsible for automatic proximity connection between Source and Receiver on the Grid.
/// When a receiver is moved to a new cell, it searches adjacent cells (Up, Down, Left, Right).
/// If a Source matching all conditions is found, it connects them:
/// - Direction matching: relative position maps to allowed source output side.
/// - Color matching: source provides the sand type accepted by the receiver.
/// - Capacity check: receiver is not already full.
/// </summary>
public class GridSandConnector : MonoBehaviour
{
    private readonly struct NeighborDirection
    {
        public readonly Vector2Int offset;
        public readonly SandPool.Side sourceFromSide;
        public readonly SandPool.Side receiverToSide;

        public NeighborDirection(Vector2Int offset, SandPool.Side fromSide, SandPool.Side toSide)
        {
            this.offset = offset;
            this.sourceFromSide = fromSide;
            this.receiverToSide = toSide;
        }
    }

    private static readonly NeighborDirection[] Directions = new NeighborDirection[]
    {
        // Source is ABOVE receiver -> flows from Source.Bottom into Receiver.Top
        new NeighborDirection(Vector2Int.up, SandPool.Side.Bottom, SandPool.Side.Top),
        // Source is BELOW receiver -> flows from Source.Top into Receiver.Bottom
        new NeighborDirection(Vector2Int.down, SandPool.Side.Top, SandPool.Side.Bottom),
        // Source is LEFT of receiver -> flows from Source.Right into Receiver.Left
        new NeighborDirection(Vector2Int.left, SandPool.Side.Right, SandPool.Side.Left),
        // Source is RIGHT of receiver -> flows from Source.Left into Receiver.Right
        new NeighborDirection(Vector2Int.right, SandPool.Side.Left, SandPool.Side.Right),
    };

    [Header("Dependencies")]
    [SerializeField] private GridManager _gridManager;

    [Header("Connection Conditions")]
    [SerializeField] private bool _requireColorMatch = true;

    [SerializeField] private bool _autoConnectOnPlaced = true;

    private readonly Dictionary<SandReceiverPool, SandSourcePool> _activeConnections = new Dictionary<SandReceiverPool, SandSourcePool>();

    public GridManager Manager { get => _gridManager; set => _gridManager = value; }
    public bool RequireColorMatch { get => _requireColorMatch; set => _requireColorMatch = value; }
    public bool AutoConnectOnPlaced { get => _autoConnectOnPlaced; set => _autoConnectOnPlaced = value; }

    private void OnEnable()
    {
        if (_gridManager != null)
        {
            _gridManager.OnObjectPlaced += HandleObjectPlaced;
            _gridManager.OnObjectRemoved += HandleObjectRemoved;
        }
    }

    private void OnDisable()
    {
        if (_gridManager != null)
        {
            _gridManager.OnObjectPlaced -= HandleObjectPlaced;
            _gridManager.OnObjectRemoved -= HandleObjectRemoved;
        }
    }

    private void HandleObjectPlaced(GridObject obj, Vector2Int cell)
    {
        if (!_autoConnectOnPlaced || _gridManager == null) return;

        SandReceiverPool receiverPool = obj.GetComponent<SandReceiverPool>();
        if (receiverPool != null)
        {
            EvaluateReceiver(receiverPool, cell);
            return;
        }

        SandSourcePool sourcePool = obj.GetComponent<SandSourcePool>();
        if (sourcePool != null)
        {
            EvaluateSource(sourcePool, cell);
        }
    }

    private void HandleObjectRemoved(GridObject obj)
    {
        SandReceiverPool receiverPool = obj.GetComponent<SandReceiverPool>();
        if (receiverPool != null)
        {
            DisconnectReceiver(receiverPool);
            return;
        }

        SandSourcePool sourcePool = obj.GetComponent<SandSourcePool>();
        if (sourcePool != null)
        {
            sourcePool.Disconnect();
            RemoveSourceFromConnections(sourcePool);
        }
    }

    /// <summary>
    /// Searches around receiverCell for an adjacent Source that matches all conditions,
    /// and connects them if found. Disconnects if no valid source is adjacent.
    /// </summary>
    public bool EvaluateReceiver(SandReceiverPool receiverPool, Vector2Int receiverCell)
    {
        DisconnectReceiver(receiverPool);

        if (receiverPool == null || receiverPool.IsFull)
            return false;

        int acceptedType = receiverPool.GetAcceptedType();

        foreach (var dir in Directions)
        {
            Vector2Int neighborCell = receiverCell + dir.offset;
            GridObject neighborObj = _gridManager.GetObjectAt(neighborCell);
            if (neighborObj == null) continue;

            SandSourcePool sourcePool = neighborObj.GetComponent<SandSourcePool>();
            if (sourcePool != null)
            {
                // Condition 1: Source allows output from this direction
                if (!sourcePool.IsDirectionAllowed(dir.sourceFromSide))
                    continue;

                // Condition 2: Color match
                if (_requireColorMatch && !sourcePool.HasSandType(acceptedType))
                    continue;

                // Connect source to receiver
                bool connected = sourcePool.Connect(receiverPool, dir.sourceFromSide, dir.receiverToSide);
                if (connected)
                {
                    _activeConnections[receiverPool] = sourcePool;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// When a Source is placed, searches around its adjacent cells for any Receiver needing sand.
    /// </summary>
    public bool EvaluateSource(SandSourcePool sourcePool, Vector2Int sourceCell)
    {
        if (sourcePool == null) return false;

        sourcePool.Disconnect();
        RemoveSourceFromConnections(sourcePool);

        foreach (var dir in Directions)
        {
            Vector2Int neighborCell = sourceCell + dir.offset;
            GridObject neighborObj = _gridManager.GetObjectAt(neighborCell);
            if (neighborObj == null) continue;

            SandReceiverPool receiverPool = neighborObj.GetComponent<SandReceiverPool>();
            if (receiverPool != null)
            {
                if (receiverPool.IsFull) continue;

                SandPool.Side fromSide = GetSourceToNeighborSide(dir.offset);
                SandPool.Side toSide = GetOppositeSide(fromSide);

                if (!sourcePool.IsDirectionAllowed(fromSide))
                    continue;

                if (_requireColorMatch && !sourcePool.HasSandType(receiverPool.GetAcceptedType()))
                    continue;

                bool connected = sourcePool.Connect(receiverPool, fromSide, toSide);
                if (connected)
                {
                    _activeConnections[receiverPool] = sourcePool;
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Disconnects any active source connected to this receiver.
    /// </summary>
    public void DisconnectReceiver(SandReceiverPool receiverPool)
    {
        if (receiverPool == null) return;

        if (_activeConnections.TryGetValue(receiverPool, out SandSourcePool source))
        {
            if (source != null && source.ActiveReceiver == receiverPool)
            {
                source.Disconnect();
            }
            _activeConnections.Remove(receiverPool);
        }
    }

    private void RemoveSourceFromConnections(SandSourcePool sourcePool)
    {
        List<SandReceiverPool> toRemove = new List<SandReceiverPool>();
        foreach (var kvp in _activeConnections)
        {
            if (kvp.Value == sourcePool) toRemove.Add(kvp.Key);
        }
        foreach (var r in toRemove) _activeConnections.Remove(r);
    }

    private SandPool.Side GetSourceToNeighborSide(Vector2Int offset)
    {
        if (offset == Vector2Int.down) return SandPool.Side.Bottom;
        if (offset == Vector2Int.up) return SandPool.Side.Top;
        if (offset == Vector2Int.left) return SandPool.Side.Left;
        return SandPool.Side.Right;
    }

    private SandPool.Side GetOppositeSide(SandPool.Side side)
    {
        switch (side)
        {
            case SandPool.Side.Bottom: return SandPool.Side.Top;
            case SandPool.Side.Top: return SandPool.Side.Bottom;
            case SandPool.Side.Left: return SandPool.Side.Right;
            default: return SandPool.Side.Left;
        }
    }
}
