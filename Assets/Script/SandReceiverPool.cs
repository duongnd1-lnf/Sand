using System;
using UnityEngine;

/// <summary>
/// Receiver sand pool:
/// - Initialized empty.
/// - Does not require internal sand simulation resolution.
/// - Strictly accepts only 1 specific color type (acceptedColor or acceptedSandType).
/// - Has a configurable capacity (e.g. 1000 particles).
/// - Automatically covers/fills up proportional to the percentage of sand received (0% -> 100%).
/// - Halts receiving when full (100%) and fires the onPoolFull event.
/// </summary>
public class SandReceiverPool : MonoBehaviour
{
    [Header("Accepted Color (Single Color Filter)")]
    public SandColor acceptedColor;

    [Range(1, 15)] public int acceptedSandType = 1;

    [Header("Capacity & Target Fill")]
    [Min(1)] public int capacity = 1000;

    [SerializeField] private int _receivedCount = 0;

    [Header("Rendering")]
    public MeshRenderer target;
    public SandPalette palette;
    public Color emptyColor = new Color(0f, 0f, 0f, 0f);

    // Events (Standard C# Action events)
    public event Action onPoolFull;
    public event Action<float> onFillChanged;

    public int ReceivedCount => _receivedCount;
    public float FillPercentage => Mathf.Clamp01((float)_receivedCount / Mathf.Max(1, capacity));
    public bool IsFull => _receivedCount >= capacity;
    public int Capacity => capacity;
    public bool Ready => true;

    private Texture2D _fillTexture;
    private bool _fullEventFired = false;
    private const int TextureWidth = 32;
    private const int TextureHeight = 64;

    private void Awake()
    {
        if (target == null)
        {
            target = GetComponentInChildren<MeshRenderer>();
        }
    }

    private void Start()
    {
        InitVisual();
    }

    private void InitVisual()
    {
        if (target == null) return;

        if (_fillTexture == null)
        {
            _fillTexture = new Texture2D(TextureWidth, TextureHeight, TextureFormat.RGBA32, false);
            _fillTexture.filterMode = FilterMode.Point;
            _fillTexture.wrapMode = TextureWrapMode.Clamp;
        }

        Material mat = target.material;
        if (mat != null)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", _fillTexture);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", _fillTexture);
        }

        UpdateFillVisual();
    }

    /// <summary>
    /// Returns the resolved sand type ID (1..15) accepted by this receiver.
    /// </summary>
    public int GetAcceptedType()
    {
        if (acceptedColor != null && palette != null && palette.types != null)
        {
            int idx = Array.IndexOf(palette.types, acceptedColor);
            if (idx >= 0) return idx + 1;
        }
        return Mathf.Clamp(acceptedSandType, 1, 15);
    }

    /// <summary>
    /// Checks whether this receiver accepts a given sand type.
    /// </summary>
    public bool AcceptsSandType(int type)
    {
        return type == 0 || type == GetAcceptedType();
    }

    /// <summary>
    /// Gets the base color representing this receiver's accepted sand.
    /// </summary>
    public Color GetVisualColor()
    {
        if (acceptedColor != null)
        {
            return acceptedColor.Get(0);
        }

        int type = GetAcceptedType();
        if (palette != null && palette.types != null && type >= 1 && type <= palette.TypeCount)
        {
            SandColor col = palette.types[type - 1];
            if (col != null) return col.Get(0);
        }

        return Color.white;
    }

    /// <summary>
    /// Receives sand particles into this receiver.
    /// Returns the number of particles actually accepted.
    /// </summary>
    public int ReceiveSand(int amount, int sandType = 0)
    {
        if (amount <= 0 || IsFull) return 0;
        if (sandType != 0 && !AcceptsSandType(sandType)) return 0;

        int remaining = capacity - _receivedCount;
        int accepted = Mathf.Min(amount, remaining);
        _receivedCount += accepted;

        UpdateFillVisual();
        onFillChanged?.Invoke(FillPercentage);

        if (IsFull && !_fullEventFired)
        {
            _fullEventFired = true;
            onPoolFull?.Invoke();
        }

        return accepted;
    }

    /// <summary>
    /// Updates the sand fill texture proportional to FillPercentage.
    /// </summary>
    public void UpdateFillVisual()
    {
        if (target == null) return;
        if (_fillTexture == null)
        {
            InitVisual();
            if (_fillTexture == null) return;
        }

        float p = FillPercentage;
        int targetRow = p >= 1f ? TextureHeight : Mathf.RoundToInt(p * TextureHeight);
        if (p > 0f && targetRow == 0) targetRow = 1;
        targetRow = Mathf.Min(targetRow, TextureHeight);

        Color[] pixels = new Color[TextureWidth * TextureHeight];
        Color baseCol = GetVisualColor();

        for (int y = 0; y < TextureHeight; y++)
        {
            for (int x = 0; x < TextureWidth; x++)
            {
                if (y < targetRow)
                {
                    // Subtle grain shading pattern
                    int shade = (x * 7 + y * 13) % 8;
                    float factor = 0.88f + shade * 0.035f;
                    pixels[y * TextureWidth + x] = new Color(
                        Mathf.Clamp01(baseCol.r * factor),
                        Mathf.Clamp01(baseCol.g * factor),
                        Mathf.Clamp01(baseCol.b * factor),
                        1f
                    );
                }
                else
                {
                    pixels[y * TextureWidth + x] = emptyColor;
                }
            }
        }

        _fillTexture.SetPixels(pixels);
        _fillTexture.Apply();
    }

    /// <summary>
    /// Resets the receiver to completely empty.
    /// </summary>
    [ContextMenu("Reset Receiver")]
    public void ResetPool()
    {
        _receivedCount = 0;
        _fullEventFired = false;
        UpdateFillVisual();
        onFillChanged?.Invoke(0f);
    }

    private void OnDestroy()
    {
        if (_fillTexture != null)
        {
            Destroy(_fillTexture);
            _fillTexture = null;
        }
    }
}
