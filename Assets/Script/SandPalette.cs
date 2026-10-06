using UnityEngine;

// Bảng màu dùng chung cho mọi hồ và ống: tạo 1 asset, kéo vào tất cả.
[CreateAssetMenu(menuName = "Game/Sand Palette", fileName = "palette")]
public class SandPalette : ScriptableObject
{
    public const int Shades = 8;       // phải trùng #define SHADES trong compute shader

    public SandColor[] types;

    public int TypeCount => types == null ? 0 : Mathf.Min(types.Length, 15);

    public Vector4[] Build()
    {
        var pal = new Vector4[16 * Shades];   // type 0 = trống = trong suốt
        for (int i = 0; i < TypeCount; i++)
        {
            if (types[i] == null) continue;
            for (int s = 0; s < Shades; s++)
                pal[(i + 1) * Shades + s] = types[i].Get(s);
        }
        return pal;
    }

    public void Apply(ComputeShader cs) => cs.SetVectorArray("Palette", Build());
}
