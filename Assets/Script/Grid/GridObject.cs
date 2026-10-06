using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Component attached to any 3D object that lives and moves within the Grid.
/// Uses a 3D BoxCollider for physics/raycasting and defines footprint size (X, Y) in grid units.
/// </summary>
[SelectionBase]
[RequireComponent(typeof(BoxCollider))]
public class GridObject : MonoBehaviour
{
    [Header("Grid Object Properties")]
    [Tooltip("Size in grid cells (width = X, height = Y). E.g., (1, 1), (2, 1), (2, 2)")]
    [SerializeField] private Vector2Int _size = new Vector2Int(1, 1);

    [Tooltip("Current bottom-left cell coordinate in the grid.")]
    [SerializeField] private Vector2Int _gridPosition;

    [Tooltip("Can this object be dragged and moved around the grid?")]
    [SerializeField] private bool _isDraggable = true;

    [Tooltip("Reference to the GridManager. If null, will be assigned automatically.")]
    [SerializeField] private GridManager _gridManager;

    [Header("3D Collider Settings")]
    [Tooltip("Thickness / depth along Z-axis for the 3D BoxCollider.")]
    [SerializeField] private float _colliderDepth = 1f;

    [Tooltip("Automatically adjust BoxCollider size to match grid cell dimensions.")]
    [SerializeField] private bool _autoConfigureCollider = true;

    [Header("Drag Visual Feedback")]
    [Tooltip("Scale multiplier applied while dragging for tactile feedback.")]
    [SerializeField] private float _dragScaleMultiplier = 1.05f;

    [Tooltip("Sorting order increase while dragging to keep the object on top.")]
    [SerializeField] private int _dragSortingOrderOffset = 100;

    [Tooltip("Optional 3D lift/elevation along Z-axis while dragging.")]
    [SerializeField] private float _dragElevationZ = -0.2f;

    [Tooltip("Duration of smooth snap animation in seconds.")]
    [SerializeField] private float _snapDuration = 0.12f;

    [Header("Events")]
    public UnityEvent<Vector2Int> OnPlaced;
    public UnityEvent OnPickup;
    public UnityEvent OnDrop;

    private Vector3 _originalScale;
    private int _originalSortingOrder;
    private Renderer[] _renderers;
    private Coroutine _snapCoroutine;
    private BoxCollider _boxCollider;

    public Vector2Int Size
    {
        get => new Vector2Int(Mathf.Max(1, _size.x), Mathf.Max(1, _size.y));
        set
        {
            _size = new Vector2Int(Mathf.Max(1, value.x), Mathf.Max(1, value.y));
            ConfigureCollider();
        }
    }

    public Vector2Int GridPosition => _gridPosition;
    public bool IsDraggable { get => _isDraggable; set => _isDraggable = value; }
    public GridManager Manager => _gridManager;
    public BoxCollider Collider => _boxCollider;
    public float DragElevationZ => _dragElevationZ;

    private void Awake()
    {
        _originalScale = transform.localScale;
        _renderers = GetComponentsInChildren<Renderer>(true);
        if (_renderers.Length > 0)
        {
            _originalSortingOrder = _renderers[0].sortingOrder;
        }

        _boxCollider = GetComponent<BoxCollider>();
        ConfigureCollider();
    }

    private void Start()
    {
        if (_gridManager == null)
        {
            _gridManager = FindFirstObjectByType<GridManager>();
        }

        ConfigureCollider();
    }

    public void Initialize(GridManager manager)
    {
        _gridManager = manager;
        ConfigureCollider();
    }

    public void SetGridPosition(Vector2Int newPosition)
    {
        _gridPosition = newPosition;
    }

    /// <summary>
    /// Adjusts the 3D BoxCollider to match the grid cell footprint and collider depth.
    /// </summary>
    public void ConfigureCollider()
    {
        if (!_autoConfigureCollider) return;

        if (_boxCollider == null)
        {
            _boxCollider = GetComponent<BoxCollider>();
            if (_boxCollider == null)
            {
                _boxCollider = GetComponentInChildren<BoxCollider>();
            }
            if (_boxCollider == null)
            {
                _boxCollider = gameObject.AddComponent<BoxCollider>();
            }
        }

        if (_boxCollider != null)
        {
            Vector2 cellSize = _gridManager != null ? _gridManager.GetCellSize() : Vector2.one;
            float width = Mathf.Max(0.2f, Size.x * cellSize.x);
            float height = Mathf.Max(0.2f, Size.y * cellSize.y);
            float depth = Mathf.Max(0.2f, _colliderDepth);
            _boxCollider.size = new Vector3(width, height, depth);
            _boxCollider.center = Vector3.zero;
        }
    }

    /// <summary>
    /// Snaps the object to a world position, optionally with smooth animation.
    /// </summary>
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
            // Smooth ease-out cubic
            float ease = 1f - Mathf.Pow(1f - t, 3f);
            transform.position = Vector3.Lerp(startPos, targetPos, ease);
            yield return null;
        }

        transform.position = targetPos;
        _snapCoroutine = null;
    }

    /// <summary>
    /// Called when the player starts dragging this 3D object.
    /// </summary>
    public void OnStartDrag()
    {
        if (_snapCoroutine != null)
        {
            StopCoroutine(_snapCoroutine);
            _snapCoroutine = null;
        }

        // Tactile scale feedback
        transform.localScale = _originalScale * _dragScaleMultiplier;

        // Elevate visual order
        if (_renderers != null)
        {
            foreach (var r in _renderers)
            {
                if (r != null) r.sortingOrder += _dragSortingOrderOffset;
            }
        }

        OnPickup?.Invoke();
    }

    /// <summary>
    /// Called when the player releases/drops this 3D object.
    /// </summary>
    public void OnEndDrag()
    {
        // Restore scale
        transform.localScale = _originalScale;

        // Restore visual order
        if (_renderers != null)
        {
            foreach (var r in _renderers)
            {
                if (r != null) r.sortingOrder -= _dragSortingOrderOffset;
            }
        }

        OnDrop?.Invoke();
    }

    private void OnValidate()
    {
        _size.x = Mathf.Max(1, _size.x);
        _size.y = Mathf.Max(1, _size.y);
    }

    [ContextMenu("Snap To Nearest Cell")]
    public void SnapToNearestCell()
    {
        if (_gridManager == null)
        {
            _gridManager = FindFirstObjectByType<GridManager>();
        }

        if (_gridManager != null)
        {
            Vector2Int cell = _gridManager.WorldToCellPosition(transform.position, Size);
            _gridPosition = cell;
            transform.position = _gridManager.CellToWorldPosition(cell, Size);
            ConfigureCollider();
        }
    }
}
