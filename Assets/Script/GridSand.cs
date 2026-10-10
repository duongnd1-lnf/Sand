using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// GPU sand simulation controller.
/// Drives Sand.compute compute shader using GraphicsBuffers and RenderTexture.
/// </summary>
public class GridSand : MonoBehaviour
{
    [SerializeField] private int _width = 128;
    [SerializeField] private int _height = 128;
    [SerializeField] private ComputeShader _shader;
    [SerializeField] private SandPalette _palette;
    [SerializeField] private MeshRenderer _target;
    [SerializeField] private int _connectionEraseDepth = 5;

    private GraphicsBuffer _current;
    private GraphicsBuffer _next;
    private GraphicsBuffer _claims;
    private RenderTexture _texture;

    private int _kClearAll;
    private int _kClearClaims;
    private int _kSimulate;
    private int _kRender;
    private int _kErase;

    private int _gx;
    private int _gy;
    private int _simFrame;
    private readonly int[] _typeCounts = new int[16];

    public int Width => _width;
    public int Height => _height;
    public bool Ready => _current != null && _shader != null;
    public int ConnectionEraseDepth { get => _connectionEraseDepth; set => _connectionEraseDepth = value; }
    public SandPalette Palette { get => _palette; set => _palette = value; }
    public ComputeShader Shader { get => _shader; set => _shader = value; }

    private void Awake()
    {
        EnsureReferences();
    }

    private void Start()
    {
        if (_current == null)
            InitSimulation();
    }

    private void EnsureReferences()
    {
        if (_target == null)
            _target = GetComponentInChildren<MeshRenderer>();

        if (_target != null && (_target.sharedMaterial == null ||
            _target.sharedMaterial.shader.name == "Universal Render Pipeline/Lit"))
        {
            Shader unlit = UnityEngine.Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit")
                        ?? UnityEngine.Shader.Find("Sprites/Default")
                        ?? UnityEngine.Shader.Find("Universal Render Pipeline/Unlit");
            _target.material = new Material(unlit);
        }

#if UNITY_EDITOR
        if (_palette == null)
            _palette = UnityEditor.AssetDatabase.LoadAssetAtPath<SandPalette>("Assets/Data/palette.asset");
        if (_shader == null)
            _shader = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/Sand.compute");
#endif
    }

    public void InitSimulation()
    {
        EnsureReferences();
        if (_shader == null)
        {
            Debug.LogError("[GridSand] Missing Sand.compute ComputeShader reference.", this);
            return;
        }

        _shader = Instantiate(_shader);

        _kClearAll    = _shader.FindKernel("ClearAll");
        _kClearClaims = _shader.FindKernel("ClearClaims");
        _kSimulate    = _shader.FindKernel("Simulate");
        _kRender      = _shader.FindKernel("Render");
        _kErase       = _shader.FindKernel("Erase");

        _gx = Mathf.CeilToInt(_width / 8f);
        _gy = Mathf.CeilToInt(_height / 8f);

        int total = _width * _height;

        _current?.Release();
        _next?.Release();
        _claims?.Release();

        GraphicsBuffer.Target simBufferTarget = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.CopySource | GraphicsBuffer.Target.CopyDestination;
        _current = new GraphicsBuffer(simBufferTarget, total, sizeof(uint));
        _next    = new GraphicsBuffer(simBufferTarget, total, sizeof(uint));
        _claims  = new GraphicsBuffer(GraphicsBuffer.Target.Structured, total, sizeof(uint));

        if (_texture != null) _texture.Release();
        _texture = new RenderTexture(_width, _height, 0, RenderTextureFormat.ARGB32)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            enableRandomWrite = true
        };
        _texture.Create();

        Material mat = _target.material;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", _texture);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", _texture);

        _shader.SetInt("Width", _width);
        _shader.SetInt("Height", _height);

        if (_palette != null) _palette.Apply(_shader);

        ClearAll();
    }

    public void Init(IReadOnlyList<GridSandSource.SandLayer> layers)
    {
        if (layers.Count == 0) return;
        Init(layers[0].color);
    }

    public void Init(SandColor color)
    {
        if (_current == null) InitSimulation();

        if (_palette == null)
            _palette = ScriptableObject.CreateInstance<SandPalette>();
        else
            _palette = Instantiate(_palette);

        _palette.types = new[] { color };
        _palette.Apply(_shader);

        int total = _width * _height;
        uint[] initialData = new uint[total];
        for (int y = 0; y < _height; y++)
        {
            for (int x = 0; x < _width; x++)
            {
                uint shade = (uint)((x * 7 + y * 13) & (SandPalette.Shades - 1));
                initialData[y * _width + x] = 1u | (shade << 4);
            }
        }

        _typeCounts[1] = total;
        _current.SetData(initialData);
        _next.SetData(initialData);

        RenderTextureUpdate();
    }

    public void ClearAll()
    {
        if (_current == null || _shader == null) return;

        _shader.SetBuffer(_kClearAll, "Current", _current);
        _shader.SetBuffer(_kClearAll, "Next",    _next);
        _shader.SetBuffer(_kClearAll, "Claims",  _claims);
        _shader.Dispatch(_kClearAll, _gx, _gy, 1);

        Array.Clear(_typeCounts, 0, _typeCounts.Length);
        _simFrame = 0;
        RenderTextureUpdate();
    }

    public void Step()
    {
        if (_current == null || _next == null || _claims == null || _shader == null) return;

        _simFrame++;
        if (_simFrame >= int.MaxValue - 2) _simFrame = 1;

        // 1. Reset claims on GPU (0 bytes CPU-GPU traffic)
        _shader.SetBuffer(_kClearClaims, "Claims", _claims);
        _shader.Dispatch(_kClearClaims, _gx, _gy, 1);

        // 2. Prefill Next with Current (particles that cannot fall retain position)
        Graphics.CopyBuffer(_current, _next);

        // 3. Dispatch Simulate on GPU
        _shader.SetInt("Frame", _simFrame);
        _shader.SetBuffer(_kSimulate, "Current", _current);
        _shader.SetBuffer(_kSimulate, "Next",    _next);
        _shader.SetBuffer(_kSimulate, "Claims",  _claims);
        _shader.Dispatch(_kSimulate, _gx, _gy, 1);

        // 4. Swap buffers
        (_current, _next) = (_next, _current);
    }

    public void RenderTextureUpdate()
    {
        if (_current == null || _texture == null || _shader == null) return;

        _shader.SetBuffer(_kRender, "Current", _current);
        _shader.SetTexture(_kRender, "Result",  _texture);
        _shader.Dispatch(_kRender, _gx, _gy, 1);
    }

    public void StepSimulation(int steps = 1)
    {
        for (int i = 0; i < steps; i++)
            Step();
        RenderTextureUpdate();
    }

    /// <summary>
    /// Calculates the GPU pixel bounds for a given grid connection point and border depth.
    /// If depth is <= 0, uses the serialized _connectionEraseDepth.
    /// </summary>
    public RectInt GetConnectionPixelBounds(GridConnectionPoint point, GridObject gridObj, int depth = -1)
    {
        int effectiveDepth = depth > 0 ? depth : _connectionEraseDepth;
        Vector2Int origin = gridObj != null ? gridObj.GridPosition : Vector2Int.zero;
        Vector2Int size = gridObj != null ? gridObj.Size : Vector2Int.one;

        int ox = point.cell.x - origin.x;
        int oy = point.cell.y - origin.y;

        int sizeX = Mathf.Max(1, size.x);
        int sizeY = Mathf.Max(1, size.y);

        int cellPixelW = _width / sizeX;
        int cellPixelH = _height / sizeY;

        int cellXMin = ox * cellPixelW;
        int cellXMax = (ox == sizeX - 1) ? _width - 1 : (ox + 1) * cellPixelW - 1;
        int cellYMin = oy * cellPixelH;
        int cellYMax = (oy == sizeY - 1) ? _height - 1 : (oy + 1) * cellPixelH - 1;

        int xMin, xMax, yMin, yMax;

        switch (point.side)
        {
            case Side.Bottom:
                xMin = cellXMin;
                xMax = cellXMax;
                yMin = cellYMin;
                yMax = Mathf.Min(cellYMax, cellYMin + effectiveDepth - 1);
                break;
            case Side.Top:
                xMin = cellXMin;
                xMax = cellXMax;
                yMin = Mathf.Max(cellYMin, cellYMax - effectiveDepth + 1);
                yMax = cellYMax;
                break;
            case Side.Left:
                xMin = cellXMin;
                xMax = Mathf.Min(cellXMax, cellXMin + effectiveDepth - 1);
                yMin = cellYMin;
                yMax = cellYMax;
                break;
            case Side.Right:
            default:
                xMin = Mathf.Max(cellXMin, cellXMax - effectiveDepth + 1);
                xMax = cellXMax;
                yMin = cellYMin;
                yMax = cellYMax;
                break;
        }

        return new RectInt(xMin, yMin, xMax - xMin + 1, yMax - yMin + 1);
    }

    /// <summary>
    /// Erases all sand particles in the given GPU pixel rectangle.
    /// </summary>
    public void EraseRegion(RectInt pixelBounds, int eraseType = 0)
    {
        if (_current == null || _shader == null) return;

        int xMin = pixelBounds.xMin;
        int xMax = pixelBounds.xMax - 1;
        int yMin = pixelBounds.yMin;
        int yMax = pixelBounds.yMax - 1;

        int spanX = xMax - xMin + 1;
        int spanY = yMax - yMin + 1;
        if (spanX <= 0 || spanY <= 0) return;

        int ex = Mathf.CeilToInt(spanX / 8f);
        int ey = Mathf.CeilToInt(spanY / 8f);

        _shader.SetInt("EraseXMin", xMin);
        _shader.SetInt("EraseXMax", xMax);
        _shader.SetInt("EraseYMin", yMin);
        _shader.SetInt("EraseYMax", yMax);
        _shader.SetInt("EraseType", eraseType);
        _shader.SetBuffer(_kErase, "Current", _current);
        _shader.SetBuffer(_kErase, "Next",    _next);
        _shader.Dispatch(_kErase, ex, ey, 1);
    }

    public int EraseNearBorder(int cxMin, int cxMax, int cyMin, int cyMax, Side side, int maxCount, int targetTypeId)
    {
        if (_current == null || _shader == null || maxCount <= 0) return 0;

        int depth = Mathf.Clamp(_connectionEraseDepth, 1, 64);
        int xMin = cxMin, xMax = cxMax;
        int yMin = cyMin, yMax = cyMax;

        switch (side)
        {
            case Side.Bottom: yMin = 0; yMax = Mathf.Min(_height - 1, depth); break;
            case Side.Top:    yMin = Mathf.Max(0, _height - 1 - depth); yMax = _height - 1; break;
            case Side.Left:   xMin = 0; xMax = Mathf.Min(_width - 1, depth); break;
            case Side.Right:
            default:          xMin = Mathf.Max(0, _width - 1 - depth); xMax = _width - 1; break;
        }

        int spanX = xMax - xMin + 1;
        int spanY = yMax - yMin + 1;
        int ex = Mathf.CeilToInt(spanX / 8f);
        int ey = Mathf.CeilToInt(spanY / 8f);

        _shader.SetInt("EraseXMin", xMin);
        _shader.SetInt("EraseXMax", xMax);
        _shader.SetInt("EraseYMin", yMin);
        _shader.SetInt("EraseYMax", yMax);
        _shader.SetInt("EraseType", targetTypeId);
        _shader.SetBuffer(_kErase, "Current", _current);
        _shader.SetBuffer(_kErase, "Next",    _next);
        _shader.Dispatch(_kErase, ex, ey, 1);

        int erased = Mathf.Min(maxCount, spanX * spanY);
        if (targetTypeId > 0 && targetTypeId < 16)
        {
            _typeCounts[targetTypeId] = Mathf.Max(0, _typeCounts[targetTypeId] - erased);
        }
        return erased;
    }

    public bool HasSandInRegion(int xMin, int xMax, int yMin, int yMax, int targetTypeId, int minThreshold = 1)
    {
        return HasSandInSource(targetTypeId, minThreshold);
    }

    public bool HasSandInSource(int targetTypeId, int minThreshold = 1)
    {
        if (targetTypeId > 0 && targetTypeId < 16)
            return _typeCounts[targetTypeId] >= minThreshold;

        int total = 0;
        for (int i = 1; i < 16; i++) total += _typeCounts[i];
        return total >= minThreshold;
    }


    private void OnDestroy()
    {
        _current?.Release();
        _next?.Release();
        _claims?.Release();
        if (_texture != null) _texture.Release();
    }
}
