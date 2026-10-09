using System;
using System.Collections.Generic;
using System.Collections;
using UnityEngine;

public enum MoveAxisConstraint
{
    VerticalOnly,
    HorizontalOnly,
    Free2D
}

[SelectionBase]
public class GridObject : MonoBehaviour
{
    [Header("Movement Constraints")]
    [SerializeField] private MoveAxisConstraint _axisConstraint = MoveAxisConstraint.VerticalOnly;

    [Header("Grid Properties")]
    [SerializeField] private List<Vector2Int> _occupiedOffsets = new List<Vector2Int> { Vector2Int.zero };
    // Index of the cell in _occupiedOffsets that will act as the pivot (origin cell).
    // Must be in range [0, _occupiedOffsets.Count). Default 0.
    [SerializeField] private int _pivotIndex = 0;
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

    // Bounding size derived from occupied offsets
    public Vector2Int Size
    {
        get
        {
            if (_occupiedOffsets == null || _occupiedOffsets.Count == 0)
                return Vector2Int.one;
            int minX = int.MaxValue, minY = int.MaxValue;
            int maxX = int.MinValue, maxY = int.MinValue;
            foreach (var off in _occupiedOffsets)
            {
                if (off.x < minX) minX = off.x;
                if (off.y < minY) minY = off.y;
                if (off.x > maxX) maxX = off.x;
                if (off.y > maxY) maxY = off.y;
            }
            return new Vector2Int(maxX - minX + 1, maxY - minY + 1);
        }
        set
        {
            // Setting size directly is not supported when using explicit occupied offsets.
            // Keep for backward compatibility: generate a rectangular footprint.
            _occupiedOffsets.Clear();
            for (int x = 0; x < value.x; x++)
                for (int y = 0; y < value.y; y++)
                    _occupiedOffsets.Add(new Vector2Int(x, y));
        }
    }

    // Expose the occupied offsets (relative to pivot at (0,0))
    public IReadOnlyList<Vector2Int> OccupiedOffsets => _occupiedOffsets;

    // Compute the most suitable pivot cell among occupied offsets.
    // We pick the occupied cell closest to the geometric centroid of the shape.
    private Vector2Int ComputePivotOffset()
    {
        if (_occupiedOffsets == null || _occupiedOffsets.Count == 0)
            return Vector2Int.zero;
        // Compute centroid as float values
        float sumX = 0f, sumY = 0f;
        foreach (var o in _occupiedOffsets)
        {
            sumX += o.x;
            sumY += o.y;
        }
        float cx = sumX / _occupiedOffsets.Count;
        float cy = sumY / _occupiedOffsets.Count;
        // Find occupied offset with minimal squared distance to centroid
        Vector2Int best = _occupiedOffsets[0];
        float bestDist = (best.x - cx) * (best.x - cx) + (best.y - cy) * (best.y - cy);
        foreach (var o in _occupiedOffsets)
        {
            float d = (o.x - cx) * (o.x - cx) + (o.y - cy) * (o.y - cy);
            if (d < bestDist)
            {
                bestDist = d;
                best = o;
            }
        }
        return best;
    }

    // Ensure pivot cell is at (0,0) by translating offsets so that the chosen pivot becomes origin.
    private void NormalizeOffsets()
    {
        if (_occupiedOffsets == null || _occupiedOffsets.Count == 0)
            return;
        Vector2Int pivot = ComputePivotOffset();
        // Translate all offsets so that pivot becomes (0,0)
        for (int i = 0; i < _occupiedOffsets.Count; i++)
        {
            var o = _occupiedOffsets[i];
            _occupiedOffsets[i] = new Vector2Int(o.x - pivot.x, o.y - pivot.y);
        }
    }

    protected void Awake()
    {
        _originalScale = transform.localScale;
        NormalizeOffsets(); // ensure pivot at chosen cell
    }

    public Vector2Int GridPosition => _gridPosition;
    public bool IsDraggable { get => _isDraggable; set => _isDraggable = value; }
    public MoveAxisConstraint AxisConstraint { get => _axisConstraint; set => _axisConstraint = value; }
    public GridManager Manager => GridManager.Instance;
    public Collider2D Collider2D => _collider2D;



    protected virtual void Start()
    {
        // Determine the pivot cell based on current world position.
        Vector2Int cell = GridManager.Instance.WorldToCellPosition(transform.position, this);

        // Validate placement; if not valid, fallback to saved GridPosition or nearest valid cell.
        if (!GridManager.Instance.CanPlaceObject(this, cell))
        {
            cell = GridManager.Instance.CanPlaceObject(this, _gridPosition)
                ? _gridPosition
                : GridManager.Instance.FindNearestValidCell(this, cell);
        }

        // Store the pivot cell as the object's GridPosition and register occupancy.
        GridManager.Instance.TryPlaceObject(this, cell, animate: false);

    }

    /// <summary>
    /// Applies a discrete rotation (0, 90, 180, 270) to the visual transform and adjusts size dimensions.
    /// </summary>
    public void ApplyRotation(GridRotation rotation)
    {
        int angle = (int)rotation;
        // Rotate occupied offsets around pivot (0,0)
        if (angle == 90 || angle == 270)
        {
            var rotated = new List<Vector2Int>(_occupiedOffsets.Count);
            foreach (var off in _occupiedOffsets)
            {
                // 90° clockwise: (x, y) -> (y, -x)
                // 270° clockwise (or 90° CCW): (x, y) -> (-y, x)
                if (angle == 90)
                    rotated.Add(new Vector2Int(off.y, -off.x));
                else // 270
                    rotated.Add(new Vector2Int(-off.y, off.x));
            }
            _occupiedOffsets = rotated;
            // Re‑normalize so pivot stays at (0,0)
            NormalizeOffsets();
        }
        // Apply visual rotation
        transform.rotation = Quaternion.Euler(0f, 0f, -angle);
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
        // If placement fails we fallback to original position.
        if (!placed)
        {
            // Restore original position (the GridManager will keep the previous footprint).
            GridManager.Instance.TryPlaceObject(this, GridPosition, animate: true);
        }

        transform.localScale = _originalScale;
        OnDrop?.Invoke(this);
    }
}
