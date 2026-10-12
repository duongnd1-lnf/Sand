using System;
using System.Collections.Generic;
using UnityEngine;

public enum GridRotation
{
    Rot_0 = 0,
    Rot_90 = 90,
    Rot_180 = 180,
    Rot_270 = 270
}

[Serializable]
public class LevelObjectConfig
{
    public string key;
    public Vector2Int originCell;
    public GridRotation rotation = GridRotation.Rot_0;
}

[Serializable]
public class LevelSourceConfig
{
    public string key = "source";
    public Vector2Int originCell = Vector2Int.zero;
    public GridRotation rotation = GridRotation.Rot_0;
    public bool isDraggable = false;
    public List<GridSandSource.SandLayer> layers = new List<GridSandSource.SandLayer>();

    public Vector2Int GetOriginCell() => originCell;
}

/// <summary>
/// ScriptableObject defining arbitrary-shaped level layouts and initial object placements.
/// </summary>
[CreateAssetMenu(fileName = "NewLevelData", menuName = "Game/Grid Level Data", order = 1)]
public class GridLevelData : ScriptableObject
{
    [Header("Cell World Sizing")]
    public Vector2 cellSize = Vector2.one;
    public Vector2 cellGap = Vector2.zero;

    [Header("Grid Cells (Coordinates in Grid)")]
    public List<Vector2Int> cells = new List<Vector2Int>();

    [Header("Level Objects")]
    public List<LevelObjectConfig> objects = new List<LevelObjectConfig>();

    [Header("Level Sand Sources")]
    public List<LevelSourceConfig> sources = new List<LevelSourceConfig>();

    /// <summary>
    /// Checks whether a coordinate belongs to this grid.
    /// </summary>
    public bool HasCell(Vector2Int pos)
    {
        return cells.Contains(pos);
    }

    /// <summary>
    /// Convenience helper to generate a default rectangular grid.
    /// </summary>
    [ContextMenu("Generate 8x8 Default")]
    public void GenerateDefault8x8Floor()
    {
        GenerateRectangular(Vector2Int.zero, new Vector2Int(8, 8));
    }

    public void GenerateRectangular(Vector2Int origin, Vector2Int size)
    {
        cells.Clear();
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                cells.Add(origin + new Vector2Int(x, y));
            }
        }
    }
}
