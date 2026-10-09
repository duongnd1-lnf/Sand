using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Visual bridge between GridSandSource (data) and a flow effect shown in the scene.
/// Attach to the same GameObject as GridSandSource.
/// For each active cell connection, spawns a Quad "sand stream" GameObject from the
/// source cell world-center to the receiver's pivot cell world-center.
/// The stream is destroyed when the connection is broken.
/// </summary>
public class GridSandSourceVisual : MonoBehaviour
{
    [Header("Stream Visual")]
    [SerializeField] private Material _streamMaterial;
    [SerializeField] private Color _streamColor = new Color(0.85f, 0.65f, 0.2f, 1f);
    [SerializeField] private float _streamWidth = 0.15f;

    private GridSandSource _source;
    // sourceCell -> stream quad GameObject
    private readonly Dictionary<Vector2Int, GameObject> _activeStreams = new Dictionary<Vector2Int, GameObject>();

    private void Awake()
    {
        _source = GetComponent<GridSandSource>();
    }

    private void OnEnable()
    {
        _source.OnCellConnected += HandleCellConnected;
        _source.OnCellDisconnected += HandleCellDisconnected;
        _source.OnDisconnected += HandleAllDisconnected;
    }

    private void OnDisable()
    {
        _source.OnCellConnected -= HandleCellConnected;
        _source.OnCellDisconnected -= HandleCellDisconnected;
        _source.OnDisconnected -= HandleAllDisconnected;
    }

    private void HandleCellConnected(Vector2Int sourceCell, GridSandReceiver receiver)
    {
        if (_activeStreams.ContainsKey(sourceCell)) return;

        Vector3 from = GridManager.Instance.CellToWorldPosition(sourceCell);
        Vector3 to = GridManager.Instance.CellToWorldPosition(receiver.GridObject.GridPosition);

        GameObject stream = CreateStream(from, to);
        _activeStreams[sourceCell] = stream;
    }

    private void HandleCellDisconnected(Vector2Int sourceCell, GridSandReceiver receiver)
    {
        DestroyStream(sourceCell);
    }

    private void HandleAllDisconnected()
    {
        foreach (var key in new List<Vector2Int>(_activeStreams.Keys))
        {
            DestroyStream(key);
        }
    }

    private void DestroyStream(Vector2Int sourceCell)
    {
        if (_activeStreams.TryGetValue(sourceCell, out GameObject go))
        {
            if (go != null) Destroy(go);
            _activeStreams.Remove(sourceCell);
        }
    }

    private void LateUpdate()
    {
        // Keep stream positions updated in case objects move during drag
        foreach (var pair in _activeStreams)
        {
            Vector2Int sourceCell = pair.Key;
            GameObject stream = pair.Value;
            if (stream == null) continue;

            if (!_source.IsCellConnected(sourceCell)) continue;

            if (!_source.ActiveConnections.TryGetValue(sourceCell, out GridSandReceiver receiver)) continue;

            Vector3 from = GridManager.Instance.CellToWorldPosition(sourceCell);
            Vector3 to = GridManager.Instance.CellToWorldPosition(receiver.GridObject.GridPosition);

            PositionStream(stream, from, to);
        }
    }

    private GameObject CreateStream(Vector3 from, Vector3 to)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "SandStream";

        // Remove collider so it doesn't interfere with drag
        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        // Set parent so it follows the scene
        go.transform.SetParent(transform.parent, true);

        // Apply material / color
        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (_streamMaterial != null)
        {
            mr.material = new Material(_streamMaterial);
        }
        else
        {
            Shader s = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default")
                     ?? Shader.Find("Sprites/Default")
                     ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (s != null) mr.material = new Material(s);
        }

        mr.material.color = _streamColor;

        // Sort above grid
        SpriteRenderer reference = GetComponentInChildren<SpriteRenderer>();
        if (reference != null)
        {
            mr.sortingLayerID = reference.sortingLayerID;
            mr.sortingOrder = reference.sortingOrder + 1;
        }

        PositionStream(go, from, to);
        return go;
    }

    private void PositionStream(GameObject go, Vector3 from, Vector3 to)
    {
        from.z = transform.position.z - 0.01f;
        to.z = transform.position.z - 0.01f;

        Vector3 dir = to - from;
        float dist = dir.magnitude;

        if (dist < 0.001f)
        {
            go.SetActive(false);
            return;
        }

        go.SetActive(true);

        Vector3 center = (from + to) * 0.5f;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        go.transform.position = center;
        go.transform.rotation = Quaternion.Euler(0f, 0f, angle);
        go.transform.localScale = new Vector3(dist, _streamWidth, 1f);
    }

    private void OnDestroy()
    {
        foreach (var go in _activeStreams.Values)
        {
            if (go != null) Destroy(go);
        }
        _activeStreams.Clear();
    }
}
