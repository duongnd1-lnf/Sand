using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Handles rendering, compute shader sand simulation, and visual stream particles for a GridSandSource.
/// Inherits from SandPool to execute compute shaders (Sand.compute) and render to a RenderTexture Quad.
/// </summary>
public class GridSandSourceVisual : SandPool
{
    private int _kDrain = -1;
    private int _kClearType = -1;
    private int _kProbeSand = -1;
    private ComputeBuffer _drainCounterBuffer;
    private ComputeBuffer _probeCounterBuffer;
    private readonly int[] _drainCounterData = new int[2];
    private readonly int[] _probeCounterData = new int[1];

    private readonly Dictionary<Vector2Int, ParticleSystem> _cellToParticles = new Dictionary<Vector2Int, ParticleSystem>();
    private static Texture2D _circleTex;

    private GridSandSource _source;

    public void BindSource(GridSandSource source)
    {
        _source = source;
    }

    private void Awake()
    {
        if (_source == null)
        {
            _source = GetComponent<GridSandSource>();
        }
    }

    protected override void Start()
    {
        EnsureReferences();
        base.Start();
    }

    public void EnsureReferences()
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
        _kDrain = shader.FindKernel("Drain");
        _kClearType = shader.FindKernel("ClearType");
        _kProbeSand = shader.FindKernel("ProbeSand");
        _drainCounterBuffer?.Release();
        _drainCounterBuffer = new ComputeBuffer(2, sizeof(int));
        _probeCounterBuffer?.Release();
        _probeCounterBuffer = new ComputeBuffer(1, sizeof(int));

        if (_source != null && _source.Layers != null && _source.Layers.Count > 0)
        {
            InitializeSandLayers(_source.Layers, _source.TopToBottom);
        }
    }

    public void InitializeSandLayers(IReadOnlyList<GridSandSource.SandLayer> layers, bool topToBottom)
    {
        if (current == null || layers == null || layers.Count == 0) return;

        int totalPixels = width * height;
        uint[] data = new uint[totalPixels];

        Vector4[] pal = palette != null ? palette.Build() : new Vector4[16 * SandPalette.Shades];
        for (int i = 0; i < layers.Count && i < 15; i++)
        {
            int tid = i + 1;
            layers[i].typeId = tid;
            if (layers[i].color != null)
            {
                for (int s = 0; s < SandPalette.Shades; s++)
                {
                    pal[tid * SandPalette.Shades + s] = layers[i].color.Get(s);
                }
            }
        }
        shader.SetVectorArray("Palette", pal);

        int totalAmount = 0;
        for (int i = 0; i < layers.Count; i++) totalAmount += layers[i].amount;
        if (totalAmount <= 0) totalAmount = layers.Count * 100;

        int currentRow = topToBottom ? height - 1 : 0;
        int currentCol = 0;

        for (int i = 0; i < layers.Count; i++)
        {
            GridSandSource.SandLayer layer = layers[i];
            if (layer == null) continue;

            int count;
            if (i == layers.Count - 1)
            {
                count = topToBottom
                    ? (currentRow + 1) * width - currentCol
                    : (height - currentRow) * width - currentCol;
            }
            else
            {
                float ratio = (float)layer.amount / totalAmount;
                count = Mathf.RoundToInt(ratio * totalPixels);
            }

            layer.initialAmount = Mathf.Max(1, layer.amount);
            layer.initialPixels = count;
            int resolvedType = layer.typeId;

            for (int p = 0; p < count; p++)
            {
                if (topToBottom && currentRow < 0) break;
                if (!topToBottom && currentRow >= height) break;

                uint shade = (uint)UnityEngine.Random.Range(0, SandPalette.Shades);
                uint pixelValue = (uint)resolvedType | (shade << 4);

                data[currentRow * width + currentCol] = pixelValue;

                currentCol++;
                if (currentCol >= width)
                {
                    currentCol = 0;
                    if (topToBottom) currentRow--;
                    else currentRow++;
                }
            }
        }

        current.SetData(data);
        next.SetData(data);
        RenderTextureUpdate();
    }

    public int DrainHole(Vector2Int sourceCell, GridSandReceiver receiver, int maxBudget, int sandType, GridObject gridObj)
    {
        if (maxBudget <= 0 || current == null) return 0;

        GetPortParameters(sourceCell, receiver, gridObj, out Side side, out int offset, out int lanes);

        if (_kDrain < 0) _kDrain = shader.FindKernel("Drain");
        if (_drainCounterBuffer == null) _drainCounterBuffer = new ComputeBuffer(2, sizeof(int));

        _drainCounterData[0] = maxBudget;
        _drainCounterData[1] = 0;
        _drainCounterBuffer.SetData(_drainCounterData);

        shader.SetInt("PortSide", (int)side);
        shader.SetInt("PortOffset", offset);
        shader.SetInt("PortLanes", lanes);
        shader.SetInt("PortFlip", 0);
        shader.SetInt("PortHeight", side == Side.Bottom || side == Side.Top ? height : width);
        shader.SetInt("DrainType", sandType);

        shader.SetBuffer(_kDrain, "Current", current);
        shader.SetBuffer(_kDrain, "DrainCounter", _drainCounterBuffer);

        int groups = Mathf.CeilToInt(lanes / 64f);
        shader.Dispatch(_kDrain, groups, 1, 1);

        _drainCounterBuffer.GetData(_drainCounterData);
        return _drainCounterData[1];
    }

    public void PurgeLayer(int sandType)
    {
        if (current == null) return;
        if (_kClearType < 0) _kClearType = shader.FindKernel("ClearType");

        shader.SetInt("ClearTargetType", sandType);
        shader.SetBuffer(_kClearType, "Current", current);
        shader.SetBuffer(_kClearType, "Next", next);
        shader.Dispatch(_kClearType, gx, gy, 1);

        RenderTextureUpdate();
    }

    public void StepSimulation(int steps)
    {
        if (current == null) return;
        for (int i = 0; i < steps; i++)
        {
            Step();
        }
        RenderTextureUpdate();
    }

    public int GetTypeId(SandColor color)
    {
        if (color == null || palette == null || palette.types == null) return 0;
        for (int i = 0; i < palette.TypeCount; i++)
        {
            if (palette.types[i] == color) return i + 1;
        }
        return 0;
    }

    public int CountSandInCell(Vector2Int sourceCell, int targetTypeId, GridObject gridObj)
    {
        if (current == null || targetTypeId <= 0 || shader == null) return 0;
        if (_kProbeSand < 0) _kProbeSand = shader.FindKernel("ProbeSand");
        if (_probeCounterBuffer == null) _probeCounterBuffer = new ComputeBuffer(1, sizeof(int));

        int objSizeX = gridObj != null ? Mathf.Max(1, gridObj.Size.x) : 1;
        int objSizeY = gridObj != null ? Mathf.Max(1, gridObj.Size.y) : 1;
        Vector2Int localCell = gridObj != null ? sourceCell - gridObj.GridPosition : Vector2Int.zero;

        int xMin = Mathf.Clamp(localCell.x * width / objSizeX, 0, width - 1);
        int xMax = Mathf.Clamp((localCell.x + 1) * width / objSizeX, 0, width);
        int yMin = Mathf.Clamp(localCell.y * height / objSizeY, 0, height - 1);
        int yMax = Mathf.Clamp((localCell.y + 1) * height / objSizeY, 0, height);

        _probeCounterData[0] = 0;
        _probeCounterBuffer.SetData(_probeCounterData);

        shader.SetInt("ProbeXMin", xMin);
        shader.SetInt("ProbeXMax", xMax);
        shader.SetInt("ProbeYMin", yMin);
        shader.SetInt("ProbeYMax", yMax);
        shader.SetInt("ProbeTargetType", targetTypeId);
        shader.SetBuffer(_kProbeSand, "Current", current);
        shader.SetBuffer(_kProbeSand, "ProbeCounter", _probeCounterBuffer);

        int countX = Mathf.CeilToInt((xMax - xMin) / 8f);
        int countY = Mathf.CeilToInt((yMax - yMin) / 8f);
        shader.Dispatch(_kProbeSand, Mathf.Max(1, countX), Mathf.Max(1, countY), 1);

        _probeCounterBuffer.GetData(_probeCounterData);
        return _probeCounterData[0];
    }

    public bool HasSandInCell(Vector2Int sourceCell, int targetTypeId, GridObject gridObj, int minThreshold = 5)
    {
        return CountSandInCell(sourceCell, targetTypeId, gridObj) >= minThreshold;
    }

    private void GetPortParameters(Vector2Int sourceCell, GridSandReceiver receiver, GridObject gridObj, out Side side, out int offset, out int lanes)
    {
        Vector3 sourceWorld = gridObj.GetCellWorldPosition(sourceCell);
        Vector3 receiverWorld = receiver.GridObject.GetCellWorldPosition(receiver.GridObject.GridPosition);
        Vector3 diff = receiverWorld - sourceWorld;

        if (Mathf.Abs(diff.x) > Mathf.Abs(diff.y))
        {
            side = diff.x < 0 ? Side.Left : Side.Right;
        }
        else
        {
            side = diff.y < 0 ? Side.Bottom : Side.Top;
        }

        int objSizeX = Mathf.Max(1, gridObj.Size.x);
        int objSizeY = Mathf.Max(1, gridObj.Size.y);
        Vector2Int localCell = sourceCell - gridObj.GridPosition;

        if (side == Side.Bottom || side == Side.Top)
        {
            float cellWidth = (float)width / objSizeX;
            float centerX = (localCell.x + 0.5f) * cellWidth;
            lanes = Mathf.Clamp(Mathf.RoundToInt(cellWidth * 0.45f), 16, width);
            offset = Mathf.Clamp(Mathf.RoundToInt(centerX - lanes * 0.5f), 0, width - lanes);
        }
        else
        {
            float cellHeight = (float)height / objSizeY;
            float centerY = (localCell.y + 0.5f) * cellHeight;
            lanes = Mathf.Clamp(Mathf.RoundToInt(cellHeight * 0.45f), 16, height);
            offset = Mathf.Clamp(Mathf.RoundToInt(centerY - lanes * 0.5f), 0, height - lanes);
        }
    }

    // ------------------------------------------------------------------
    // Particle Stream Visuals
    // ------------------------------------------------------------------

    private static Texture2D GetCircleTexture()
    {
        if (_circleTex != null) return _circleTex;

        int size = 16;
        _circleTex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        _circleTex.filterMode = FilterMode.Bilinear;
        _circleTex.wrapMode = TextureWrapMode.Clamp;

        float center = (size - 1) * 0.5f;
        float radius = center;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - center;
                float dy = y - center;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                float alpha = Mathf.Clamp01(1f - (dist - (radius - 1.5f)) / 1.5f);
                _circleTex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        _circleTex.Apply();
        return _circleTex;
    }

    public ParticleSystem CreateStream(Vector2Int sourceCell, GridSandReceiver receiver)
    {
        GameObject go = new GameObject($"SandStream_{sourceCell}");
        go.transform.SetParent(transform, true);

        ParticleSystem ps = go.AddComponent<ParticleSystem>();

        var main = ps.main;
        main.loop = false;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 5000;
        main.stopAction = ParticleSystemStopAction.None;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.gravityModifier = 0f;

        var emission = ps.emission;
        emission.enabled = false;

        var shape = ps.shape;
        shape.enabled = false;

        ParticleSystemRenderer psr = go.GetComponent<ParticleSystemRenderer>();
        psr.renderMode = ParticleSystemRenderMode.Billboard;
        psr.sortMode = ParticleSystemSortMode.Distance;

        Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                             ?? Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit")
                             ?? Shader.Find("Sprites/Default");
        Material mat = new Material(particleShader);
        Texture2D circle = GetCircleTexture();
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", circle);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", circle);
        psr.material = mat;

        if (target != null)
        {
            psr.sortingLayerID = target.sortingLayerID;
            psr.sortingOrder = target.sortingOrder + 10;
        }

        _cellToParticles[sourceCell] = ps;
        return ps;
    }

    public void DestroyStream(Vector2Int sourceCell)
    {
        if (_cellToParticles.TryGetValue(sourceCell, out ParticleSystem ps))
        {
            _cellToParticles.Remove(sourceCell);
            if (ps != null)
            {
                Destroy(ps.gameObject, 0.5f);
            }
        }
    }

    public void DestroyAllStreams()
    {
        foreach (var ps in _cellToParticles.Values)
        {
            if (ps != null)
            {
                Destroy(ps.gameObject, 0.5f);
            }
        }
        _cellToParticles.Clear();
    }

    public void EmitStreamParticles(Vector2Int sourceCell, GridSandReceiver receiver, int pixelCount, SandColor color)
    {
        if (pixelCount <= 0) return;
        if (!_cellToParticles.TryGetValue(sourceCell, out ParticleSystem ps) || ps == null) return;

        if (!ps.isPlaying) ps.Play();

        GridManager gm = GridManager.Instance;
        float cellSize = gm != null ? gm.CellSize.x : 1f;

        Vector2Int targetCell = GetNearestReceiverCell(sourceCell, receiver);
        Vector3 targetCenter = receiver.GridObject.GetCellWorldPosition(targetCell);
        Vector3 spawnCenter = GetStreamSpawnPosition(sourceCell, targetCenter, cellSize);

        Vector3 toTarget = targetCenter - spawnCenter;
        bool isVertical = Mathf.Abs(toTarget.y) > Mathf.Abs(toTarget.x);

        int particleCount = Mathf.Clamp(Mathf.RoundToInt(pixelCount * 0.4f), 1, 40);

        ParticleSystem.EmitParams emitParams = new ParticleSystem.EmitParams();

        for (int i = 0; i < particleCount; i++)
        {
            Vector3 spawnOffset;
            if (isVertical)
            {
                float ox = UnityEngine.Random.Range(-cellSize * 0.15f, cellSize * 0.15f);
                float oy = UnityEngine.Random.Range(-0.02f, 0.02f);
                spawnOffset = new Vector3(ox, oy, 0f);
            }
            else
            {
                float ox = UnityEngine.Random.Range(-0.02f, 0.02f);
                float oy = UnityEngine.Random.Range(-cellSize * 0.15f, cellSize * 0.15f);
                spawnOffset = new Vector3(ox, oy, 0f);
            }

            Vector3 spawnPos = spawnCenter + spawnOffset;
            spawnPos.z = transform.position.z - 0.05f;

            Vector3 targetJitter = new Vector3(
                UnityEngine.Random.Range(-cellSize * 0.12f, cellSize * 0.12f),
                UnityEngine.Random.Range(-cellSize * 0.12f, cellSize * 0.12f),
                0f
            );
            Vector3 endPoint = targetCenter + targetJitter;
            endPoint.z = transform.position.z - 0.05f;

            Vector3 displacement = endPoint - spawnPos;
            float distance = displacement.magnitude;
            float travelTime = Mathf.Clamp(distance / 4.0f, 0.2f, 0.5f);
            travelTime += UnityEngine.Random.Range(-0.03f, 0.03f);

            emitParams.position = spawnPos;
            emitParams.velocity = displacement / travelTime;
            emitParams.startLifetime = travelTime;
            emitParams.startSize = UnityEngine.Random.Range(0.04f, 0.065f) * cellSize;

            int shade = UnityEngine.Random.Range(0, SandPalette.Shades);
            emitParams.startColor = color != null ? color.Get(shade) : Color.yellow;

            ps.Emit(emitParams, 1);
        }
    }

    private Vector2Int GetNearestReceiverCell(Vector2Int sourceCell, GridSandReceiver receiver)
    {
        if (receiver.GridObject.OccupiedOffsets == null || receiver.GridObject.OccupiedOffsets.Count <= 1)
        {
            return receiver.GridObject.GridPosition;
        }

        Vector2Int origin = receiver.GridObject.GridPosition;
        Vector2Int nearest = origin;
        float minSqrDist = float.MaxValue;
        Vector3 sourceWorld = _source.GridObject.GetCellWorldPosition(sourceCell);

        foreach (var offset in receiver.GridObject.OccupiedOffsets)
        {
            Vector2Int candidate = origin + offset;
            Vector3 candidateWorld = receiver.GridObject.GetCellWorldPosition(candidate);
            float sqrDist = (candidateWorld - sourceWorld).sqrMagnitude;
            if (sqrDist < minSqrDist)
            {
                minSqrDist = sqrDist;
                nearest = candidate;
            }
        }

        return nearest;
    }

    private Vector3 GetStreamSpawnPosition(Vector2Int sourceCell, Vector3 targetCenter, float cellSize)
    {
        Vector3 cellCenter = _source.GridObject.GetCellWorldPosition(sourceCell);
        Vector3 dir = (targetCenter - cellCenter).normalized;
        return cellCenter + dir * (cellSize * 0.45f);
    }

    protected override void OnDestroy()
    {
        DestroyAllStreams();
        _drainCounterBuffer?.Release();
        _probeCounterBuffer?.Release();
        base.OnDestroy();
        if (shader != null) Destroy(shader);
    }
}
