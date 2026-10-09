using UnityEngine;

/// <summary>
/// 2D Single-Touch Drag Controller.
/// Handles mobile touch and mouse drag interactions for GridObjects.
/// Ensures only one object can be dragged at a time.
/// Automatically calculates and snaps to the nearest valid cell on finger release.
/// </summary>
public class GridDragController : MonoBehaviour
{
    [Header("Dependencies")]
    [SerializeField] private GridManager _gridManager;
    [SerializeField] private Camera _camera;
    [SerializeField] private LayerMask _dragLayerMask = ~0;

    [Header("Drag Tuning")]
    [SerializeField] private Vector2 _fingerOffset = new Vector2(0f, 0.4f);

    private GridObject _currentDraggedObject;
    private Vector2Int _previousValidCell;
    private Vector2Int _simulatedCell;
    private Vector3 _dragOffset;
    private float _draggedObjectInitialZ;
    private float _minDragWorldX;
    private float _maxDragWorldX;
    private float _minDragWorldY;
    private float _maxDragWorldY;
    private float _fixedWorldX;
    private float _fixedWorldY;
    private int _activeFingerId = -1;
    private bool _isDragging;

    public GridObject CurrentDraggedObject => _currentDraggedObject;
    public bool IsDragging => _isDragging;

    private void Awake()
    {
        if (_gridManager == null)
        {
            _gridManager = GridManager.Instance != null ? GridManager.Instance : FindFirstObjectByType<GridManager>();
        }
        if (_camera == null)
        {
            _camera = Camera.main;
        }
    }

    private void Update()
    {
        if (Input.touchSupported && Input.touchCount > 0)
        {
            HandleTouchInput();
        }
        else
        {
            HandleMouseInput();
        }
    }

    private void HandleTouchInput()
    {
        if (!_isDragging)
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began)
                {
                    if (TryStartDrag(touch.position, touch.fingerId))
                    {
                        break;
                    }
                }
            }
        }
        else
        {
            bool fingerFound = false;
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.fingerId == _activeFingerId)
                {
                    fingerFound = true;
                    if (touch.phase == TouchPhase.Moved || touch.phase == TouchPhase.Stationary)
                    {
                        UpdateDrag(touch.position);
                    }
                    else if (touch.phase == TouchPhase.Ended)
                    {
                        EndDrag();
                    }
                    else if (touch.phase == TouchPhase.Canceled)
                    {
                        CancelDrag();
                    }
                    break;
                }
            }

            if (!fingerFound)
            {
                CancelDrag();
            }
        }
    }

    private void HandleMouseInput()
    {
        if (Input.GetMouseButtonDown(0) && !_isDragging)
        {
            TryStartDrag(Input.mousePosition, -1);
        }
        else if (Input.GetMouseButton(0) && _isDragging)
        {
            UpdateDrag(Input.mousePosition);
        }
        else if (Input.GetMouseButtonUp(0) && _isDragging)
        {
            EndDrag();
        }
    }

    private bool TryStartDrag(Vector2 screenPosition, int fingerId)
    {
        Vector3 worldPoint = _camera.ScreenToWorldPoint(screenPosition);
        Vector2 point2D = new Vector2(worldPoint.x, worldPoint.y);

        Collider2D hitCollider = Physics2D.OverlapPoint(point2D, _dragLayerMask);
        if (hitCollider == null) return false;

        GridObject candidate = hitCollider.GetComponentInParent<GridObject>();
        if (candidate == null || !candidate.IsDraggable) return false;

        GridManager gm = _gridManager != null ? _gridManager : GridManager.Instance;
        Vector2Int currentCell = candidate.GridPosition;
        if (!gm.IsInsideGrid(currentCell) || !gm.CanPlaceObject(candidate, currentCell))
        {
            Vector2Int posCell = gm.WorldToCellPosition(candidate.transform.position);
            currentCell = gm.CanPlaceObject(candidate, posCell) ? posCell : gm.FindNearestValidCell(candidate, posCell);
            gm.TryPlaceObject(candidate, currentCell, animate: false);
        }

        _currentDraggedObject = candidate;
        _previousValidCell = currentCell;
        _simulatedCell = currentCell;
        _draggedObjectInitialZ = candidate.transform.position.z;
        _activeFingerId = fingerId;
        _isDragging = true;

        Vector3 candidatePos = candidate.transform.position;
        _dragOffset = candidatePos - new Vector3(point2D.x, point2D.y, candidatePos.z);

        ComputeReachableBounds(candidate, _previousValidCell);

        candidate.OnStartDrag();
        return true;
    }

    private void ComputeReachableBounds(GridObject obj, Vector2Int startCell)
    {
        GridManager gm = _gridManager != null ? _gridManager : GridManager.Instance;
        switch (obj.AxisConstraint)
        {
            case MoveAxisConstraint.VerticalOnly:
            {
                gm.GetAxisReachableRange(obj, startCell, vertical: true, out int minY, out int maxY);
                float y1 = gm.CellToWorldPosition(new Vector2Int(startCell.x, minY), obj).y;
                float y2 = gm.CellToWorldPosition(new Vector2Int(startCell.x, maxY), obj).y;
                _minDragWorldY = Mathf.Min(y1, y2);
                _maxDragWorldY = Mathf.Max(y1, y2);
                _fixedWorldX = gm.CellToWorldPosition(startCell, obj).x;
                break;
            }
            case MoveAxisConstraint.HorizontalOnly:
            {
                gm.GetAxisReachableRange(obj, startCell, vertical: false, out int minX, out int maxX);
                float x1 = gm.CellToWorldPosition(new Vector2Int(minX, startCell.y), obj).x;
                float x2 = gm.CellToWorldPosition(new Vector2Int(maxX, startCell.y), obj).x;
                _minDragWorldX = Mathf.Min(x1, x2);
                _maxDragWorldX = Mathf.Max(x1, x2);
                _fixedWorldY = gm.CellToWorldPosition(startCell, obj).y;
                break;
            }
            case MoveAxisConstraint.Free2D:
            default:
                break;
        }
    }

    private void UpdateDrag(Vector2 screenPosition)
    {
        Vector3 worldPoint = _camera.ScreenToWorldPoint(screenPosition);
        float rawX = worldPoint.x + _dragOffset.x + _fingerOffset.x;
        float rawY = worldPoint.y + _dragOffset.y + _fingerOffset.y;

        Vector3 targetPos;
        switch (_currentDraggedObject.AxisConstraint)
        {
            case MoveAxisConstraint.VerticalOnly:
            {
                float clampedY = Mathf.Clamp(rawY, _minDragWorldY, _maxDragWorldY);
                targetPos = new Vector3(_fixedWorldX, clampedY, _draggedObjectInitialZ);
                break;
            }
            case MoveAxisConstraint.HorizontalOnly:
            {
                float clampedX = Mathf.Clamp(rawX, _minDragWorldX, _maxDragWorldX);
                targetPos = new Vector3(clampedX, _fixedWorldY, _draggedObjectInitialZ);
                break;
            }
            case MoveAxisConstraint.Free2D:
            default:
            {
                GridManager gm = _gridManager != null ? _gridManager : GridManager.Instance;
                Vector2Int desiredCell = gm.WorldToCellPosition(new Vector3(rawX, rawY, 0));

                int maxSteps = 15;
                while (_simulatedCell != desiredCell && maxSteps-- > 0)
                {
                    int dx = desiredCell.x - _simulatedCell.x;
                    int dy = desiredCell.y - _simulatedCell.y;
                    bool moved = false;

                    if (dx != 0)
                    {
                        Vector2Int stepX = new Vector2Int(_simulatedCell.x + (dx > 0 ? 1 : -1), _simulatedCell.y);
                        if (gm.CanPlaceObject(_currentDraggedObject, stepX))
                        {
                            _simulatedCell = stepX;
                            moved = true;
                        }
                    }

                    if (dy != 0)
                    {
                        Vector2Int stepY = new Vector2Int(_simulatedCell.x, _simulatedCell.y + (dy > 0 ? 1 : -1));
                        if (gm.CanPlaceObject(_currentDraggedObject, stepY))
                        {
                            _simulatedCell = stepY;
                            moved = true;
                        }
                    }

                    if (!moved) break;
                }

                Vector3 simCenter = gm.CellToWorldPosition(_simulatedCell, _currentDraggedObject);
                Vector2 step = gm.GetCellStep();

                bool canLeft = gm.CanPlaceObject(_currentDraggedObject, _simulatedCell + Vector2Int.left);
                bool canRight = gm.CanPlaceObject(_currentDraggedObject, _simulatedCell + Vector2Int.right);
                bool canDown = gm.CanPlaceObject(_currentDraggedObject, _simulatedCell + Vector2Int.down);
                bool canUp = gm.CanPlaceObject(_currentDraggedObject, _simulatedCell + Vector2Int.up);

                float minX = canLeft ? simCenter.x - step.x : simCenter.x;
                float maxX = canRight ? simCenter.x + step.x : simCenter.x;
                float minY = canDown ? simCenter.y - step.y : simCenter.y;
                float maxY = canUp ? simCenter.y + step.y : simCenter.y;

                float clampedX = Mathf.Clamp(rawX, minX, maxX);
                float clampedY = Mathf.Clamp(rawY, minY, maxY);

                targetPos = new Vector3(clampedX, clampedY, _draggedObjectInitialZ);
                break;
            }
        }

        _currentDraggedObject.transform.position = targetPos;
    }

    private void EndDrag()
    {
        Vector2Int targetCell;
        if (_currentDraggedObject.AxisConstraint == MoveAxisConstraint.Free2D)
        {
            targetCell = _simulatedCell;
        }
        else
        {
            Vector2Int rawCell = _gridManager.WorldToCellPosition(_currentDraggedObject.transform.position, _currentDraggedObject);
            targetCell = _gridManager.GetBestReachableCell(_currentDraggedObject, _previousValidCell, rawCell);
        }

        _gridManager.TryPlaceObject(_currentDraggedObject, targetCell, animate: true);

        _currentDraggedObject.OnEndDrag();
        _currentDraggedObject = null;
        _isDragging = false;
        _activeFingerId = -1;
    }

    private void CancelDrag()
    {
        EndDrag();
    }
}
