using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Manages the 2D grid logic, coordinate conversions, cell occupancy, and object placement.
/// Uses Unity's Grid and Tilemap for background reference and world-to-cell transformations.
/// </summary>
public class GridManager : MonoBehaviour
{
    public enum BoundaryMode
    {
        ManualBounds,   // Explicit width and height in cells
        TilemapBounds,  // Derived from the assigned Tilemap
        Unbounded       // No boundaries (infinite grid)
    }

    [Header("Grid Setup")]
    [Tooltip("Reference to the Unity Grid component. If null, will look on this GameObject or in scene.")]
    [SerializeField] private Grid _grid;

    [Tooltip("Optional background Tilemap used as visual grid layout.")]
    [SerializeField] private Tilemap _tilemap;

    [Header("Boundaries")]
    [SerializeField] private BoundaryMode _boundaryMode = BoundaryMode.ManualBounds;
    [SerializeField] private Vector2Int _gridOrigin = Vector2Int.zero;
    [SerializeField] private Vector2Int _gridSize = new Vector2Int(10, 10);

    [Header("Auto Registration")]
    [Tooltip("If true, automatically detects and registers all GridObjects in scene on Start.")]
    [SerializeField] private bool _autoRegisterOnStart = true;

    [Header("Debug Visualization")]
    [SerializeField] private bool _drawGizmos = true;
    [SerializeField] private Color _gridBorderColor = new Color(0.2f, 0.8f, 1f, 0.5f);
    [SerializeField] private Color _occupiedCellColor = new Color(1f, 0.3f, 0.2f, 0.4f);
    [SerializeField] private Color _emptyCellColor = new Color(0.2f, 1f, 0.4f, 0.15f);

    // Dictionary tracking which cell is occupied by which GridObject
    private readonly Dictionary<Vector2Int, GridObject> _occupiedCells = new Dictionary<Vector2Int, GridObject>();

    // Events for placement feedback
    public event Action<GridObject, Vector2Int> OnObjectPlaced;
    public event Action<GridObject> OnObjectRemoved;

    public Grid TargetGrid => _grid;
    public Tilemap BackgroundTilemap => _tilemap;
    public BoundaryMode CurrentBoundaryMode => _boundaryMode;
    public Vector2Int GridOrigin => _gridOrigin;
    public Vector2Int GridSize => _gridSize;

    private void Awake()
    {
        EnsureGridReference();
        RefreshBoundaries();
    }

    private void Start()
    {
        if (_autoRegisterOnStart)
        {
            RegisterAllSceneObjects();
        }
    }

    /// <summary>
    /// Ensures a valid Grid component reference exists.
    /// </summary>
    public void EnsureGridReference()
    {
        if (_grid == null)
        {
            if (_tilemap != null && _tilemap.layoutGrid != null)
            {
                _grid = _tilemap.layoutGrid;
            }
            else
            {
                _grid = GetComponent<Grid>();
                if (_grid == null)
                {
                    _grid = GetComponentInParent<Grid>();
                }
                if (_grid == null)
                {
                    _grid = FindFirstObjectByType<Grid>();
                }
            }
        }
    }

    /// <summary>
    /// Refreshes grid boundaries if derived from the background Tilemap.
    /// </summary>
    public void RefreshBoundaries()
    {
        if (_boundaryMode == BoundaryMode.TilemapBounds && _tilemap != null)
        {
            _tilemap.CompressBounds();
            BoundsInt bounds = _tilemap.cellBounds;
            _gridOrigin = new Vector2Int(bounds.xMin, bounds.yMin);
            _gridSize = new Vector2Int(bounds.size.x, bounds.size.y);
        }
    }

    /// <summary>
    /// Registers all active GridObjects in the scene to their nearest grid positions.
    /// </summary>
    public void RegisterAllSceneObjects()
    {
        GridObject[] allObjects = FindObjectsByType<GridObject>(FindObjectsSortMode.None);
        foreach (GridObject obj in allObjects)
        {
            if (obj == null) continue;
            obj.Initialize(this);

            Vector2Int initialCell = WorldToCellPosition(obj.transform.position, obj.Size);
            if (CanPlaceObject(obj, initialCell))
            {
                TryPlaceObject(obj, initialCell, animate: false);
            }
            else
            {
                Debug.LogWarning($"[GridManager] Could not register '{obj.name}' at cell {initialCell}: Cell is occupied or out of bounds.", obj);
            }
        }
    }

    #region Coordinate Conversion

    /// <summary>
    /// Gets the cell size including any gap.
    /// </summary>
    public Vector2 GetCellSize()
    {
        if (_grid != null)
        {
            return new Vector2(_grid.cellSize.x + _grid.cellGap.x, _grid.cellSize.y + _grid.cellGap.y);
        }
        return Vector2.one;
    }

    /// <summary>
    /// Calculates the world center position for an object of given size with bottom-left cell at origin.
    /// </summary>
    public Vector3 CellToWorldPosition(Vector2Int originCell, Vector2Int size)
    {
        EnsureGridReference();
        if (_grid == null)
        {
            Vector2 cs = GetCellSize();
            return new Vector3(
                (originCell.x + size.x * 0.5f) * cs.x,
                (originCell.y + size.y * 0.5f) * cs.y,
                0f
            );
        }

        Vector3 minCorner = _grid.CellToWorld(new Vector3Int(originCell.x, originCell.y, 0));
        Vector3 maxCorner = _grid.CellToWorld(new Vector3Int(originCell.x + size.x, originCell.y + size.y, 0));
        Vector3 center = (minCorner + maxCorner) * 0.5f;
        center.z = 0f;
        return center;
    }

    /// <summary>
    /// Converts a world center position to the bottom-left origin cell for an object of given size.
    /// </summary>
    public Vector2Int WorldToCellPosition(Vector3 worldPos, Vector2Int size)
    {
        EnsureGridReference();
        Vector2 cellSize = GetCellSize();

        if (_grid == null)
        {
            Vector3 worldBL = worldPos - new Vector3(size.x * cellSize.x * 0.5f, size.y * cellSize.y * 0.5f, 0f);
            int cx = Mathf.FloorToInt((worldBL.x + cellSize.x * 0.5f) / cellSize.x);
            int cy = Mathf.FloorToInt((worldBL.y + cellSize.y * 0.5f) / cellSize.y);
            return new Vector2Int(cx, cy);
        }

        Vector3 localPos = _grid.transform.InverseTransformPoint(worldPos);
        Vector3 localHalfSize = new Vector3(size.x * cellSize.x * 0.5f, size.y * cellSize.y * 0.5f, 0f);
        Vector3 localBL = localPos - localHalfSize;
        Vector3 localSamplePoint = localBL + new Vector3(cellSize.x * 0.5f, cellSize.y * 0.5f, 0f);
        Vector3 worldSamplePoint = _grid.transform.TransformPoint(localSamplePoint);

        Vector3Int cellInt = _grid.WorldToCell(worldSamplePoint);
        return new Vector2Int(cellInt.x, cellInt.y);
    }

    #endregion

    #region Grid Bounds & Occupancy

    /// <summary>
    /// Checks whether a specific single cell is inside the active grid bounds.
    /// </summary>
    public bool IsCellInsideGrid(Vector2Int cell)
    {
        if (_boundaryMode == BoundaryMode.Unbounded)
        {
            return true;
        }

        return cell.x >= _gridOrigin.x &&
               cell.x < _gridOrigin.x + _gridSize.x &&
               cell.y >= _gridOrigin.y &&
               cell.y < _gridOrigin.y + _gridSize.y;
    }

    /// <summary>
    /// Checks whether the entire area required for an object of given size is inside the grid bounds.
    /// </summary>
    public bool IsAreaInsideGrid(Vector2Int origin, Vector2Int size)
    {
        if (_boundaryMode == BoundaryMode.Unbounded)
        {
            return true;
        }

        return origin.x >= _gridOrigin.x &&
               origin.x + size.x <= _gridOrigin.x + _gridSize.x &&
               origin.y >= _gridOrigin.y &&
               origin.y + size.y <= _gridOrigin.y + _gridSize.y;
    }

    /// <summary>
    /// Checks if an object can be placed at the target origin cell.
    /// Returns true if within bounds and all cells are either empty or already owned by the object.
    /// </summary>
    public bool CanPlaceObject(GridObject obj, Vector2Int origin)
    {
        if (obj == null) return false;
        if (!IsAreaInsideGrid(origin, obj.Size)) return false;

        for (int x = 0; x < obj.Size.x; x++)
        {
            for (int y = 0; y < obj.Size.y; y++)
            {
                Vector2Int cell = origin + new Vector2Int(x, y);
                if (_occupiedCells.TryGetValue(cell, out GridObject occupant))
                {
                    if (occupant != null && occupant != obj)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Attempts to place the object at the target origin cell.
    /// If successful, updates occupancy and moves object.
    /// </summary>
    public bool TryPlaceObject(GridObject obj, Vector2Int origin, bool animate = true)
    {
        if (!CanPlaceObject(obj, origin))
        {
            return false;
        }

        // Remove previous occupancy of this object
        RemoveObjectFromOccupancy(obj);

        // Register new occupied cells
        for (int x = 0; x < obj.Size.x; x++)
        {
            for (int y = 0; y < obj.Size.y; y++)
            {
                Vector2Int cell = origin + new Vector2Int(x, y);
                _occupiedCells[cell] = obj;
            }
        }

        obj.SetGridPosition(origin);
        Vector3 targetWorldPos = CellToWorldPosition(origin, obj.Size);
        obj.SnapToWorldPosition(targetWorldPos, animate);

        OnObjectPlaced?.Invoke(obj, origin);
        return true;
    }

    /// <summary>
    /// Removes an object completely from the grid.
    /// </summary>
    public void RemoveObject(GridObject obj)
    {
        if (obj == null) return;
        RemoveObjectFromOccupancy(obj);
        OnObjectRemoved?.Invoke(obj);
    }

    private void RemoveObjectFromOccupancy(GridObject obj)
    {
        if (obj == null) return;

        List<Vector2Int> cellsToRemove = new List<Vector2Int>();
        foreach (var pair in _occupiedCells)
        {
            if (pair.Value == obj)
            {
                cellsToRemove.Add(pair.Key);
            }
        }

        foreach (Vector2Int cell in cellsToRemove)
        {
            _occupiedCells.Remove(cell);
        }
    }

    /// <summary>
    /// Gets the GridObject at the specified cell, or null if cell is empty.
    /// </summary>
    public GridObject GetObjectAt(Vector2Int cell)
    {
        _occupiedCells.TryGetValue(cell, out GridObject occupant);
        return occupant;
    }

    #endregion

    #region Gizmos

    private void OnDrawGizmos()
    {
        if (!_drawGizmos) return;

        EnsureGridReference();
        Vector2 cellSize = GetCellSize();

        if (_boundaryMode != BoundaryMode.Unbounded)
        {
            // Draw grid bounds
            Gizmos.color = _gridBorderColor;
            Vector3 min = CellToWorldPosition(_gridOrigin, Vector2Int.zero);
            Vector3 max = CellToWorldPosition(_gridOrigin + _gridSize, Vector2Int.zero);
            Vector3 center = (min + max) * 0.5f;
            Vector3 size = new Vector3(_gridSize.x * cellSize.x, _gridSize.y * cellSize.y, 0.05f);
            Gizmos.DrawWireCube(center, size);

            // Draw cells
            for (int x = 0; x < _gridSize.x; x++)
            {
                for (int y = 0; y < _gridSize.y; y++)
                {
                    Vector2Int cell = _gridOrigin + new Vector2Int(x, y);
                    Vector3 cellCenter = CellToWorldPosition(cell, Vector2Int.one);

                    bool isOccupied = Application.isPlaying && _occupiedCells.ContainsKey(cell) && _occupiedCells[cell] != null;
                    Gizmos.color = isOccupied ? _occupiedCellColor : _emptyCellColor;
                    Gizmos.DrawWireCube(cellCenter, new Vector3(cellSize.x * 0.95f, cellSize.y * 0.95f, 0.01f));
                }
            }
        }
    }

    #endregion
}
