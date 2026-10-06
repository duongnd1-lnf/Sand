using UnityEngine;

public class GridPlacementGhost : MonoBehaviour
{
    [SerializeField] private SpriteRenderer _fillRenderer;
    [SerializeField] private LineRenderer _borderRenderer;

    [SerializeField] private Color _validColor = new Color(0.2f, 1f, 0.3f, 0.4f);
    [SerializeField] private Color _invalidColor = new Color(1f, 0.2f, 0.2f, 0.4f);

    public void UpdatePreview(Vector3 worldCenter, Vector2 worldDimensions, bool isValid)
    {
        gameObject.SetActive(true);
        transform.position = worldCenter;

        Color c = isValid ? _validColor : _invalidColor;

        if (_fillRenderer != null)
        {
            _fillRenderer.color = c;
            _fillRenderer.transform.localScale = new Vector3(worldDimensions.x, worldDimensions.y, 1f);
        }

        if (_borderRenderer != null)
        {
            _borderRenderer.startColor = c;
            _borderRenderer.endColor = c;
            float hx = worldDimensions.x * 0.5f;
            float hy = worldDimensions.y * 0.5f;
            _borderRenderer.SetPosition(0, new Vector3(-hx, -hy, 0));
            _borderRenderer.SetPosition(1, new Vector3(-hx, hy, 0));
            _borderRenderer.SetPosition(2, new Vector3(hx, hy, 0));
            _borderRenderer.SetPosition(3, new Vector3(hx, -hy, 0));
        }
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }
}
