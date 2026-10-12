using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Game/Color", fileName = "color")]
public class SandColor : ScriptableObject
{
    [SerializeField] private List<Color> color = new List<Color> { Color.magenta };
    [SerializeField] private int id;

    public int Id => id;

    public Color Get(int i)
    {
        if (color == null || color.Count == 0) return Color.magenta;
        return color[i % color.Count];
    }

    public Color Get() {
        return color[Random.Range(0, color.Count)];
    }
}