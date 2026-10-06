using System;
using UnityEngine;

public class GridDragController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private GridManager _gridManager;
    [SerializeField] private Camera _camera;
    [SerializeField] private LayerMask _dragLayerMask = ~0;

    [Header("Preview (Optional)")]
    [SerializeField] private GridPlacementGhost _placementGhost;
    [SerializeField] private bool _showGhostPreview = true;

    private GridObject _currentDraggedObject;
    private Vector2Int _previousValidCell;
    private Vector3 _dragOffset;
    private Plane _dragPlane;
    private bool _isDragging;

    public GridObject CurrentDraggedObject => _currentDraggedObject;
    public bool IsDragging => _isDragging;

    private void Update()
    {
        Vector2 screenPosition = Input.mousePosition;

        if (Input.GetMouseButtonDown(0) && !_isDragging)
        {
            TryStartDrag(screenPosition);
        }
        else if (Input.GetMouseButton(0) && _isDragging)
        {
            UpdateDrag(screenPosition);
        }
        else if (Input.GetMouseButtonUp(0) && _isDragging)
        {
            EndDrag();
        }
    }

    private void TryStartDrag(Vector2 screenPosition)
    {
        Ray ray = _camera.ScreenPointToRay(screenPosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, _dragLayerMask);

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            GridObject candidate = hit.collider.GetComponentInParent<GridObject>();
            if (candidate != null && candidate.IsDraggable)
            {
                _currentDraggedObject = candidate;
                _previousValidCell = candidate.GridPosition;

                Vector3 planeNormal = -_camera.transform.forward;
                _dragPlane = new Plane(planeNormal, candidate.transform.position);

                if (_dragPlane.Raycast(ray, out float enter))
                {
                    _dragOffset = candidate.transform.position - ray.GetPoint(enter);
                }

                _isDragging = true;
                candidate.OnStartDrag();
                UpdateGhostPreview(candidate.transform.position);
                break;
            }
        }
    }

    private void UpdateDrag(Vector2 screenPosition)
    {
        Ray ray = _camera.ScreenPointToRay(screenPosition);
        if (_dragPlane.Raycast(ray, out float enter))
        {
            Vector3 targetPos = ray.GetPoint(enter) + _dragOffset;
            targetPos.z += _currentDraggedObject.DragElevationZ;
            _currentDraggedObject.transform.position = targetPos;

            UpdateGhostPreview(targetPos);
        }
    }

    private void UpdateGhostPreview(Vector3 currentWorldPos)
    {
        if (!_showGhostPreview || _placementGhost == null) return;

        Vector2Int cell = _gridManager.WorldToCellPosition(currentWorldPos, _currentDraggedObject.Size);
        bool canPlace = _gridManager.CanPlaceObject(_currentDraggedObject, cell);

        Vector3 center = _gridManager.CellToWorldPosition(cell, _currentDraggedObject.Size);
        Vector2 cs = _gridManager.GetCellSize();
        Vector2 dim = new Vector2(_currentDraggedObject.Size.x * cs.x, _currentDraggedObject.Size.y * cs.y);

        _placementGhost.UpdatePreview(center, dim, canPlace);
    }

    private void EndDrag()
    {
        Vector2Int cell = _gridManager.WorldToCellPosition(_currentDraggedObject.transform.position, _currentDraggedObject.Size);
        bool success = _gridManager.TryPlaceObject(_currentDraggedObject, cell, animate: true);

        if (!success)
        {
            _gridManager.TryPlaceObject(_currentDraggedObject, _previousValidCell, animate: true);
        }

        _currentDraggedObject.OnEndDrag();

        if (_placementGhost != null)
        {
            _placementGhost.Hide();
        }

        _currentDraggedObject = null;
        _isDragging = false;
    }
}
