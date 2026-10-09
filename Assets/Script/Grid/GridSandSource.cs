using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Source component for sand containers on the grid.
/// Can occupy multiple cells and contain multiple stacked sand layers.
/// Inherits from SandPool to run compute shader simulation and visual sand rendering on a child Quad.
/// Creates a fake visual stream line to connected receivers and flows sand out of the connected cells.
/// </summary>
public class GridSandSource : SandPool
{
    [Serializable]
    public class SandLayer
    {
        public SandColor color;
        public int amount = 100;
    }

    [Header("Sand Layers")]
    [SerializeField] private List<SandLayer> _layers = new List<SandLayer>();
    [SerializeField] private bool _topToBottom = false;
    [SerializeField] private bool _simulatePhysics = false;

    [Header("Transfer Settings")]
    [SerializeField] private float _transferRate = 10f;
    [Range(2, 16)] [SerializeField] private int _drainRadius = 4;
    [Range(1, 4)] [SerializeField] private int _simStepsPerTick = 2;

    [Header("Stream Visual")]
    [SerializeField] private float _streamWidth = 0.18f;

    private float _elapsed = 0f;
    private GridObject _gridObject;
    private int _kAddSand = -1;

    private readonly Dictionary<Vector2Int, GridSandReceiver> _cellToReceiver = new Dictionary<Vector2Int, GridSandReceiver>();
    private readonly Dictionary<GridSandReceiver, Vector2Int> _receiverToCell = new Dictionary<GridSandReceiver, Vector2Int>();
    private readonly Dictionary<Vector2Int, GameObject> _cellToLine = new Dictionary<Vector2Int, GameObject>();
    private readonly Dictionary<Vector2Int, Material> _cellToLineMat = new Dictionary<Vector2Int, Material>();

    public event Action<Vector2Int, GridSandReceiver> OnCellConnected;
    public event Action<Vector2Int, GridSandReceiver> OnCellDisconnected;
    public event Action<GridSandReceiver> OnConnected;
    public event Action OnDisconnected;

    public GridObject GridObject => _gridObject;
    public IReadOnlyList<SandLayer> Layers => _layers;
    public bool HasActiveConnections => _cellToReceiver.Count > 0;
    public int ActiveConnectionCount => _cellToReceiver.Count;
    public IReadOnlyDictionary<Vector2Int, GridSandReceiver> ActiveConnections => _cellToReceiver;
    public bool IsConnected => HasActiveConnections;

    public SandLayer CurrentLayer => _layers.Count > 0 ? _layers[0] : null;
    public SandColor CurrentColor => CurrentLayer?.color;
    public bool HasContent => _layers.Count > 0 && CurrentLayer.amount > 0;
    public float TransferRate => _transferRate;
    public int TotalAmount
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _layers.Count; i++) total += _layers[i].amount;
            return total;
        }
    }

    private void Awake()
    {
        _gridObject = GetComponent<GridObject>();
    }

    protected override void Start()
    {
        EnsureReferences();
        base.Start();
    }

    protected virtual void Update()
    {
        if (current == null) return;

        if (HasActiveConnections)
        {
            foreach (var pair in _cellToReceiver)
            {
                Vector2Int cell = pair.Key;
                GridSandReceiver receiver = pair.Value;
                if (receiver == null || !receiver.IsConnected) continue;

                GetHoleCoordinates(cell, receiver, out int hx, out int hy);
                DrainHole(hx, hy, _drainRadius);
            }

            for (int s = 0; s < _simStepsPerTick; s++)
            {
                Step();
            }

            RenderTextureUpdate();
        }
        else if (_simulatePhysics)
        {
            for (int i = 0; i < stepsPerFrame; i++)
            {
                Step();
            }
            RenderTextureUpdate();
        }
    }

    private void LateUpdate()
    {
        if (_cellToLine.Count == 0) return;

        GridManager gm = GridManager.Instance;
        Color activeCol = CurrentColor != null ? CurrentColor.Get(0) : Color.white;

        foreach (var pair in _cellToLine)
        {
            Vector2Int sourceCell = pair.Key;
            GameObject line = pair.Value;
            if (line == null) continue;

            if (!_cellToReceiver.TryGetValue(sourceCell, out GridSandReceiver receiver) || receiver == null)
                continue;

            Vector3 from = gm.CellToWorldPosition(sourceCell);
            Vector3 to = gm.CellToWorldPosition(receiver.GridObject.GridPosition);
            PositionStreamLine(line, from, to);

            if (_cellToLineMat.TryGetValue(sourceCell, out Material mat) && mat != null)
            {
                mat.color = activeCol;
            }
        }
    }

    private void DrainHole(int x, int y, int radius)
    {
        if (_kAddSand < 0) _kAddSand = shader.FindKernel("AddSand");

        shader.SetInt("BrushX", x);
        shader.SetInt("BrushY", y);
        shader.SetInt("BrushRadius", radius);
        shader.SetInt("BrushType", 0);
        shader.SetInt("BrushTypeDel", -1);
        shader.SetBuffer(_kAddSand, "Current", current);
        shader.Dispatch(_kAddSand, gx, gy, 1);
    }

    private void GetHoleCoordinates(Vector2Int sourceCell, GridSandReceiver receiver, out int holeX, out int holeY)
    {
        float tx = 0.5f;
        if (_gridObject != null)
        {
            tx = (sourceCell.x - _gridObject.GridPosition.x + 0.5f) / Mathf.Max(1, _gridObject.Size.x);
        }
        holeX = Mathf.Clamp(Mathf.RoundToInt(tx * width), 0, width - 1);
        holeY = 0;

        Vector2Int diff = receiver.GridObject.GridPosition - sourceCell;
        if (diff.y > 0)
        {
            holeY = height - 1;
        }
        else if (diff.y < 0)
        {
            holeY = 0;
        }
        else if (diff.x < 0)
        {
            holeX = 0;
            float ty = _gridObject != null ? (sourceCell.y - _gridObject.GridPosition.y + 0.5f) / Mathf.Max(1, _gridObject.Size.y) : 0.5f;
            holeY = Mathf.Clamp(Mathf.RoundToInt(ty * height), 0, height - 1);
        }
        else if (diff.x > 0)
        {
            holeX = width - 1;
            float ty = _gridObject != null ? (sourceCell.y - _gridObject.GridPosition.y + 0.5f) / Mathf.Max(1, _gridObject.Size.y) : 0.5f;
            holeY = Mathf.Clamp(Mathf.RoundToInt(ty * height), 0, height - 1);
        }
    }

    private void EnsureReferences()
    {
        if (target == null)
        {
            target = GetComponentInChildren<MeshRenderer>();
        }

        if (target != null && (target.sharedMaterial == null || target.sharedMaterial.shader.name == "Universal Render Pipeline/Lit"))
        {
            Shader unlit = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit")
                        ?? Shader.Find("Sprites/Default")
                        ?? Shader.Find("Universal Render Pipeline/Unlit");
            if (unlit != null)
            {
                target.material = new Material(unlit);
            }
        }

#if UNITY_EDITOR
        if (shader == null)
        {
            shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Sand.compute");
        }
        if (palette == null)
        {
            palette = UnityEditor.AssetDatabase.LoadAssetAtPath<SandPalette>("Assets/Data/palette.asset");
        }
#endif
    }

#if UNITY_EDITOR
    private void Reset()
    {
        EnsureReferences();
    }

    private void OnValidate()
    {
        EnsureReferences();
    }
#endif

    protected override void OnPoolInitialized()
    {
        _kAddSand = shader.FindKernel("AddSand");
        InitializeFullSand();
    }

    [ContextMenu("Rebuild Sand")]
    public void InitializeFullSand()
    {
        if (current == null) return;

        int totalPixels = width * height;
        uint[] data = new uint[totalPixels];

        if (_layers != null && _layers.Count > 0)
        {
            int totalConfiguredAmount = TotalAmount;
            if (totalConfiguredAmount <= 0)
            {
                totalConfiguredAmount = _layers.Count * 100;
            }

            int currentRow = _topToBottom ? height - 1 : 0;
            int currentCol = 0;

            for (int i = 0; i < _layers.Count; i++)
            {
                SandLayer layer = _layers[i];
                if (layer == null) continue;

                int count;
                if (i == _layers.Count - 1)
                {
                    count = _topToBottom
                        ? (currentRow + 1) * width - currentCol
                        : (height - currentRow) * width - currentCol;
                }
                else
                {
                    float ratio = (float)layer.amount / totalConfiguredAmount;
                    count = Mathf.RoundToInt(ratio * totalPixels);
                }

                int resolvedType = GetResolvedSandType(layer);

                for (int p = 0; p < count; p++)
                {
                    if (_topToBottom && currentRow < 0) break;
                    if (!_topToBottom && currentRow >= height) break;

                    uint shade = (uint)UnityEngine.Random.Range(0, SandPalette.Shades);
                    uint pixelValue = (uint)resolvedType | (shade << 4);

                    data[currentRow * width + currentCol] = pixelValue;

                    currentCol++;
                    if (currentCol >= width)
                    {
                        currentCol = 0;
                        if (_topToBottom) currentRow--;
                        else currentRow++;
                    }
                }
            }
        }

        current.SetData(data);
        next.SetData(data);
        RenderTextureUpdate();
    }

    private int GetResolvedSandType(SandLayer layer)
    {
        if (layer.color != null && palette != null && palette.types != null)
        {
            int idx = Array.IndexOf(palette.types, layer.color);
            if (idx >= 0) return idx + 1;

            Color targetCol = layer.color.Get(0);
            for (int i = 0; i < palette.TypeCount; i++)
            {
                if (palette.types[i] != null && palette.types[i].Get(0) == targetCol)
                    return i + 1;
            }
        }
        return 1;
    }

    public bool IsCellConnected(Vector2Int sourceCell)
    {
        return _cellToReceiver.ContainsKey(sourceCell);
    }

    public bool IsConnectedTo(GridSandReceiver receiver)
    {
        return _receiverToCell.ContainsKey(receiver);
    }

    public bool CanConnect(GridSandReceiver receiver)
    {
        if (receiver.IsConnected || receiver.IsFull) return false;
        if (!HasContent) return false;
        if (receiver.TargetColor != CurrentColor) return false;

        return true;
    }

    public void Connect(Vector2Int sourceCell, GridSandReceiver receiver)
    {
        if (_cellToReceiver.ContainsKey(sourceCell)) return;
        if (_receiverToCell.ContainsKey(receiver)) return;

        _cellToReceiver[sourceCell] = receiver;
        _receiverToCell[receiver] = sourceCell;

        CreateFakeLine(sourceCell, receiver);

        OnCellConnected?.Invoke(sourceCell, receiver);
        OnConnected?.Invoke(receiver);
    }

    public void Disconnect(GridSandReceiver receiver)
    {
        if (!_receiverToCell.TryGetValue(receiver, out Vector2Int sourceCell)) return;

        _cellToReceiver.Remove(sourceCell);
        _receiverToCell.Remove(receiver);

        DestroyFakeLine(sourceCell);

        OnCellDisconnected?.Invoke(sourceCell, receiver);
        if (_cellToReceiver.Count == 0)
        {
            OnDisconnected?.Invoke();
        }
    }

    public void DisconnectCell(Vector2Int sourceCell)
    {
        if (!_cellToReceiver.TryGetValue(sourceCell, out GridSandReceiver receiver)) return;
        Disconnect(receiver);
    }

    public void DisconnectAll()
    {
        foreach (var go in _cellToLine.Values)
        {
            if (go != null) Destroy(go);
        }
        _cellToLine.Clear();
        _cellToLineMat.Clear();

        if (_cellToReceiver.Count == 0) return;

        var receivers = new List<GridSandReceiver>(_receiverToCell.Keys);
        for (int i = 0; i < receivers.Count; i++)
        {
            Disconnect(receivers[i]);
            receivers[i].Disconnect();
        }
    }

    private void CreateFakeLine(Vector2Int sourceCell, GridSandReceiver receiver)
    {
        GridManager gm = GridManager.Instance;
        Vector3 from = gm.CellToWorldPosition(sourceCell);
        Vector3 to = gm.CellToWorldPosition(receiver.GridObject.GridPosition);

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = $"FakeLine_{sourceCell}";
        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        go.transform.SetParent(transform, true);

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        Shader s = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Universal Render Pipeline/Unlit");

        Material mat = new Material(s);
        Color colVal = CurrentColor != null ? CurrentColor.Get(0) : Color.yellow;
        mat.color = colVal;
        mr.material = mat;

        if (target != null)
        {
            mr.sortingLayerID = target.sortingLayerID;
            mr.sortingOrder = target.sortingOrder + 1;
        }

        PositionStreamLine(go, from, to);
        _cellToLine[sourceCell] = go;
        _cellToLineMat[sourceCell] = mat;
    }

    private void PositionStreamLine(GameObject go, Vector3 from, Vector3 to)
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

    private void DestroyFakeLine(Vector2Int sourceCell)
    {
        if (_cellToLine.TryGetValue(sourceCell, out GameObject line))
        {
            if (line != null) Destroy(line);
            _cellToLine.Remove(sourceCell);
            _cellToLineMat.Remove(sourceCell);
        }
    }

    public void InitializeLayers(IEnumerable<SandLayer> layers)
    {
        _layers.Clear();
        foreach (var l in layers)
        {
            _layers.Add(new SandLayer { color = l.color, amount = l.amount });
        }

        if (Ready)
        {
            InitializeFullSand();
        }
    }

    public void ConsumeCurrentLayer(int amount)
    {
        if (_layers.Count == 0) return;

        CurrentLayer.amount -= amount;
        if (CurrentLayer.amount <= 0)
        {
            _layers.RemoveAt(0);

            if (_layers.Count == 0)
            {
                ClearAll();
            }

            var receivers = new List<GridSandReceiver>(_receiverToCell.Keys);
            for (int i = 0; i < receivers.Count; i++)
            {
                GridSandReceiver r = receivers[i];
                if (_layers.Count == 0 || r.TargetColor != CurrentColor)
                {
                    Disconnect(r);
                    r.Disconnect();
                }
            }
        }
    }

    public int Tick(float deltaTime)
    {
        if (!HasContent || !HasActiveConnections) return 0;

        _elapsed += deltaTime;
        float interval = _transferRate > 0f ? 1f / _transferRate : float.MaxValue;

        if (_elapsed < interval) return 0;

        int ticks = Mathf.FloorToInt(_elapsed / interval);
        _elapsed -= ticks * interval;

        int unitsPerTick = Mathf.Max(1, Mathf.RoundToInt(_transferRate));
        int amount = ticks * unitsPerTick;
        amount = Mathf.Min(amount, CurrentLayer.amount);
        return amount;
    }

    protected override void OnDestroy()
    {
        DisconnectAll();
        base.OnDestroy();
        if (shader != null) Destroy(shader);
    }
}
