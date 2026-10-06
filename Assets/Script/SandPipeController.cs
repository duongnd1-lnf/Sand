using UnityEngine;

// Gán hồ nguồn, hồ đích và cạnh tiếp xúc; controller đọc MeshRenderer target của từng hồ
// để tự tạo Quad ống đúng vị trí, hướng, kích thước, rồi cấu hình SandPipe.
public class SandPipeController : MonoBehaviour
{
    [Header("Hồ nguồn (lấy cát)")]
    public SandPool fromPool;
    public SandPool.Side fromSide = SandPool.Side.Bottom;
    [Range(0f, 1f)] public float fromEdgePos = 0.5f;   // vị trí dọc cạnh: 0 = đầu, 1 = cuối
    [Min(1)] public int fromDepth = 4;                 // bề dày (pixel) tầng được phép lấy

    [Header("Hồ đích (thêm cát)")]
    public SandPool toPool;
    public SandPool.Side toSide = SandPool.Side.Top;
    [Range(0f, 1f)] public float toEdgePos = 0.5f;

    [Header("Ống")]
    [Min(1)] public int lanes = 8;          // bề rộng ống, tính theo ô của hồ nguồn
    [Min(1)] public int speed = 1;          // số ô tiến mỗi tick
    public float embed = 0f;                // lấn vào trong mỗi hồ (đơn vị thế giới) để ống "chiếm nửa mỗi bên"
    public ComputeShader pipeShader;        // Pipe.compute
    public SandPalette palette;             // để trống = dùng palette của hồ nguồn
    public Material pipeMaterial;           // để trống = nhân bản material của hồ nguồn
    public Color emptyColor = new Color(0.18f, 0.18f, 0.22f, 1f);
    public bool buildOnStart = true;

    SandPipe pipe;

    struct Geo
    {
        public Vector3 center;
        public Quaternion rot;
        public Vector3 scale;
        public int length, lanes, offA, offB;
        public bool flipA, flipB;
        public Vector3 a, b;
    }

    void Start()
    {
        if (buildOnStart) Build();
    }

    [ContextMenu("Rebuild")]
    public void Build()
    {
        if (pipe != null) Destroy(pipe.gameObject);

        if (!TryCompute(out Geo g))
        {
            Debug.LogError("SandPipeController: thiếu hồ/target hoặc 2 điểm tiếp xúc trùng nhau.", this);
            return;
        }

        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "SandPipe";
        Destroy(go.GetComponent<Collider>());   // không chặn raycast vẽ cát
        go.transform.SetPositionAndRotation(g.center, g.rot);
        go.transform.localScale = g.scale;

        MeshRenderer mr = go.GetComponent<MeshRenderer>();
        mr.sharedMaterial = pipeMaterial != null ? pipeMaterial : fromPool.target.sharedMaterial;
        mr.sortingLayerID = fromPool.target.sortingLayerID;
        mr.sortingOrder = fromPool.target.sortingOrder + 1;

        pipe = go.AddComponent<SandPipe>();
        pipe.shader = pipeShader;
        pipe.palette = palette != null ? palette : fromPool.palette;
        pipe.view = mr;
        pipe.emptyColor = emptyColor;
        pipe.length = g.length;
        pipe.lanes = g.lanes;
        pipe.speed = speed;
        pipe.source = new SandPipe.Port
        {
            pool = fromPool,
            side = fromSide,
            offset = g.offA,
            flip = g.flipA,
            depth = fromDepth
        };
        pipe.destination = new SandPipe.Port
        {
            pool = toPool,
            side = toSide,
            offset = g.offB,
            flip = g.flipB,
            depth = 1
        };
        pipe.Init();
    }

    bool TryCompute(out Geo g)
    {
        g = default;
        if (fromPool == null || toPool == null || fromPool.target == null || toPool.target == null)
            return false;

        int cellsA = fromPool.EdgeCells(fromSide);
        int cellsB = toPool.EdgeCells(toSide);
        int n = Mathf.Max(1, Mathf.Min(lanes, Mathf.Min(cellsA, cellsB)));

        int offA = fromPool.EdgeOffset(fromSide, fromEdgePos, n);
        int offB = toPool.EdgeOffset(toSide, toEdgePos, n);

        // Điểm tiếp xúc nằm đúng giữa dải lane thực tế (đã kẹp trong hồ)
        fromPool.GetContact(fromSide, (offA + n * 0.5f) / cellsA, out Vector3 a, out Vector3 aOut, out Vector3 aAlong);
        toPool.GetContact(toSide, (offB + n * 0.5f) / cellsB, out Vector3 b, out Vector3 bOut, out Vector3 bAlong);

        a -= aOut * embed;
        b -= bOut * embed;

        Vector3 fwd = fromPool.target.transform.forward;
        Vector3 d = Vector3.ProjectOnPlane(b - a, fwd);
        float dist = d.magnitude;
        if (dist < 1e-4f) return false;

        Vector3 dir = d / dist;
        Vector3 perp = Vector3.Cross(fwd, dir);   // hướng tăng lane (trục Y cục bộ của Quad ống)
        float cell = fromPool.CellWorldSize(fromSide);

        g.a = a;
        g.b = a + d;
        g.center = a + d * 0.5f;
        g.rot = Quaternion.LookRotation(fwd, perp);   // trục X cục bộ = dir (nguồn -> đích)
        g.scale = new Vector3(dist, n * cell, 1f);
        g.length = Mathf.Max(2, Mathf.RoundToInt(dist / cell));
        g.lanes = n;
        g.offA = offA;
        g.offB = offB;
        g.flipA = Vector3.Dot(perp, aAlong) < 0f;
        g.flipB = Vector3.Dot(perp, bAlong) < 0f;
        return true;
    }

    // Xem trước trong Scene view khi chọn controller
    void OnDrawGizmosSelected()
    {
        if (!TryCompute(out Geo g)) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(g.a, 0.05f);
        Gizmos.color = Color.magenta;
        Gizmos.DrawWireSphere(g.b, 0.05f);

        Gizmos.color = Color.yellow;
        Gizmos.matrix = Matrix4x4.TRS(g.center, g.rot, g.scale);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(1f, 1f, 0f));
        Gizmos.matrix = Matrix4x4.identity;
    }

    void OnDestroy()
    {
        if (pipe != null) Destroy(pipe.gameObject);
    }
}