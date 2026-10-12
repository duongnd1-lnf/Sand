using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Arbitrary-shaped (polyomino) GridObject.
/// Its shape is defined by an explicit list of cell offsets and a designated pivot cell.
/// Visual transform is anchored to the center of the pivot cell.
/// </summary>
[SelectionBase]
public class GridCustomObject : GridObject
{
    [Header("Custom Shape Properties")]
    [SerializeField] private List<Vector2Int> _occupiedOffsets = new List<Vector2Int> { Vector2Int.zero };
    [SerializeField] private int _pivotIndex = 0;

    public override IReadOnlyList<Vector2Int> OccupiedOffsets => _occupiedOffsets;

    public override Vector2Int Size
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
            _occupiedOffsets.Clear();
            for (int x = 0; x < value.x; x++)
                for (int y = 0; y < value.y; y++)
                    _occupiedOffsets.Add(new Vector2Int(x, y));
            NormalizeOffsets();
        }
    }

    public int PivotIndex
    {
        get => _pivotIndex;
        set
        {
            _pivotIndex = value;
            NormalizeOffsets();
        }
    }

    protected override void Awake()
    {
        base.Awake();
        NormalizeOffsets();
    }

    private Vector2Int ComputePivotOffset()
    {
        if (_occupiedOffsets == null || _occupiedOffsets.Count == 0)
            return Vector2Int.zero;

        if (_pivotIndex >= 0 && _pivotIndex < _occupiedOffsets.Count)
            return _occupiedOffsets[_pivotIndex];

        float sumX = 0f, sumY = 0f;
        foreach (var o in _occupiedOffsets)
        {
            sumX += o.x;
            sumY += o.y;
        }
        float cx = sumX / _occupiedOffsets.Count;
        float cy = sumY / _occupiedOffsets.Count;

        Vector2Int best = _occupiedOffsets[0];
        float bestDist = (best.x - cx) * (best.x - cx) + (best.y - cy) * (best.y - cy);
        for (int i = 0; i < _occupiedOffsets.Count; i++)
        {
            var o = _occupiedOffsets[i];
            float d = (o.x - cx) * (o.x - cx) + (o.y - cy) * (o.y - cy);
            if (d < bestDist)
            {
                bestDist = d;
                best = o;
                _pivotIndex = i;
            }
        }
        return best;
    }

    public void NormalizeOffsets()
    {
        if (_occupiedOffsets == null || _occupiedOffsets.Count == 0)
            return;

        Vector2Int pivot = ComputePivotOffset();
        for (int i = 0; i < _occupiedOffsets.Count; i++)
        {
            var o = _occupiedOffsets[i];
            _occupiedOffsets[i] = new Vector2Int(o.x - pivot.x, o.y - pivot.y);
        }
        _pivotIndex = 0;
    }

    public override Vector3 GetWorldPosition(GridManager manager, Vector2Int originCell)
    {
        return manager.CellToWorldPosition(originCell);
    }

    public override Vector2Int WorldToOriginCell(GridManager manager, Vector3 worldPos)
    {
        return manager.WorldToCellPosition(worldPos);
    }

    public override void ApplyRotation(GridRotation rotation)
    {
        int angle = (int)rotation;
        if (angle == 90 || angle == 270)
        {
            var rotated = new List<Vector2Int>(_occupiedOffsets.Count);
            foreach (var off in _occupiedOffsets)
            {
                if (angle == 90)
                    rotated.Add(new Vector2Int(off.y, -off.x));
                else
                    rotated.Add(new Vector2Int(-off.y, off.x));
            }
            _occupiedOffsets = rotated;
            NormalizeOffsets();
        }
        transform.rotation = Quaternion.Euler(0f, 0f, -angle);
    }
}
