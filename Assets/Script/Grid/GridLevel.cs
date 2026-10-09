using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Root component for a Level Prefab.
/// Holds references to the level's floor tilemap and registers all level objects on GridManager.
/// </summary>
public class GridLevel : MonoBehaviour
{
    [SerializeField] private Tilemap _floorTilemap;

    public Tilemap FloorTilemap => _floorTilemap;

    private void Start()
    {
        InitializeLevel();
    }

    public void InitializeLevel()
    {
        GridManager gm = GridManager.Instance;
        gm.InitializeFromFloorTilemap(_floorTilemap);

        GridObject[] objects = GetComponentsInChildren<GridObject>();
        for (int i = 0; i < objects.Length; i++)
        {
            Vector2Int cell = gm.WorldToCellPosition(objects[i].transform.position, objects[i]);
            gm.TryPlaceObject(objects[i], cell, animate: false);
        }

        FindFirstObjectByType<GridSandConnector>().EvaluateAllGridConnections();
    }
}
