using UnityEngine;

public class SandPool : MonoBehaviour
{
    // Cạnh của hồ mà ống có thể nối vào
    public enum Side { Bottom = 0, Top = 1, Left = 2, Right = 3 }

    public ComputeShader shader;       // Sand.compute (mỗi hồ tự nhân bản instance riêng)
    public SandPalette palette;
    public MeshRenderer target;        // Quad mặc định của Unity + MeshCollider
    public Camera cam;                 // để trống = Camera.main
    public int width = 256;
    public int height = 256;
    public int brushRadius = 4;
    public int stepsPerFrame = 1;

    [Header("Vẽ bằng chuột")]
    public bool allowPaint = true;
    public int selectedType = 1;       // 0 = tẩy, 1.. = loại cát
    public int selectedTypeDelete = -1;// khi selectedType = 0: chỉ tẩy loại này, số âm = tẩy tất cả

    ComputeBuffer current, next;
    RenderTexture tex;
    int kClear, kClearNext, kAddSand, kSimulate, kRender, kExtract, kInject;
    int frame;
    int gx, gy;

    public bool Ready => current != null;

    void Start()
    {
        if (cam == null) cam = Camera.main;

        // Instance riêng để nhiều hồ không ghi đè tham số của nhau
        shader = Instantiate(shader);

        kClear = shader.FindKernel("Clear");
        kClearNext = shader.FindKernel("ClearNext");
        kAddSand = shader.FindKernel("AddSand");
        kSimulate = shader.FindKernel("Simulate");
        kRender = shader.FindKernel("Render");
        kExtract = shader.FindKernel("Extract");
        kInject = shader.FindKernel("Inject");

        current = new ComputeBuffer(width * height, sizeof(uint));
        next = new ComputeBuffer(width * height, sizeof(uint));

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
        ClearAll();
    }

    // ------------------------------------------------------------------
    // API cho ống
    // ------------------------------------------------------------------

    // Số ô dọc theo một cạnh
    public int EdgeCells(Side side) =>
        (side == Side.Bottom || side == Side.Top) ? width : height;

    // Kích thước 1 ô (đơn vị thế giới) dọc theo cạnh. Giả định mesh là Quad 1x1 của Unity.
    public float CellWorldSize(Side side)
    {
        Vector3 s = target.transform.lossyScale;
        return (side == Side.Bottom || side == Side.Top) ? s.x / width : s.y / height;
    }

    // Ô đầu tiên của cổng dọc cạnh, canh giữa quanh edgePos (0..1) và kẹp trong hồ
    public int EdgeOffset(Side side, float edgePos, int lanes)
    {
        int cells = EdgeCells(side);
        int center = Mathf.RoundToInt(Mathf.Clamp01(edgePos) * cells);
        return Mathf.Clamp(center - lanes / 2, 0, Mathf.Max(0, cells - lanes));
    }

    // Điểm tiếp xúc trên cạnh (t = 0..1 dọc cạnh), hướng ra ngoài hồ và hướng tăng dần dọc cạnh (đều ở toạ độ thế giới)
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

    // LẤY: mỗi lane lấy 1 hạt gần cạnh nhất trong bề dày `depth` pixel
    public void Extract(ComputeBuffer pipe, int pipeLength, int lanes, Side side, int offset, bool flip, int depth)
    {
        SetPort(pipeLength, lanes, side, offset, flip, depth);
        shader.SetBuffer(kExtract, "Current", current);
        shader.SetBuffer(kExtract, "PipeBuf", pipe);
        shader.Dispatch(kExtract, Mathf.CeilToInt(lanes / 64f), 1, 1);
    }

    // THÊM: thả hạt cuối ống vào ô sát cạnh
    public void Inject(ComputeBuffer pipe, int pipeLength, int lanes, Side side, int offset, bool flip)
    {
        SetPort(pipeLength, lanes, side, offset, flip, 1);
        shader.SetBuffer(kInject, "Current", current);
        shader.SetBuffer(kInject, "PipeBuf", pipe);
        shader.Dispatch(kInject, Mathf.CeilToInt(lanes / 64f), 1, 1);
    }

    void SetPort(int pipeLength, int lanes, Side side, int offset, bool flip, int depth)
    {
        shader.SetInt("PipeLength", pipeLength);
        shader.SetInt("PortLanes", lanes);
        shader.SetInt("PortSide", (int)side);
        shader.SetInt("PortOffset", offset);
        shader.SetInt("PortFlip", flip ? 1 : 0);
        shader.SetInt("PortHeight", depth);
    }

    // ------------------------------------------------------------------

    void ClearAll()
    {
        shader.SetBuffer(kClear, "Current", current);
        shader.SetBuffer(kClear, "Next", next);
        shader.Dispatch(kClear, gx, gy, 1);
    }

    void HandleKeys()
    {
        if (Input.GetKeyDown(KeyCode.C)) ClearAll();

        int maxType = Mathf.Min(palette.TypeCount, 9);
        if (Input.GetKeyDown(KeyCode.Alpha0)) selectedType = 0;
        for (int n = 1; n <= maxType; n++)
            if (Input.GetKeyDown(KeyCode.Alpha0 + n))
                selectedType = n;
    }

    void Update()
    {
        if (allowPaint)
        {
            HandleKeys();

            bool left = Input.GetMouseButton(0);
            bool right = Input.GetMouseButton(1);

            if (left || right)
            {
                Ray ray = cam.ScreenPointToRay(Input.mousePosition);
                if (Physics.Raycast(ray, out RaycastHit hit) &&
                    hit.collider.gameObject == target.gameObject)
                {
                    Vector2 uv = hit.textureCoord;
                    shader.SetInt("BrushX", Mathf.Clamp((int)(uv.x * width), 0, width - 1));
                    shader.SetInt("BrushY", Mathf.Clamp((int)(uv.y * height), 0, height - 1));
                    shader.SetInt("BrushRadius", brushRadius);
                    shader.SetInt("BrushType", right ? 0 : selectedType);
                    shader.SetInt("BrushTypeDel", right ? -1 : selectedTypeDelete);
                    shader.SetInt("Frame", frame);
                    shader.SetBuffer(kAddSand, "Current", current);
                    shader.Dispatch(kAddSand, gx, gy, 1);
                }
            }
        }

        for (int i = 0; i < stepsPerFrame; i++)
            Step();

        shader.SetBuffer(kRender, "Current", current);
        shader.SetTexture(kRender, "Result", tex);
        shader.Dispatch(kRender, gx, gy, 1);
    }

    void Step()
    {
        shader.SetBuffer(kClearNext, "Next", next);
        shader.Dispatch(kClearNext, gx, gy, 1);

        shader.SetInt("Frame", frame++);
        shader.SetBuffer(kSimulate, "Current", current);
        shader.SetBuffer(kSimulate, "Next", next);
        shader.Dispatch(kSimulate, gx, gy, 1);

        (current, next) = (next, current);
    }

    void OnDestroy()
    {
        current?.Release();
        next?.Release();
        if (tex != null) tex.Release();
    }
}