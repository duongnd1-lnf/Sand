using System;
using UnityEngine;

/// <summary>
/// Handles player interaction (mouse click/drag and touchscreen tap/drag) for GridObjects.
/// Supports 3D Physics.RaycastAll (with 2D Physics fallback) and plane-based dragging.
/// </summary>
public class GridDragController : MonoBehaviour
{
    [Header("Dependencies")]
    [Tooltip("Reference to the GridManager. If null, will automatically find one in the scene.")]
    [SerializeField] private GridManager _gridManager;

    [Tooltip("Camera used for raycasting. Defaults to Camera.main.")]
    [SerializeField] private Camera _camera;

    [Tooltip("Layer mask for selectable GridObjects. Default is Everything.")]
    [SerializeField] private LayerMask _dragLayerMask = ~0;

    [Header("Preview Ghost")]
    [Tooltip("Optional ghost preview. If null, a default one will be created at runtime.")]
    [SerializeField] private GridPlacementGhost _placementGhost;
    [SerializeField] private bool _showGhostPreview = true;

    [Header("Debugging")]
    [Tooltip("Log raycast hits and drag state to Console to diagnose issues.")]
    [SerializeField] private bool _debugLogs = true;

    // Active drag state
    private GridObject _currentDraggedObject;
    private Vector2Int _previousValidCell;
    private Vector3 _dragOffset;
    private Plane _dragPlane;
    private bool _isDragging;

    public GridObject CurrentDraggedObject => _currentDraggedObject;
    public bool IsDragging => _isDragging;

    private void Awake()
    {
        FindDependencies();
        EnsureGhost();
    }

    private void FindDependencies()
    {
        if (_camera == null)
        {
            _camera = Camera.main;
        }

        if (_gridManager == null)
        {
            _gridManager = FindFirstObjectByType<GridManager>();
        }
    }

    private void EnsureGhost()
    {
        if (_showGhostPreview && _placementGhost == null)
        {
            GameObject ghostGo = new GameObject("GridPlacementGhost");
            ghostGo.transform.SetParent(transform);
            _placementGhost = ghostGo.AddComponent<GridPlacementGhost>();
        }
    }

    private void Update()
    {
        // Auto-recover missing references if needed
        if (_camera == null) _camera = Camera.main;
        if (_gridManager == null) _gridManager = FindFirstObjectByType<GridManager>();

        if (_camera == null)
        {
            if (_debugLogs && Time.frameCount % 180 == 0)
            {
                Debug.LogWarning("[GridDragController] Camera is null! Please tag your scene camera as 'MainCamera' or assign it.", this);
            }
            return;
        }

        HandleInput();
    }

    private void HandleInput()
    {
        bool pointerDown = false;
        bool pointerHeld = false;
        bool pointerUp = false;
        Vector2 screenPosition = Vector2.zero;

        // Check Touch input first
        if (Input.touchCount > 0)
        {
            Touch touch = Input.GetTouch(0);
            screenPosition = touch.position;
            pointerDown = touch.phase == TouchPhase.Began;
            pointerHeld = touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary;
            pointerUp = touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled;
        }
        // Fallback to Mouse input (works in Editor and Desktop)
        else
        {
            screenPosition = Input.mousePosition;
            pointerDown = Input.GetMouseButtonDown(0);
            pointerHeld = Input.GetMouseButton(0);
            pointerUp = Input.GetMouseButtonUp(0);
        }

        // --- Interaction Flow ---
        if (pointerDown && !_isDragging)
        {
            TryStartDrag(screenPosition);
        }
        else if (pointerHeld && _isDragging)
        {
            UpdateDrag(screenPosition);
        }
        else if (pointerUp && _isDragging)
        {
            EndDrag();
        }
    }

    private void TryStartDrag(Vector2 screenPosition)
    {
        Ray ray = _camera.ScreenPointToRay(screenPosition);

        GridObject hitObject = null;
        Vector3 hitPoint = Vector3.zero;

        // 1. Primary: 3D RaycastAll to find any GridObject (avoids being blocked by background quad/colliders)
        RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, _dragLayerMask);
        if (hits != null && hits.Length > 0)
        {
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                GridObject candidate = hit.collider.GetComponentInParent<GridObject>();
                if (candidate != null && candidate.IsDraggable)
                {
                    hitObject = candidate;
                    hitPoint = hit.point;
                    break;
                }
            }
        }

        // 2. Fallback: 2D RaycastAll in case the object has a 2D collider
        if (hitObject == null)
        {
            RaycastHit2D[] hits2D = Physics2D.GetRayIntersectionAll(ray, Mathf.Infinity, _dragLayerMask);
            if (hits2D != null && hits2D.Length > 0)
            {
                Array.Sort(hits2D, (a, b) => a.distance.CompareTo(b.distance));
                foreach (RaycastHit2D hit2D in hits2D)
                {
                    GridObject candidate = hit2D.collider.GetComponentInParent<GridObject>();
                    if (candidate != null && candidate.IsDraggable)
                    {
                        hitObject = candidate;
                        hitPoint = hit2D.point;
                        break;
                    }
                }
            }
        }

        // 3. Initiate Drag if valid object found
        if (hitObject != null)
        {
            _currentDraggedObject = hitObject;
            _previousValidCell = hitObject.GridPosition;

            // Normal pointing directly towards the camera so Raycast always hits the front of the plane
            Vector3 planeNormal = -_camera.transform.forward;
            _dragPlane = new Plane(planeNormal, hitObject.transform.position);

            if (_dragPlane.Raycast(ray, out float enter))
            {
                Vector3 planeHit = ray.GetPoint(enter);
                _dragOffset = hitObject.transform.position - planeHit;
            }
            else
            {
                _dragOffset = Vector3.zero;
            }

            _isDragging = true;
            hitObject.OnStartDrag();

            if (_debugLogs)
            {
                Debug.Log($"[GridDragController] Started dragging '{hitObject.name}' at cell {hitObject.GridPosition}.", hitObject);
            }

            UpdateGhostPreview(hitObject.transform.position);
        }
        else if (_debugLogs && hits != null && hits.Length > 0)
        {
            Debug.Log($"[GridDragController] Clicked on '{hits[0].collider.name}', but no GridObject component was found on it or its parents.");
        }
    }

    private void UpdateDrag(Vector2 screenPosition)
    {
        if (_currentDraggedObject == null) return;

        Ray ray = _camera.ScreenPointToRay(screenPosition);
        Vector3 targetPos;

        if (_dragPlane.Raycast(ray, out float enter))
        {
            Vector3 planeHit = ray.GetPoint(enter);
            targetPos = planeHit + _dragOffset;
        }
        else
        {
            // Fallback screen projection
            float zDist = Mathf.Abs(_camera.transform.position.z - _currentDraggedObject.transform.position.z);
            targetPos = _camera.ScreenToWorldPoint(new Vector3(screenPosition.x, screenPosition.y, zDist)) + _dragOffset;
        }

        // Apply 3D lift/elevation along Z-axis while dragging
        targetPos.z += _currentDraggedObject.DragElevationZ;

        _currentDraggedObject.transform.position = targetPos;
        UpdateGhostPreview(targetPos);
    }

    private void UpdateGhostPreview(Vector3 currentWorldPos)
    {
        if (!_showGhostPreview || _placementGhost == null || _currentDraggedObject == null || _gridManager == null) return;

        Vector2Int candidateCell = _gridManager.WorldToCellPosition(currentWorldPos, _currentDraggedObject.Size);
        bool canPlace = _gridManager.CanPlaceObject(_currentDraggedObject, candidateCell);

        Vector3 snapWorldCenter = _gridManager.CellToWorldPosition(candidateCell, _currentDraggedObject.Size);
        Vector2 cellSize = _gridManager.GetCellSize();
        Vector2 dimensions = new Vector2(_currentDraggedObject.Size.x * cellSize.x, _currentDraggedObject.Size.y * cellSize.y);

        _placementGhost.UpdatePreview(snapWorldCenter, dimensions, canPlace);
    }

    private void EndDrag()
    {
        if (_currentDraggedObject == null) return;

        if (_gridManager != null)
        {
            Vector2Int candidateCell = _gridManager.WorldToCellPosition(_currentDraggedObject.transform.position, _currentDraggedObject.Size);
            bool success = _gridManager.TryPlaceObject(_currentDraggedObject, candidateCell, animate: true);

            if (!success)
            {
                if (_debugLogs)
                {
                    Debug.Log($"[GridDragController] Cannot place at cell {candidateCell}. Returning to previous cell {_previousValidCell}.");
                }
                // Placement failed: snap back to the previous valid cell
                _gridManager.TryPlaceObject(_currentDraggedObject, _previousValidCell, animate: true);
            }
            else if (_debugLogs)
            {
                Debug.Log($"[GridDragController] Successfully placed '{_currentDraggedObject.name}' at cell {candidateCell}.");
            }
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
