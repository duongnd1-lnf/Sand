using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public class GridManager : MonoBehaviour
{
    public enum BoundaryMode
    {
        ManualBounds,
        TilemapBounds,
        Unbounded
    }

    [Header("Grid References")]
    [SerializeField] private Grid _grid;
    [SerializeField] private Tilemap _tilemap;

    [Header("Boundaries")]
    [SerializeField] private BoundaryMode _boundaryMode = BoundaryMode.ManualBounds;
    [SerializeField] private Vector2Int _gridOrigin = Vector2Int.zero;
    [SerializeField] private Vector2Int _gridSize = new Vector2Int(10, 10);

    [Header("Debug")]
    [SerializeField] private bool _drawGizmos = true;

    private readonly Dictionary<Vector2Int, GridObject> _occupiedCells = new Dictionary<Vector2Int, GridObject>();

    public event Action<GridObject, Vector2Int> OnObjectPlaced;
    public event Action<GridObject> OnObjectRemoved;

    public Grid TargetGrid => _grid;
    public Tilemap BackgroundTilemap => _tilemap;
    public Vector2Int GridOrigin => _gridOrigin;
    public Vector2Int GridSize => _gridSize;

    private void Awake()
    {
        RefreshBoundaries();
    }

    public void RefreshBoundaries()
    {
        if (_boundaryMode == BoundaryMode.TilemapBounds && _tilemap != null)
        {
            _tilemap.CompressBounds();
            BoundsInt b = _tilemap.cellBounds;
            _gridOrigin = new Vector2Int(b.xMin, b.yMin);
            _gridSize = new Vector2Int(b.size.x, b.size.y);
        }
    }

    public Vector2 GetCellSize()
    {
        return new Vector2(_grid.cellSize.x + _grid.cellGap.x, _grid.cellSize.y + _grid.cellGap.y);
    }

    public Vector3 CellToWorldPosition(Vector2Int originCell, Vector2Int size)
    {
        Vector3 minCorner = _grid.CellToWorld(new Vector3Int(originCell.x, originCell.y, 0));
        Vector3 maxCorner = _grid.CellToWorld(new Vector3Int(originCell.x + size.x, originCell.y + size.y, 0));
        Vector3 center = (minCorner + maxCorner) * 0.5f;
        center.z = transform.position.z;
        return center;
    }

    public Vector2Int WorldToCellPosition(Vector3 worldPos, Vector2Int size)
    {
        Vector3 localPos = _grid.transform.InverseTransformPoint(worldPos);
        Vector3 cellSize = _grid.cellSize + _grid.cellGap;
        Vector3 localBL = localPos - new Vector3(size.x * cellSize.x * 0.5f, size.y * cellSize.y * 0.5f, 0f);
        Vector3 samplePoint = localBL + new Vector3(cellSize.x * 0.5f, cellSize.y * 0.5f, 0f);
        Vector3 worldSample = _grid.transform.TransformPoint(samplePoint);

        Vector3Int cell = _grid.WorldToCell(worldSample);
        return new Vector2Int(cell.x, cell.y);
    }

    public bool IsAreaInsideGrid(Vector2Int origin, Vector2Int size)
    {
        if (_boundaryMode == BoundaryMode.Unbounded) return true;

        return origin.x >= _gridOrigin.x &&
               origin.x + size.x <= _gridOrigin.x + _gridSize.x &&
               origin.y >= _gridOrigin.y &&
               origin.y + size.y <= _gridOrigin.y + _gridSize.y;
    }

    public bool CanPlaceObject(GridObject obj, Vector2Int origin)
    {
        if (!IsAreaInsideGrid(origin, obj.Size)) return false;

        for (int x = 0; x < obj.Size.x; x++)
        {
            for (int y = 0; y < obj.Size.y; y++)
            {
                Vector2Int cell = origin + new Vector2Int(x, y);
                if (_occupiedCells.TryGetValue(cell, out GridObject occupant))
                {
                    if (occupant != null && occupant != obj)
                        return false;
                }
            }
        }

        return true;
    }

    public bool TryPlaceObject(GridObject obj, Vector2Int origin, bool animate = true)
    {
        if (!CanPlaceObject(obj, origin)) return false;

        RemoveObjectFromOccupancy(obj);

        for (int x = 0; x < obj.Size.x; x++)
        {
            for (int y = 0; y < obj.Size.y; y++)
            {
                Vector2Int cell = origin + new Vector2Int(x, y);
                _occupiedCells[cell] = obj;
            }
        }

        obj.SetGridPosition(origin);
        Vector3 targetPos = CellToWorldPosition(origin, obj.Size);
        obj.SnapToWorldPosition(targetPos, animate);

        OnObjectPlaced?.Invoke(obj, origin);
        return true;
    }

    public void RemoveObject(GridObject obj)
    {
        RemoveObjectFromOccupancy(obj);
        OnObjectRemoved?.Invoke(obj);
    }

    private void RemoveObjectFromOccupancy(GridObject obj)
    {
        List<Vector2Int> keys = new List<Vector2Int>();
        foreach (var kvp in _occupiedCells)
        {
            if (kvp.Value == obj) keys.Add(kvp.Key);
        }
        foreach (var k in keys) _occupiedCells.Remove(k);
    }

    public GridObject GetObjectAt(Vector2Int cell)
    {
        _occupiedCells.TryGetValue(cell, out GridObject obj);
        return obj;
    }

    public bool IsCellOccupied(Vector2Int cell)
    {
        return _occupiedCells.ContainsKey(cell) && _occupiedCells[cell] != null;
    }

    private void OnDrawGizmos()
    {
        if (!_drawGizmos || _grid == null) return;
        if (_boundaryMode == BoundaryMode.Unbounded) return;

        Vector2 cs = GetCellSize();
        Gizmos.color = Color.cyan;
        Vector3 min = CellToWorldPosition(_gridOrigin, Vector2Int.zero);
        Vector3 max = CellToWorldPosition(_gridOrigin + _gridSize, Vector2Int.zero);
        Gizmos.DrawWireCube((min + max) * 0.5f, new Vector3(_gridSize.x * cs.x, _gridSize.y * cs.y, 0.1f));
    }
}
