using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Dedicated renderer for drawing grid tiles (floor, walls, corners) and spawning level objects.
/// Handles Rebuild Grid operations for the visual grid.
/// </summary>
public class GridRenderer : MonoBehaviour
{
    [Header("Tilemaps")]
    [SerializeField] private Tilemap _floorTilemap;
    [SerializeField] private Tilemap _wallTilemap;

    [Header("Floor Tile")]
    [SerializeField] private TileBase _cellTile;

    [Header("Wall Tiles")]
    [SerializeField] private TileBase _wallTopTile;
    [SerializeField] private TileBase _wallBottomTile;
    [SerializeField] private TileBase _wallLeftTile;
    [SerializeField] private TileBase _wallRightTile;

    [Header("Corner Tiles")]
    [SerializeField] private TileBase _cornerTopLeftTile;
    [SerializeField] private TileBase _cornerTopRightTile;
    [SerializeField] private TileBase _cornerBottomLeftTile;
    [SerializeField] private TileBase _cornerBottomRightTile;

    [Serializable]
    public class ObjectPrefabEntry
    {
        public string key;
        public GridObject prefab;
    }

    [Header("Object Prefab Catalog")]
    [SerializeField] private List<ObjectPrefabEntry> _prefabCatalog = new List<ObjectPrefabEntry>();
    [SerializeField] private Transform _objectsContainer;

    private readonly List<GridObject> _spawnedObjects = new List<GridObject>();

    public Tilemap FloorTilemap
    {
        get => _floorTilemap;
        set => _floorTilemap = value;
    }

    public Tilemap WallTilemap
    {
        get => _wallTilemap;
        set => _wallTilemap = value;
    }

    public List<ObjectPrefabEntry> PrefabCatalog => _prefabCatalog;

    /// <summary>
    /// Rebuilds the visual grid by scanning painted floor tiles and generating surrounding walls and corners.
    /// Also synchronizes GridManager floor cells if an instance is present.
    /// </summary>
    [ContextMenu("Rebuild Grid")]
    public void RebuildGrid()
    {
        GenerateWallsAroundFloor();
        if (GridManager.Instance != null)
        {
            GridManager.Instance.InitializeFromFloorTilemap(_floorTilemap);
        }
    }

    /// <summary>
    /// Master render call: Renders tilemaps and spawns initial level objects from LevelData.
    /// </summary>
    public void Render(GridManager gridManager)
    {
        RenderTiles(gridManager.Cells);
        SpawnLevelObjects(gridManager.LevelData, gridManager);
    }

    /// <summary>
    /// Renders floor cells and surrounds perimeter with wall and corner tiles.
    /// </summary>
    public void RenderTiles(IReadOnlyDictionary<Vector2Int, GridCellData> cells)
    {
        ClearTilemaps();

        HashSet<Vector2Int> floorPositions = new HashSet<Vector2Int>();
        foreach (var kvp in cells)
        {
            if (kvp.Value.type == CellType.Floor)
            {
                floorPositions.Add(kvp.Key);
            }
        }

        foreach (var pos in floorPositions)
        {
            _floorTilemap.SetTile(new Vector3Int(pos.x, pos.y, 0), _cellTile);
        }

        HashSet<Vector2Int> wallPositions = new HashSet<Vector2Int>();
        Vector2Int[] neighborOffsets = new Vector2Int[]
        {
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0),
            new Vector2Int(1, 0),
            new Vector2Int(-1, 1),
            new Vector2Int(1, 1),
            new Vector2Int(-1, -1),
            new Vector2Int(1, -1)
        };

        foreach (var pos in floorPositions)
        {
            foreach (var offset in neighborOffsets)
            {
                Vector2Int neighbor = pos + offset;
                if (!floorPositions.Contains(neighbor))
                {
                    wallPositions.Add(neighbor);
                }
            }
        }

        foreach (var wPos in wallPositions)
        {
            TileBase wallTile = GetWallTileForPosition(wPos, floorPositions);
            _wallTilemap.SetTile(new Vector3Int(wPos.x, wPos.y, 0), wallTile);
        }
    }

    /// <summary>
    /// Scans whatever floor tiles are currently painted on _floorTilemap and automatically surrounds them with matching walls and corners on _wallTilemap.
    /// </summary>
    [ContextMenu("Auto-Generate Walls Around Floor")]
    public void GenerateWallsAroundFloor()
    {
        HashSet<Vector2Int> floorPositions = new HashSet<Vector2Int>();
        BoundsInt bounds = _floorTilemap.cellBounds;
        foreach (var pos in bounds.allPositionsWithin)
        {
            if (_floorTilemap.HasTile(pos))
            {
                floorPositions.Add(new Vector2Int(pos.x, pos.y));
            }
        }

        _wallTilemap.ClearAllTiles();

        HashSet<Vector2Int> wallPositions = new HashSet<Vector2Int>();
        Vector2Int[] neighborOffsets = new Vector2Int[]
        {
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0),
            new Vector2Int(1, 0),
            new Vector2Int(-1, 1),
            new Vector2Int(1, 1),
            new Vector2Int(-1, -1),
            new Vector2Int(1, -1)
        };

        foreach (var pos in floorPositions)
        {
            foreach (var offset in neighborOffsets)
            {
                Vector2Int neighbor = pos + offset;
                if (!floorPositions.Contains(neighbor))
                {
                    wallPositions.Add(neighbor);
                }
            }
        }

        foreach (var wPos in wallPositions)
        {
            TileBase wallTile = GetWallTileForPosition(wPos, floorPositions);
            _wallTilemap.SetTile(new Vector3Int(wPos.x, wPos.y, 0), wallTile);
        }
    }

    /// <summary>
    /// Spawns initial objects from LevelData using the Prefab Catalog.
    /// </summary>
    public void SpawnLevelObjects(GridLevelData levelData, GridManager gridManager)
    {
        ClearSpawnedObjects(gridManager);

        Transform parent = _objectsContainer;

        foreach (var config in levelData.objects)
        {
            GridObject prefab = GetPrefabByKey(config.key);
            Vector3 spawnPos = gridManager.CellToWorldPosition(config.originCell);
            GridObject instance = Instantiate(prefab, spawnPos, Quaternion.identity, parent);
            instance.ApplyRotation(config.rotation);

            gridManager.TryPlaceObject(instance, config.originCell, animate: false);
            _spawnedObjects.Add(instance);
        }

        foreach (var sourceConfig in levelData.sources)
        {
            GridObject prefab = GetPrefabByKey(sourceConfig.key);
            Vector2Int originCell = sourceConfig.GetOriginCell();

            Vector3 spawnPos = gridManager.CellToWorldPosition(originCell, prefab.Size);
            GridObject instance = Instantiate(prefab, spawnPos, Quaternion.identity, parent);
            instance.ApplyRotation(sourceConfig.rotation);
            instance.IsDraggable = sourceConfig.isDraggable;

            GridSandSource sourceComp = instance.GetComponent<GridSandSource>();
            sourceComp.InitializeLayers(sourceConfig.layers);

            gridManager.TryPlaceObject(instance, originCell, animate: false);
            _spawnedObjects.Add(instance);
        }
    }

    public GridObject GetPrefabByKey(string key)
    {
        foreach (var entry in _prefabCatalog)
        {
            if (entry.key == key) return entry.prefab;
        }
        Debug.LogError("Not prefab by key: " + key);
        return null;
    }

    public void ClearSpawnedObjects(GridManager gridManager)
    {
        for (int i = 0; i < _spawnedObjects.Count; i++)
        {
            gridManager.RemoveObject(_spawnedObjects[i]);
            if (Application.isPlaying) Destroy(_spawnedObjects[i].gameObject);
            else DestroyImmediate(_spawnedObjects[i].gameObject);
        }
        _spawnedObjects.Clear();
    }

    /// <summary>
    /// Updates or redraws a single floor cell on the tilemap.
    /// </summary>
    public void UpdateCell(Vector2Int pos, GridCellData data)
    {
        _floorTilemap.SetTile(new Vector3Int(pos.x, pos.y, 0), data.type == CellType.Floor ? _cellTile : null);
    }

    private TileBase GetWallTileForPosition(Vector2Int pos, HashSet<Vector2Int> floor)
    {
        bool hasFloorDown = floor.Contains(pos + Vector2Int.down);
        bool hasFloorUp = floor.Contains(pos + Vector2Int.up);
        bool hasFloorLeft = floor.Contains(pos + Vector2Int.left);
        bool hasFloorRight = floor.Contains(pos + Vector2Int.right);

        // Cardinal wall edges
        if (hasFloorDown && !hasFloorLeft && !hasFloorRight && !hasFloorUp) return _wallTopTile;
        if (hasFloorUp && !hasFloorLeft && !hasFloorRight && !hasFloorDown) return _wallBottomTile;
        if (hasFloorLeft && !hasFloorUp && !hasFloorDown && !hasFloorRight) return _wallRightTile;
        if (hasFloorRight && !hasFloorUp && !hasFloorDown && !hasFloorLeft) return _wallLeftTile;

        // Diagonal outer corners
        bool hasFloorDownLeft = floor.Contains(pos + new Vector2Int(-1, -1));
        bool hasFloorDownRight = floor.Contains(pos + new Vector2Int(1, -1));
        bool hasFloorUpLeft = floor.Contains(pos + new Vector2Int(-1, 1));
        bool hasFloorUpRight = floor.Contains(pos + new Vector2Int(1, 1));

        if (!hasFloorDown && !hasFloorUp && !hasFloorLeft && !hasFloorRight)
        {
            if (hasFloorDownLeft) return _cornerTopRightTile;
            if (hasFloorDownRight) return _cornerTopLeftTile;
            if (hasFloorUpLeft) return _cornerBottomRightTile;
            if (hasFloorUpRight) return _cornerBottomLeftTile;
        }

        // Inner corners / junction intersections
        if (hasFloorDown && hasFloorLeft) return _cornerTopRightTile;
        if (hasFloorDown && hasFloorRight) return _cornerTopLeftTile;
        if (hasFloorUp && hasFloorLeft) return _cornerBottomRightTile;
        if (hasFloorUp && hasFloorRight) return _cornerBottomLeftTile;

        // Fallbacks
        if (hasFloorDown) return _wallTopTile;
        if (hasFloorUp) return _wallBottomTile;
        if (hasFloorLeft) return _wallRightTile;
        if (hasFloorRight) return _wallLeftTile;

        return null;
    }

    [ContextMenu("Clear Tilemaps")]
    public void ClearTilemaps()
    {
        _floorTilemap.ClearAllTiles();
        _wallTilemap.ClearAllTiles();
    }
}
