using System;
using UnityEngine;

/// <summary>
/// Represents a connection point along a cell border.
/// Specifies the source cell and the border direction (Side) connecting to a receiver.
/// </summary>
[Serializable]
public struct GridConnectionPoint : IEquatable<GridConnectionPoint>
{
    public Vector2Int cell;
    public Side side;

    public Vector2Int Direction
    {
        get
        {
            switch (side)
            {
                case Side.Bottom: return Vector2Int.down;
                case Side.Top: return Vector2Int.up;
                case Side.Left: return Vector2Int.left;
                case Side.Right: return Vector2Int.right;
                default: return Vector2Int.down;
            }
        }
    }

    public GridConnectionPoint(Vector2Int cell, Side side)
    {
        this.cell = cell;
        this.side = side;
    }

    /// <summary>
    /// Calculates the world-space dividing line segment of this cell border.
    /// </summary>
    public void GetBorderSegment(GridManager gm, out Vector3 start, out Vector3 end)
    {
        Vector3 center = gm != null ? gm.CellToWorldPosition(cell) : new Vector3(cell.x, cell.y, 0f);
        Vector2 halfSize = (gm != null ? gm.CellSize : Vector2.one) * 0.5f;

        switch (side)
        {
            case Side.Bottom:
                start = center + new Vector3(-halfSize.x, -halfSize.y, 0f);
                end   = center + new Vector3( halfSize.x, -halfSize.y, 0f);
                break;
            case Side.Top:
                start = center + new Vector3(-halfSize.x,  halfSize.y, 0f);
                end   = center + new Vector3( halfSize.x,  halfSize.y, 0f);
                break;
            case Side.Left:
                start = center + new Vector3(-halfSize.x, -halfSize.y, 0f);
                end   = center + new Vector3(-halfSize.x,  halfSize.y, 0f);
                break;
            case Side.Right:
            default:
                start = center + new Vector3( halfSize.x, -halfSize.y, 0f);
                end   = center + new Vector3( halfSize.x,  halfSize.y, 0f);
                break;
        }
    }

    public Vector3 GetBorderCenter(GridManager gm)
    {
        GetBorderSegment(gm, out Vector3 s, out Vector3 e);
        return (s + e) * 0.5f;
    }

    public bool Equals(GridConnectionPoint other)
    {
        return cell.Equals(other.cell) && side == other.side;
    }

    public override bool Equals(object obj)
    {
        return obj is GridConnectionPoint other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(cell, (int)side);
    }

    public static bool operator ==(GridConnectionPoint left, GridConnectionPoint right) => left.Equals(right);
    public static bool operator !=(GridConnectionPoint left, GridConnectionPoint right) => !left.Equals(right);

    public override string ToString() => $"{cell} [{side}]";
}
