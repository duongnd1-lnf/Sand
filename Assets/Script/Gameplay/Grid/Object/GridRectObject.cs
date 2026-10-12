using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Rectangular or square GridObject.
/// Its shape is defined strictly by width and height (_size).
/// Visual transform is centered over the entire rectangular bounding box.
/// </summary>
[SelectionBase]
public class GridRectObject : GridObject
{
    [Header("Rectangle Properties")]
    [SerializeField] private Vector2Int _size = Vector2Int.one;

    private readonly List<Vector2Int> _cachedOffsets = new List<Vector2Int>();

    public override Vector2Int Size
    {
        get => _size;
        set => SetSize(value);
    }

    public override IReadOnlyList<Vector2Int> OccupiedOffsets
    {
        get
        {
            if (_cachedOffsets.Count != _size.x * _size.y)
            {
                RebuildOffsets();
            }
            return _cachedOffsets;
        }
    }

    protected override void Awake()
    {
        base.Awake();
        RebuildOffsets();
    }

    private void OnValidate()
    {
        if (_size.x < 1) _size.x = 1;
        if (_size.y < 1) _size.y = 1;
        RebuildOffsets();
    }

    public void SetSize(Vector2Int newSize)
    {
        _size = new Vector2Int(Mathf.Max(1, newSize.x), Mathf.Max(1, newSize.y));
        RebuildOffsets();
    }

    private void RebuildOffsets()
    {
        _cachedOffsets.Clear();
        for (int x = 0; x < _size.x; x++)
        {
            for (int y = 0; y < _size.y; y++)
            {
                _cachedOffsets.Add(new Vector2Int(x, y));
            }
        }
    }

    public override Vector3 GetWorldPosition(GridManager manager, Vector2Int originCell)
    {
        return manager.CellToWorldPosition(originCell, _size);
    }

    public override Vector2Int WorldToOriginCell(GridManager manager, Vector3 worldPos)
    {
        return manager.WorldToCellPosition(worldPos, _size);
    }

    public override void ApplyRotation(GridRotation rotation)
    {
        int angle = (int)rotation;
        if (angle == 90 || angle == 270)
        {
            _size = new Vector2Int(_size.y, _size.x);
            RebuildOffsets();
        }
        transform.rotation = Quaternion.Euler(0f, 0f, -angle);
    }
}
