using System;
using UnityEngine;

/// <summary>
/// Base class for sand simulation pools.
/// Handles ComputeBuffer allocation, compute shader dispatch, and pipe port IO.
/// </summary>
public abstract class SandPool : MonoBehaviour
{
    public enum Side { Bottom = 0, Top = 1, Left = 2, Right = 3 }

    [Header("Simulation Resolution")]
    public int width = 256;
    public int height = 256;
    public int stepsPerFrame = 1;

    [Header("Rendering")]
    public ComputeShader shader;       // Sand.compute
    public SandPalette palette;
    public MeshRenderer target;        // Quad mesh renderer

    protected ComputeBuffer current, next;
    protected ComputeBuffer defaultCounterBuffer;
    protected RenderTexture tex;
    protected int kClear, kClearNext, kSimulate, kRender, kExtract, kInject;
    protected int frame;
    protected int gx, gy;

    public bool Ready => current != null;

    protected virtual void Start()
    {
        InitPool();
    }

    public void InitPool()
    {
        if (shader == null || target == null || palette == null)
        {
            Debug.LogError($"[{GetType().Name}] Missing required references (shader, target, or palette).", this);
            return;
        }

        shader = Instantiate(shader);

        kClear = shader.FindKernel("Clear");
        kClearNext = shader.FindKernel("ClearNext");
        kSimulate = shader.FindKernel("Simulate");
        kRender = shader.FindKernel("Render");
        kExtract = shader.FindKernel("Extract");
        kInject = shader.FindKernel("Inject");

        current?.Release();
        next?.Release();
        current = new ComputeBuffer(width * height, sizeof(uint));
        next = new ComputeBuffer(width * height, sizeof(uint));

        defaultCounterBuffer?.Release();
        defaultCounterBuffer = new ComputeBuffer(2, sizeof(int));
        defaultCounterBuffer.SetData(new int[] { 0, int.MaxValue });

        if (tex != null) tex.Release();
        tex = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
        tex.enableRandomWrite = true;
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Create();

        Material mat = target.material;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);

        gx = Mathf.CeilToInt(width / 8f);
        gy = Mathf.CeilToInt(height / 8f);

        shader.SetInt("Width", width);
        shader.SetInt("Height", height);

        palette.Apply(shader);

        OnPoolInitialized();
        RenderTextureUpdate();
    }

    protected abstract void OnPoolInitialized();

    // ------------------------------------------------------------------
    // API for Sand Transfer / Pipes
    // ------------------------------------------------------------------

    public int EdgeCells(Side side) =>
        (side == Side.Bottom || side == Side.Top) ? width : height;

    public float CellWorldSize(Side side)
    {
        Vector3 s = target.transform.lossyScale;
        return (side == Side.Bottom || side == Side.Top) ? s.x / width : s.y / height;
    }

    public int EdgeOffset(Side side, float edgePos, int lanes)
    {
        int cells = EdgeCells(side);
        int center = Mathf.RoundToInt(Mathf.Clamp01(edgePos) * cells);
        return Mathf.Clamp(center - lanes / 2, 0, Mathf.Max(0, cells - lanes));
    }

    public void GetContact(Side side, float t, out Vector3 point, out Vector3 outward, out Vector3 along)
    {
        Vector3 lp, ln, la;
        switch (side)
        {
            case Side.Bottom: lp = new Vector3(t - 0.5f, -0.5f, 0); ln = Vector3.down; la = Vector3.right; break;
            case Side.Top: lp = new Vector3(t - 0.5f, 0.5f, 0); ln = Vector3.up; la = Vector3.right; break;
            case Side.Left: lp = new Vector3(-0.5f, t - 0.5f, 0); ln = Vector3.left; la = Vector3.up; break;
            default: lp = new Vector3(0.5f, t - 0.5f, 0); ln = Vector3.right; la = Vector3.up; break;
        }

        Transform tr = target.transform;
        point = tr.TransformPoint(lp);
        outward = tr.TransformDirection(ln).normalized;
        along = tr.TransformDirection(la).normalized;
    }

    public void Extract(ComputeBuffer pipe, int pipeLength, int lanes, Side side, int offset, bool flip, int depth, int filterType = 0)
    {
        SetPort(pipeLength, lanes, side, offset, flip, depth, acceptType: 0, filterType: filterType);
        shader.SetBuffer(kExtract, "Current", current);
        shader.SetBuffer(kExtract, "PipeBuf", pipe);
        shader.Dispatch(kExtract, Mathf.CeilToInt(lanes / 64f), 1, 1);
    }

    public virtual void Inject(ComputeBuffer pipe, int pipeLength, int lanes, Side side, int offset, bool flip, int acceptType = 0)
    {
        SetPort(pipeLength, lanes, side, offset, flip, 1, acceptType: acceptType, filterType: 0);
        shader.SetBuffer(kInject, "Current", current);
        shader.SetBuffer(kInject, "PipeBuf", pipe);
        shader.SetBuffer(kInject, "PortCounter", defaultCounterBuffer);
        shader.Dispatch(kInject, Mathf.CeilToInt(lanes / 64f), 1, 1);
    }

    void SetPort(int pipeLength, int lanes, Side side, int offset, bool flip, int depth, int acceptType = 0, int filterType = 0)
    {
        shader.SetInt("PipeLength", pipeLength);
        shader.SetInt("PortLanes", lanes);
        shader.SetInt("PortSide", (int)side);
        shader.SetInt("PortOffset", offset);
        shader.SetInt("PortFlip", flip ? 1 : 0);
        shader.SetInt("PortHeight", depth);
        shader.SetInt("PortAcceptType", acceptType);
        shader.SetInt("PortFilterType", filterType);
    }

    public void ClearAll()
    {
        if (current == null || next == null) return;
        shader.SetBuffer(kClear, "Current", current);
        shader.SetBuffer(kClear, "Next", next);
        shader.Dispatch(kClear, gx, gy, 1);
        RenderTextureUpdate();
    }

    public void RenderTextureUpdate()
    {
        if (current == null || tex == null) return;
        shader.SetBuffer(kRender, "Current", current);
        shader.SetTexture(kRender, "Result", tex);
        shader.Dispatch(kRender, gx, gy, 1);
    }

    public void Step()
    {
        if (current == null || next == null) return;
        shader.SetBuffer(kClearNext, "Next", next);
        shader.Dispatch(kClearNext, gx, gy, 1);

        shader.SetInt("Frame", frame++);
        shader.SetBuffer(kSimulate, "Current", current);
        shader.SetBuffer(kSimulate, "Next", next);
        shader.Dispatch(kSimulate, gx, gy, 1);

        (current, next) = (next, current);
    }

    protected virtual void OnDestroy()
    {
        current?.Release();
        next?.Release();
        defaultCounterBuffer?.Release();
        if (tex != null) tex.Release();
    }
}