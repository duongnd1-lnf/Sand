using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Color", fileName = "color")]
public class SandColor : ScriptableObject
{
    // Giữ nguyên tên field "color" để asset cũ không mất dữ liệu
    [SerializeField] private List<Color> color = new List<Color> { Color.magenta };

    public int Count => color != null ? color.Count : 0;

    // Lấy sắc độ thứ i (lặp vòng nếu danh sách ngắn hơn số shade)
    public Color Get(int i)
    {
        if (color == null || color.Count == 0) return Color.magenta;
        return color[i % color.Count];
    }
}