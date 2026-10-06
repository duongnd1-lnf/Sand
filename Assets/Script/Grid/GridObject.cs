using System.Collections;
using UnityEngine;
using UnityEngine.Events;

[SelectionBase]
public class GridObject : MonoBehaviour
{
    [Header("Grid Properties")]
    [SerializeField] private Vector2Int _size = new Vector2Int(1, 1);
    [SerializeField] private Vector2Int _gridPosition;
    [SerializeField] private bool _isDraggable = true;
    [SerializeField] private GridManager _gridManager;
    [SerializeField] private BoxCollider _boxCollider;

    [Header("Drag Visual Settings")]
    [SerializeField] private float _dragScaleMultiplier = 1.05f;
    [SerializeField] private float _dragElevationZ = -0.2f;
    [SerializeField] private float _snapDuration = 0.12f;

    [Header("Events")]
    public UnityEvent<Vector2Int> OnPlaced;
    public UnityEvent OnPickup;
    public UnityEvent OnDrop;

    private Vector3 _originalScale;
    private Coroutine _snapCoroutine;

    public Vector2Int Size
    {
        get => new Vector2Int(Mathf.Max(1, _size.x), Mathf.Max(1, _size.y));
        set => _size = value;
    }

    public Vector2Int GridPosition => _gridPosition;
    public bool IsDraggable { get => _isDraggable; set => _isDraggable = value; }
    public GridManager Manager { get => _gridManager; set => _gridManager = value; }
    public BoxCollider BoxCollider => _boxCollider;
    public float DragElevationZ => _dragElevationZ;

    [Header("Initial Placement")]
    [SerializeField] private bool _registerOnStart = true;
    [SerializeField] private bool _useTransformForInitialCell = true;

    protected virtual void Awake()
    {
        _originalScale = transform.localScale;
    }

    protected virtual void Start()
    {
        if (_registerOnStart && _gridManager != null)
        {
            Vector2Int cell = _useTransformForInitialCell
                ? _gridManager.WorldToCellPosition(transform.position, Size)
                : _gridPosition;
            _gridManager.TryPlaceObject(this, cell, animate: false);
        }
    }

    public void SetGridPosition(Vector2Int newPosition)
    {
        _gridPosition = newPosition;
    }

    public void SnapToWorldPosition(Vector3 targetWorldPos, bool animate = true)
    {
        if (_snapCoroutine != null)
        {
            StopCoroutine(_snapCoroutine);
            _snapCoroutine = null;
        }

        if (animate && gameObject.activeInHierarchy && _snapDuration > 0f)
        {
            _snapCoroutine = StartCoroutine(SmoothSnapRoutine(targetWorldPos, _snapDuration));
        }
        else
        {
            transform.position = targetWorldPos;
        }

        OnPlaced?.Invoke(_gridPosition);
    }

    private IEnumerator SmoothSnapRoutine(Vector3 targetPos, float duration)
    {
        Vector3 startPos = transform.position;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            transform.position = Vector3.Lerp(startPos, targetPos, ease);
            yield return null;
        }

        transform.position = targetPos;
        _snapCoroutine = null;
    }

    public virtual void OnStartDrag()
    {
        if (_snapCoroutine != null)
        {
            StopCoroutine(_snapCoroutine);
            _snapCoroutine = null;
        }

        transform.localScale = _originalScale * _dragScaleMultiplier;
        OnPickup?.Invoke();
    }

    public virtual void OnEndDrag()
    {
        transform.localScale = _originalScale;
        OnDrop?.Invoke();
    }
}
