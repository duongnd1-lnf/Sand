using System;
using UnityEngine;

/// <summary>
/// Logical cell types on the grid.
/// </summary>
public enum CellType
{
    Void = 0,       // Out of bounds / hole / empty space (cannot place objects)
    Floor = 1,      // Valid playable ground (can place objects)
    Obstacle = 2    // Permanent wall / obstacle (cannot place objects)
}

/// <summary>
/// Pure data representation of a single cell on the grid.
/// </summary>
[Serializable]
public class GridCellData
{
    public Vector2Int position;
    public CellType type;
    public GridObject occupant;

    public bool IsPlayable => type == CellType.Floor;

    public GridCellData(Vector2Int pos, CellType cellType)
    {
        position = pos;
        type = cellType;
        occupant = null;
    }
}
