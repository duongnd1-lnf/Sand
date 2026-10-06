using UnityEngine;

// Phần mô phỏng của ống. Thường không tự đặt tay: SandPipeController tạo và cấu hình.
public class SandPipe : MonoBehaviour
{
    [System.Serializable]
    public class Port
    {
        public SandPool pool;
        public SandPool.Side side;
        public int offset;     // ô đầu tiên của cổng dọc cạnh hồ
        public bool flip;      // đảo chiều lane so với chiều dọc cạnh
        public int depth = 1;  // chỉ dùng cho nguồn: bề dày (pixel) được phép lấy
    }

    [HideInInspector] public ComputeShader shader;   // Pipe.compute
    [HideInInspector] public MeshRenderer view;
    [HideInInspector] public SandPalette palette;
    [HideInInspector] public Color emptyColor;

    public Port source;
    public Port destination;
    public int length = 64;
    public int lanes = 8;
    public int speed = 1;

    ComputeBuffer buffer;
    RenderTexture tex;
    int kAdvance, kRender;

    public void Init()
    {
        shader = Instantiate(shader);
        kAdvance = shader.FindKernel("Advance");
        kRender = shader.FindKernel("Render");

        buffer = new ComputeBuffer(length * lanes, sizeof(uint));
        buffer.SetData(new uint[length * lanes]);

        tex = new RenderTexture(length, lanes, 0, RenderTextureFormat.ARGB32);
        tex.enableRandomWrite = true;
        tex.filterMode = FilterMode.Point;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Create();

        Material mat = view.material;
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);

        shader.SetInt("Length", length);
        shader.SetInt("Lanes", lanes);
        shader.SetVector("EmptyColor", emptyColor);
        palette.Apply(shader);
    }

    void Update()
    {
        if (buffer == null || !source.pool.Ready || !destination.pool.Ready)
            return;

        // 1) Hạt cuối ống -> hồ đích (nếu chỗ sát cạnh còn trống)
        destination.pool.Inject(buffer, length, lanes, destination.side, destination.offset, destination.flip);

        // 2) Cả đoàn hạt dịch về phía đích
        int groups = Mathf.CeilToInt(lanes / 64f);
        for (int i = 0; i < speed; i++)
        {
            shader.SetBuffer(kAdvance, "Buf", buffer);
            shader.Dispatch(kAdvance, groups, 1, 1);
        }

        // 3) Đầu ống trống -> lấy thêm hạt từ hồ nguồn
        source.pool.Extract(buffer, length, lanes, source.side, source.offset, source.flip, source.depth);

        // 4) Vẽ ống
        shader.SetBuffer(kRender, "Buf", buffer);
        shader.SetTexture(kRender, "Result", tex);
        shader.Dispatch(kRender, Mathf.CeilToInt(length / 8f), Mathf.CeilToInt(lanes / 8f), 1);
    }

    void OnDestroy()
    {
        buffer?.Release();
        if (tex != null) tex.Release();
    }
}