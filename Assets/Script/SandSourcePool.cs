using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Source sand pool: Initialized full of sand with stacked color layers.
/// Multiple output directions can be selected (allowedFromSides).
/// When connected to a receiver, it creates a SandSimulator quad that visually
/// streams sand from the source into the nearest connected cell of the receiver.
/// </summary>
public class SandSourcePool : SandPool
{
    [Serializable]
    public class SandLayer
    {
        public SandColor colorAsset;

        [Range(1, 15)] public int sandType = 1;

        [Min(1)] public int pixelCount = 1000;
    }

    [Header("Source Layers (Top to Bottom)")]
    public List<SandLayer> layers = new List<SandLayer>();

    public bool autoFillFullPool = true;

    public bool simulatePhysics = false;

    [Header("Allowed Output Directions")]
    public List<Side> allowedFromSides = new List<Side> { Side.Bottom };

    [Header("Active Transfer (Controlled Externally)")]
    [SerializeField] private SandReceiverPool _activeReceiver;
    [SerializeField] private Side _activeFromSide = Side.Bottom;
    [SerializeField] private Side _activeToSide = Side.Top;
    [SerializeField] private bool _isTransferring = false;

    [Header("Transfer Parameters")]
    [Range(1, 64)] public int transferLanes = 16;
    [Range(1, 10)] public int transferSpeed = 2;

    [Header("Sand Simulator (Flow Stream)")]
    public ComputeShader pipeShader;
    public Material pipeMaterial;
    public Color streamEmptyColor = new Color(0f, 0f, 0f, 0f);
    public float sourceEmbed = 0.08f;

    private SandSimulator _activeSimulator;

    public SandReceiverPool ActiveReceiver => _activeReceiver;
    public Side ActiveFromSide => _activeFromSide;
    public Side ActiveToSide => _activeToSide;
    public bool IsTransferring { get => _isTransferring; set => _isTransferring = value; }
    public SandSimulator ActiveSimulator => _activeSimulator;

    protected override void OnPoolInitialized()
    {
        EnsureShaderReference();
        InitializeFullSand();
    }

    private void EnsureShaderReference()
    {
        if (pipeShader == null)
        {
#if UNITY_EDITOR
            pipeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Pipe.compute");
#endif
        }
    }

    /// <summary>
    /// Checks if a direction is included in the allowed output sides list.
    /// </summary>
    public bool IsDirectionAllowed(Side side)
    {
        return allowedFromSides != null && allowedFromSides.Contains(side);
    }

    /// <summary>
    /// External API to connect this pool to a receiver on specified sides.
    /// </summary>
    public bool Connect(SandReceiverPool receiver, Side from, Side to)
    {
        GridObject sourceObj = GetComponent<GridObject>();
        GridObject receiverObj = receiver != null ? receiver.GetComponent<GridObject>() : null;
        Vector2Int sPos = sourceObj != null ? sourceObj.GridPosition : Vector2Int.zero;
        Vector2Int rPos = receiverObj != null ? receiverObj.GridPosition : Vector2Int.zero;
        return Connect(receiver, from, to, sPos, rPos);
    }

    /// <summary>
    /// Connects to a receiver and creates a SandSimulator flowing into nearestReceiverCell.
    /// </summary>
    public bool Connect(SandReceiverPool receiver, Side from, Side to, Vector2Int sourceCell, Vector2Int receiverCell)
    {
        if (receiver == null)
        {
            Disconnect();
            return false;
        }

        if (!IsDirectionAllowed(from))
        {
            Debug.LogWarning($"[SandSourcePool] Direction '{from}' is not in allowedFromSides on {name}.", this);
            return false;
        }

        Disconnect();

        _activeReceiver = receiver;
        _activeFromSide = from;
        _activeToSide = to;
        _isTransferring = true;

        CreateSimulator(receiver, from, to, sourceCell, receiverCell);
        return true;
    }

    private void CreateSimulator(SandReceiverPool receiver, Side from, Side to, Vector2Int sourceCell, Vector2Int receiverCell)
    {
        EnsureShaderReference();

        if (pipeShader == null)
        {
            Debug.LogError("[SandSourcePool] pipeShader (Pipe.compute) is missing! Cannot create SandSimulator.", this);
            return;
        }

        GridObject sourceObj = GetComponent<GridObject>();
        GridManager gridManager = sourceObj != null ? sourceObj.Manager : null;

        // 1) Compute normalized edge position t
        float t = 0.5f;
        if (sourceObj != null)
        {
            if (from == Side.Bottom || from == Side.Top)
            {
                t = (sourceCell.x - sourceObj.GridPosition.x + 0.5f) / Mathf.Max(1, sourceObj.Size.x);
            }
            else
            {
                t = (sourceCell.y - sourceObj.GridPosition.y + 0.5f) / Mathf.Max(1, sourceObj.Size.y);
            }
            t = Mathf.Clamp01(t);
        }

        int cellsA = EdgeCells(from);
        int n = Mathf.Max(1, Mathf.Min(transferLanes, cellsA));
        int offA = EdgeOffset(from, t, n);

        GetContact(from, t, out Vector3 edgePt, out Vector3 outward, out Vector3 along);
        Vector3 startPt = edgePt - outward * sourceEmbed;

        // 2) Determine destination position inside nearestReceiverCell
        Vector3 receiverCellCenter;
        if (gridManager != null)
        {
            receiverCellCenter = gridManager.CellToWorldPosition(receiverCell, Vector2Int.one);
        }
        else
        {
            receiverCellCenter = receiver.transform.position;
        }

        Vector3 endPt = receiverCellCenter;

        Vector3 fwd = target != null ? target.transform.forward : Vector3.forward;
        Vector3 d = Vector3.ProjectOnPlane(endPt - startPt, fwd);
        float dist = d.magnitude;

        if (dist < 0.01f)
        {
            dist = 0.1f;
            d = outward * dist;
        }

        Vector3 dir = d.normalized;
        Vector3 perp = Vector3.Cross(fwd, dir);
        bool flipA = Vector3.Dot(perp, along) < 0f;

        float cellWorldSize = CellWorldSize(from);
        int streamLength = Mathf.Max(4, Mathf.RoundToInt(dist / Mathf.Max(1e-5f, cellWorldSize)));

        // 3) Create Quad GameObject
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "SandSimulator";
        Collider col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        Vector3 center = startPt + d * 0.5f;
        Quaternion rot = Quaternion.LookRotation(fwd, perp);
        Vector3 scale = new Vector3(dist, n * cellWorldSize, 1f);

        go.transform.SetPositionAndRotation(center, rot);
        go.transform.localScale = scale;
        go.transform.SetParent(transform, true);

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        if (pipeMaterial != null)
        {
            mr.material = new Material(pipeMaterial);
        }
        else
        {
            Shader spriteShader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit") ?? Shader.Find("Sprites/Default");
            if (spriteShader != null)
            {
                mr.material = new Material(spriteShader);
            }
            else if (target != null && target.sharedMaterial != null)
            {
                mr.material = new Material(target.sharedMaterial);
            }
        }

        if (target != null)
        {
            mr.sortingLayerID = target.sortingLayerID;
            mr.sortingOrder = target.sortingOrder + 1;
        }

        _activeSimulator = go.AddComponent<SandSimulator>();
        _activeSimulator.Init(
            source: this,
            receiver: receiver,
            fromSide: from,
            offA: offA,
            flipA: flipA,
            streamLength: streamLength,
            streamLanes: n,
            transferSpeed: transferSpeed,
            computeShader: pipeShader,
            sandPalette: palette,
            meshRenderer: mr,
            backgroundEmptyColor: streamEmptyColor
        );
    }

    /// <summary>
    /// External API to disconnect the active receiver and destroy the simulator.
    /// </summary>
    public void Disconnect()
    {
        if (_activeSimulator != null)
        {
            Destroy(_activeSimulator.gameObject);
            _activeSimulator = null;
        }

        _activeReceiver = null;
        _isTransferring = false;
    }

    /// <summary>
    /// Fills the pool with configured sand layers from top to bottom.
    /// </summary>
    [ContextMenu("Rebuild Sand")]
    public void InitializeFullSand()
    {
        if (current == null) return;

        int totalPixels = width * height;
        uint[] data = new uint[totalPixels];

        if (layers != null && layers.Count > 0)
        {
            int totalConfiguredPixels = 0;
            foreach (var l in layers)
            {
                if (l != null) totalConfiguredPixels += Mathf.Max(0, l.pixelCount);
            }

            int currentRow = height - 1;
            int currentCol = 0;

            for (int i = 0; i < layers.Count; i++)
            {
                SandLayer layer = layers[i];
                if (layer == null) continue;

                int count = layer.pixelCount;
                if (autoFillFullPool && totalConfiguredPixels > 0)
                {
                    count = Mathf.RoundToInt((float)layer.pixelCount / totalConfiguredPixels * totalPixels);
                }

                int resolvedType = GetResolvedSandType(layer);

                for (int p = 0; p < count; p++)
                {
                    if (currentRow < 0) break;

                    uint shade = (uint)UnityEngine.Random.Range(0, SandPalette.Shades);
                    uint pixelValue = (uint)resolvedType | (shade << 4);

                    data[currentRow * width + currentCol] = pixelValue;

                    currentCol++;
                    if (currentCol >= width)
                    {
                        currentCol = 0;
                        currentRow--;
                    }
                }
            }

            // Fill remainder with last layer to ensure 100% full
            if (autoFillFullPool && currentRow >= 0 && layers.Count > 0)
            {
                int lastType = GetResolvedSandType(layers[layers.Count - 1]);
                while (currentRow >= 0)
                {
                    uint shade = (uint)UnityEngine.Random.Range(0, SandPalette.Shades);
                    data[currentRow * width + currentCol] = (uint)lastType | (shade << 4);

                    currentCol++;
                    if (currentCol >= width)
                    {
                        currentCol = 0;
                        currentRow--;
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
        if (layer.colorAsset != null && palette != null && palette.types != null)
        {
            int idx = Array.IndexOf(palette.types, layer.colorAsset);
            if (idx >= 0) return idx + 1;
        }
        return Mathf.Clamp(layer.sandType, 1, 15);
    }

    /// <summary>
    /// Checks whether this source has any layer configured with the specified sand type.
    /// </summary>
    public bool HasSandType(int sandType)
    {
        if (layers == null || layers.Count == 0) return false;
        foreach (var layer in layers)
        {
            if (layer != null && GetResolvedSandType(layer) == sandType)
                return true;
        }
        return false;
    }

    void Update()
    {
        if (current == null) return;

        if (_isTransferring)
        {
            if (_activeReceiver == null || _activeReceiver.IsFull)
            {
                _isTransferring = false;
            }
        }

        if (simulatePhysics && !_isTransferring)
        {
            for (int i = 0; i < stepsPerFrame; i++)
                Step();
        }

        RenderTextureUpdate();
    }

    protected override void OnDestroy()
    {
        Disconnect();
        base.OnDestroy();
    }
}
