using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Core Grid Manager: Pure Data & Logic Model.
/// Supports arbitrary grid shapes composed of square cells and polyomino-shaped GridObjects.
/// Only cells explicitly defined in GridLevelData exist on the board.
/// </summary>
public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    [Header("Input Source")]
    [SerializeField] private GridLevelData _levelData;

    [Header("Cell World Sizing (when not using LevelData)")]
    [SerializeField] private Vector2 _cellSize = Vector2.one;
    [SerializeField] private Vector2 _cellGap = Vector2.zero;

    [Header("Debug")]
    [SerializeField] private bool _drawGizmos = true;

    // Active cells on the board: Coordinate -> GridCellData
    private readonly Dictionary<Vector2Int, GridCellData> _cells = new Dictionary<Vector2Int, GridCellData>();
    private readonly Dictionary<GridObject, List<Vector2Int>> _objectFootprints = new Dictionary<GridObject, List<Vector2Int>>();

    // Events
    public event Action<GridManager> OnGridInitialized;
    public event Action<Vector2Int, GridCellData> OnCellUpdated;
    public event Action<GridObject, Vector2Int> OnObjectPlaced;
    public event Action<GridObject, Vector2Int, Vector2Int> OnObjectMoved;
    public event Action<GridObject> OnObjectRemoved;

    public GridLevelData LevelData => _levelData;
    public Vector2 CellSize => _cellSize;
    public Vector2 CellGap => _cellGap;

    public IReadOnlyDictionary<Vector2Int, GridCellData> Cells => _cells;

    private void Awake()
    {
        Instance = this;
        InitializeGrid();
    }

    /// <summary>
    /// Builds the grid cells strictly from LevelData.
    /// </summary>
    public void InitializeGrid()
    {
        _cells.Clear();
        _objectFootprints.Clear();

        if (_levelData != null)
        {
            _cellSize = _levelData.cellSize;
            _cellGap = _levelData.cellGap;
            foreach (var pos in _levelData.cells)
            {
                _cells[pos] = new GridCellData(pos, CellType.Floor);
            }
        }

        OnGridInitialized?.Invoke(this);
    }

    /// <summary>
    /// Loads a new level dataset and re-initializes the grid.
    /// </summary>
    public void LoadLevel(GridLevelData newLevelData)
    {
        _levelData = newLevelData;
        InitializeGrid();
    }

    /// <summary>
    /// Scans an existing Floor Tilemap from a Level Prefab and registers all painted floor cells into the grid.
    /// Automatically instructs GridRenderer to generate walls around the floor if needed.
    /// </summary>
    public void InitializeFromFloorTilemap(Tilemap floorTilemap)
    {
        _cells.Clear();
        _objectFootprints.Clear();

        Grid parentGrid = floorTilemap.layoutGrid;
        _cellSize = new Vector2(parentGrid.cellSize.x, parentGrid.cellSize.y);
        _cellGap = new Vector2(parentGrid.cellGap.x, parentGrid.cellGap.y);

        BoundsInt bounds = floorTilemap.cellBounds;
        foreach (var pos in bounds.allPositionsWithin)
        {
            if (floorTilemap.HasTile(pos))
            {
                Vector2Int gridPos = new Vector2Int(pos.x, pos.y);
                _cells[gridPos] = new GridCellData(gridPos, CellType.Floor);
            }
        }

        OnGridInitialized?.Invoke(this);
    }

    // -------------------------------------------------------------------------
    // Coordinate Conversions (Pure Math)
    // -------------------------------------------------------------------------

    public Vector2 GetCellStep()
    {
        return CellSize + CellGap;
    }

    public Vector2 GetCellSize()
    {
        return CellSize;
    }

    /// <summary>
    /// Calculates world position center for a single cell.
    /// </summary>
    public Vector3 CellToWorldPosition(Vector2Int cell)
    {
        return CellBoundsToWorldCenter(cell, Vector2Int.one);
    }

    /// <summary>
    /// Returns the world center of the pivot cell for a GridObject placed at originCell.
    /// Since the pivot offset is always (0,0), this is just the center of originCell itself.
    /// </summary>
    public Vector3 CellToWorldPosition(Vector2Int originCell, GridObject obj)
    {
        return CellToWorldPosition(originCell);
    }

    /// <summary>
    /// Calculates world position center for a rectangular bounding area.
    /// </summary>
    public Vector3 CellToWorldPosition(Vector2Int originCell, Vector2Int size)
    {
        return CellBoundsToWorldCenter(originCell, size);
    }

    private Vector3 CellBoundsToWorldCenter(Vector2Int minCell, Vector2Int size)
    {
        Vector2 step = GetCellStep();
        Vector2 cs = CellSize;

        float minX = minCell.x * step.x;
        float minY = minCell.y * step.y;

        float maxX = minX + size.x * cs.x + Mathf.Max(0, size.x - 1) * CellGap.x;
        float maxY = minY + size.y * cs.y + Mathf.Max(0, size.y - 1) * CellGap.y;

        return new Vector3((minX + maxX) * 0.5f, (minY + maxY) * 0.5f, transform.position.z);
    }

    /// <summary>
    /// Converts a world coordinate into the cell coordinate containing it.
    /// </summary>
    public Vector2Int WorldToCellPosition(Vector3 worldPos)
    {
        return WorldToCellPosition(worldPos, Vector2Int.one);
    }

    /// <summary>
    /// Converts the world position of a GridObject's pivot into its origin (pivot) cell.
    /// Since transform.position IS the pivot cell center, we just do a single-cell lookup.
    /// </summary>
    public Vector2Int WorldToCellPosition(Vector3 worldPos, GridObject obj)
    {
        return WorldToCellPosition(worldPos);
    }

    /// <summary>
    /// Converts a world coordinate into the closest bottom-left cell for a given size.
    /// </summary>
    public Vector2Int WorldToCellPosition(Vector3 worldPos, Vector2Int size)
    {
        Vector2 step = GetCellStep();
        Vector2 cs = CellSize;

        float totalWidth = size.x * cs.x + Mathf.Max(0, size.x - 1) * CellGap.x;
        float totalHeight = size.y * cs.y + Mathf.Max(0, size.y - 1) * CellGap.y;

        float blX = worldPos.x - totalWidth * 0.5f;
        float blY = worldPos.y - totalHeight * 0.5f;

        float sampleX = blX + cs.x * 0.5f;
        float sampleY = blY + cs.y * 0.5f;

        int cellX = Mathf.RoundToInt(sampleX / Mathf.Max(1e-4f, step.x) - 0.5f);
        int cellY = Mathf.RoundToInt(sampleY / Mathf.Max(1e-4f, step.y) - 0.5f);

        return new Vector2Int(cellX, cellY);
    }

    // -------------------------------------------------------------------------
    // Bounds & Cell Queries
    // -------------------------------------------------------------------------

    /// <summary>
    /// Checks if a coordinate is an active cell on the board.
    /// </summary>
    public bool IsInsideGrid(Vector2Int cell)
    {
        return _cells.ContainsKey(cell);
    }

    public GridCellData GetCell(Vector2Int pos)
    {
        _cells.TryGetValue(pos, out GridCellData data);
        return data;
    }

    public GridObject GetObjectAt(Vector2Int pos)
    {
        if (_cells.TryGetValue(pos, out GridCellData cell))
        {
            return cell.occupant;
        }
        return null;
    }

    public void SetCellType(Vector2Int pos, CellType newType)
    {
        if (_cells.TryGetValue(pos, out GridCellData cell))
        {
            cell.type = newType;
            OnCellUpdated?.Invoke(pos, cell);
        }
    }

    // -------------------------------------------------------------------------
    // Object Placement & Movement (Polyomino Support)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Checks whether an object can legally be placed at originCell.
    /// Every occupied cell must land on an existing Floor cell without other occupants.
    /// </summary>
    public bool CanPlaceObject(GridObject obj, Vector2Int originCell)
    {
        foreach (var offset in obj.OccupiedOffsets)
        {
            Vector2Int pos = originCell + offset;
            if (!_cells.TryGetValue(pos, out GridCellData cell))
                return false;
            if (cell.type != CellType.Floor)
                return false;
            if (cell.occupant != null && cell.occupant != obj)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Finds the nearest legal placement cell around center where obj can be placed.
    /// </summary>
    public Vector2Int FindNearestValidCell(GridObject obj, Vector2Int center)
    {
        if (CanPlaceObject(obj, center)) return center;

        for (int r = 1; r <= 20; r++)
        {
            for (int x = -r; x <= r; x++)
            {
                for (int y = -r; y <= r; y++)
                {
                    if (Mathf.Abs(x) != r && Mathf.Abs(y) != r) continue;
                    Vector2Int candidate = center + new Vector2Int(x, y);
                    if (CanPlaceObject(obj, candidate)) return candidate;
                }
            }
        }

        return center;
    }

    /// <summary>
    /// Calculates the 1D continuous corridor of valid positions along an axis starting from startCell.
    /// Stops immediately when a wall, obstacle, or narrow bottleneck is encountered.
    /// </summary>
    public void GetAxisReachableRange(GridObject obj, Vector2Int startCell, bool vertical, out int minCoord, out int maxCoord)
    {
        if (vertical)
        {
            minCoord = startCell.y;
            maxCoord = startCell.y;

            for (int y = startCell.y + 1; ; y++)
            {
                if (CanPlaceObject(obj, new Vector2Int(startCell.x, y)))
                {
                    maxCoord = y;
                }
                else
                {
                    break;
                }
            }

            for (int y = startCell.y - 1; ; y--)
            {
                if (CanPlaceObject(obj, new Vector2Int(startCell.x, y)))
                {
                    minCoord = y;
                }
                else
                {
                    break;
                }
            }
        }
        else
        {
            minCoord = startCell.x;
            maxCoord = startCell.x;

            for (int x = startCell.x + 1; ; x++)
            {
                if (CanPlaceObject(obj, new Vector2Int(x, startCell.y)))
                {
                    maxCoord = x;
                }
                else
                {
                    break;
                }
            }

            for (int x = startCell.x - 1; ; x--)
            {
                if (CanPlaceObject(obj, new Vector2Int(x, startCell.y)))
                {
                    minCoord = x;
                }
                else
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Finds all connected valid cells reachable from startCell in 2D space without passing through blocked cells or narrow bottlenecks.
    /// </summary>
    public HashSet<Vector2Int> GetReachableCells2D(GridObject obj, Vector2Int startCell)
    {
        HashSet<Vector2Int> reachable = new HashSet<Vector2Int> { startCell };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(startCell);

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            foreach (var dir in dirs)
            {
                Vector2Int next = current + dir;
                if (!reachable.Contains(next) && CanPlaceObject(obj, next))
                {
                    reachable.Add(next);
                    queue.Enqueue(next);
                }
            }
        }

        return reachable;
    }

    /// <summary>
    /// Determines the best valid cell reachable from startCell closest to desiredCell according to the object's movement constraints.
    /// Prevents crossing through narrow bottlenecks or obstacles.
    /// </summary>
    public Vector2Int GetBestReachableCell(GridObject obj, Vector2Int startCell, Vector2Int desiredCell)
    {
        switch (obj.AxisConstraint)
        {
            case MoveAxisConstraint.VerticalOnly:
            {
                GetAxisReachableRange(obj, startCell, vertical: true, out int minY, out int maxY);
                int targetY = Mathf.Clamp(desiredCell.y, minY, maxY);
                return new Vector2Int(startCell.x, targetY);
            }
            case MoveAxisConstraint.HorizontalOnly:
            {
                GetAxisReachableRange(obj, startCell, vertical: false, out int minX, out int maxX);
                int targetX = Mathf.Clamp(desiredCell.x, minX, maxX);
                return new Vector2Int(targetX, startCell.y);
            }
            case MoveAxisConstraint.Free2D:
            default:
            {
                HashSet<Vector2Int> reachable = GetReachableCells2D(obj, startCell);
                if (reachable.Contains(desiredCell)) return desiredCell;

                Vector2Int best = startCell;
                float minDistSq = float.MaxValue;
                foreach (var cell in reachable)
                {
                    float distSq = (cell - desiredCell).sqrMagnitude;
                    if (distSq < minDistSq)
                    {
                        minDistSq = distSq;
                        best = cell;
                    }
                }
                return best;
            }
        }
    }

    /// <summary>
    /// Attempts to place an object at the target origin cell.
    /// </summary>
    public bool TryPlaceObject(GridObject obj, Vector2Int originCell, bool animate = true)
    {
        if (!CanPlaceObject(obj, originCell)) return false;

        Vector2Int oldPos = obj.GridPosition;
        bool isMove = _objectFootprints.ContainsKey(obj);

        // Remove from previous cells
        RemoveObjectFromOccupancy(obj);

        // Register in new cells
        List<Vector2Int> newFootprint = new List<Vector2Int>();

        foreach (var offset in obj.OccupiedOffsets)
        {
            Vector2Int pos = originCell + offset;
            if (_cells.TryGetValue(pos, out GridCellData cell))
            {
                cell.occupant = obj;
                newFootprint.Add(pos);
                OnCellUpdated?.Invoke(pos, cell);
            }
        }

        _objectFootprints[obj] = newFootprint;
        obj.SetGridPosition(originCell);

        Vector3 targetWorldPos = CellToWorldPosition(originCell, obj);
        obj.SnapToWorldPosition(targetWorldPos, animate);

        if (isMove && oldPos != originCell)
        {
            OnObjectMoved?.Invoke(obj, oldPos, originCell);
        }
        else
        {
            OnObjectPlaced?.Invoke(obj, originCell);
        }

        return true;
    }

    /// <summary>
    /// Removes an object completely from the grid.
    /// </summary>
    public void RemoveObject(GridObject obj)
    {
        RemoveObjectFromOccupancy(obj);
        OnObjectRemoved?.Invoke(obj);
    }

    private void RemoveObjectFromOccupancy(GridObject obj)
    {
        if (_objectFootprints.TryGetValue(obj, out List<Vector2Int> footprint))
        {
            foreach (var pos in footprint)
            {
                if (_cells.TryGetValue(pos, out GridCellData cell) && cell.occupant == obj)
                {
                    cell.occupant = null;
                    OnCellUpdated?.Invoke(pos, cell);
                }
            }
            _objectFootprints.Remove(obj);
        }
    }

    // -------------------------------------------------------------------------
    // Debug Gizmos
    // -------------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (!_drawGizmos) return;

        Vector2 cs = CellSize;

        // Draw only cells that exist on the board
        foreach (var kvp in _cells)
        {
            Vector2Int cellPos = kvp.Key;
            GridCellData data = kvp.Value;
            Vector3 center = CellToWorldPosition(cellPos, Vector2Int.one);

            Color c = Color.gray;
            if (data.type == CellType.Void) c = new Color(0.15f, 0.15f, 0.15f, 0.2f);
            else if (data.type == CellType.Obstacle) c = new Color(0.85f, 0.25f, 0.25f, 0.6f);
            else if (data.occupant != null) c = new Color(0.2f, 0.85f, 0.35f, 0.6f);
            else c = new Color(0.2f, 0.6f, 1f, 0.35f);

            Gizmos.color = c;
            Gizmos.DrawWireCube(center, new Vector3(cs.x, cs.y, 0.05f));
        }
    }
}
