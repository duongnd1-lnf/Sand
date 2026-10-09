using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum MoveAxisConstraint
{
    VerticalOnly,
    HorizontalOnly,
    Free2D
}

[SelectionBase]
public abstract class GridObject : MonoBehaviour
{
    [Header("Movement Constraints")]
    [SerializeField] private MoveAxisConstraint _axisConstraint = MoveAxisConstraint.VerticalOnly;

    [Header("Grid Properties")]
    [SerializeField] private Vector2Int _gridPosition;
    [SerializeField] private bool _isDraggable = true;
    [SerializeField] private Collider2D _collider2D;

    [Header("Drag Visual Settings")]
    [SerializeField] private float _dragScaleMultiplier = 1.05f;
    [SerializeField] private float _snapDuration = 0.12f;

    // Events (Standard C# Action events)
    public event Action<Vector2Int> OnPlaced;
    public event Action<GridObject> OnPickup;
    public event Action<GridObject> OnDrop;

    private Vector3 _originalScale;
    private Coroutine _snapCoroutine;

    public Vector2Int GridPosition => _gridPosition;
    public bool IsDraggable { get => _isDraggable; set => _isDraggable = value; }
    public MoveAxisConstraint AxisConstraint { get => _axisConstraint; set => _axisConstraint = value; }
    public GridManager Manager => GridManager.Instance;
    public Collider2D Collider2D => _collider2D;

    public abstract IReadOnlyList<Vector2Int> OccupiedOffsets { get; }
    public abstract Vector2Int Size { get; set; }

    public abstract Vector3 GetWorldPosition(GridManager manager, Vector2Int originCell);
    public abstract Vector2Int WorldToOriginCell(GridManager manager, Vector3 worldPos);
    public abstract void ApplyRotation(GridRotation rotation);

    protected virtual void Awake()
    {
        _originalScale = transform.localScale;
    }

    protected virtual void Start()
    {
        // Determine the origin cell based on current world position.
        Vector2Int cell = GridManager.Instance.WorldToCellPosition(transform.position, this);

        // Validate placement; if not valid, fallback to saved GridPosition or nearest valid cell.
        if (!GridManager.Instance.CanPlaceObject(this, cell))
        {
            cell = GridManager.Instance.CanPlaceObject(this, _gridPosition)
                ? _gridPosition
                : GridManager.Instance.FindNearestValidCell(this, cell);
        }

        // Store origin cell and register occupancy.
        GridManager.Instance.TryPlaceObject(this, cell, animate: false);
    }

    public void SetGridPosition(Vector2Int newPosition)
    {
        _gridPosition = newPosition;
    }

    public void SnapToWorldPosition(Vector3 targetWorldPos, bool animate = true)
    {
        targetWorldPos.z = transform.position.z;
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
        // Remove object from grid so that its cells become free while dragging.
        GridManager.Instance.RemoveObject(this);

        if (_snapCoroutine != null)
        {
            StopCoroutine(_snapCoroutine);
            _snapCoroutine = null;
        }

        transform.localScale = _originalScale * _dragScaleMultiplier;
        OnPickup?.Invoke(this);
    }

    public virtual void OnEndDrag()
    {
        // When drop is finished we attempt to place the object at its current GridPosition.
        // The GridDragController should have already updated GridPosition via SetGridPosition.
        bool placed = GridManager.Instance.TryPlaceObject(this, GridPosition, animate: true);
        if (!placed)
        {
            // Restore original position (the GridManager will keep the previous footprint).
            GridManager.Instance.TryPlaceObject(this, GridPosition, animate: true);
        }

        transform.localScale = _originalScale;
        OnDrop?.Invoke(this);
    }
}
