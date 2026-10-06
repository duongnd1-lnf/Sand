using UnityEngine;

/// <summary>
/// Displays a visual ghost/highlight over the target grid cells during dragging.
/// Indicates whether the drop location is valid (e.g. green) or blocked/invalid (e.g. red).
/// </summary>
public class GridPlacementGhost : MonoBehaviour
{
    [Header("Colors")]
    [SerializeField] private Color _validColor = new Color(0.2f, 1f, 0.3f, 0.4f);
    [SerializeField] private Color _invalidColor = new Color(1f, 0.2f, 0.2f, 0.4f);
    [SerializeField] private Color _borderColorValid = new Color(0.2f, 1f, 0.3f, 0.9f);
    [SerializeField] private Color _borderColorInvalid = new Color(1f, 0.2f, 0.2f, 0.9f);

    [Header("Sorting")]
    [SerializeField] private int _sortingOrder = 50;

    private SpriteRenderer _fillRenderer;
    private LineRenderer _borderRenderer;

    private void Awake()
    {
        CreateVisuals();
        Hide();
    }

    private void CreateVisuals()
    {
        // 1. Fill Quad / Sprite
        GameObject fillGo = new GameObject("GhostFill");
        fillGo.transform.SetParent(transform, false);

        _fillRenderer = fillGo.AddComponent<SpriteRenderer>();
        _fillRenderer.sprite = CreateSolidWhiteSprite();
        _fillRenderer.sortingOrder = _sortingOrder;

        // 2. Border LineRenderer
        GameObject borderGo = new GameObject("GhostBorder");
        borderGo.transform.SetParent(transform, false);

        _borderRenderer = borderGo.AddComponent<LineRenderer>();
        _borderRenderer.useWorldSpace = false;
        _borderRenderer.loop = true;
        _borderRenderer.positionCount = 4;
        _borderRenderer.startWidth = 0.05f;
        _borderRenderer.endWidth = 0.05f;
        _borderRenderer.material = new Material(Shader.Find("Sprites/Default"));
        _borderRenderer.sortingOrder = _sortingOrder + 1;
    }

    private Sprite CreateSolidWhiteSprite()
    {
        Texture2D tex = new Texture2D(2, 2);
        Color[] colors = new Color[4] { Color.white, Color.white, Color.white, Color.white };
        tex.SetPixels(colors);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, 2, 2), new Vector2(0.5f, 0.5f), 2f);
    }

    /// <summary>
    /// Updates the ghost position, size, and validity tint.
    /// </summary>
    public void UpdatePreview(Vector3 worldCenter, Vector2 worldDimensions, bool isValid)
    {
        gameObject.SetActive(true);
        transform.position = worldCenter;

        Color targetColor = isValid ? _validColor : _invalidColor;
        Color targetBorder = isValid ? _borderColorValid : _borderColorInvalid;

        if (_fillRenderer != null)
        {
            _fillRenderer.color = targetColor;
            _fillRenderer.transform.localScale = new Vector3(worldDimensions.x, worldDimensions.y, 1f);
        }

        if (_borderRenderer != null)
        {
            _borderRenderer.startColor = targetBorder;
            _borderRenderer.endColor = targetBorder;

            float hx = worldDimensions.x * 0.5f;
            float hy = worldDimensions.y * 0.5f;

            _borderRenderer.SetPosition(0, new Vector3(-hx, -hy, 0));
            _borderRenderer.SetPosition(1, new Vector3(-hx, hy, 0));
            _borderRenderer.SetPosition(2, new Vector3(hx, hy, 0));
            _borderRenderer.SetPosition(3, new Vector3(hx, -hy, 0));
        }
    }

    /// <summary>
    /// Hides the ghost.
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
