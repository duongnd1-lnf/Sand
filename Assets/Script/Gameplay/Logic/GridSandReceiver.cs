using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Receiver component for sand containers on the grid.
/// Accepts sand from an adjacent GridSandSource.
/// </summary>
public class GridSandReceiver : MonoBehaviour
{
    [Header("Target & Capacity")]
    [SerializeField] private SandColor _targetColor;
    [SerializeField] private int _capacity = 100;
    [SerializeField] private int _currentAmount = 0;

    [SerializeField] private GridObject _gridObject;
    private GridSandSource _connectedSource;
    private readonly List<GridConnectionPoint> _connectionPoints = new();

    public event Action<GridSandSource, IReadOnlyList<GridConnectionPoint>> OnConnected;
    public event Action OnDisconnected;
    public event Action OnFilled;

    public GridObject GridObject { get => _gridObject; set => _gridObject = value; }
    public SandColor TargetColor { get => _targetColor; set => _targetColor = value; }
    public int Capacity { get => _capacity; set => _capacity = value; }
    public int CurrentAmount { get => _currentAmount; set => _currentAmount = value; }
    public float FillPercentage => Mathf.Clamp01((float)_currentAmount / Mathf.Max(1, _capacity));
    public bool IsFull => _currentAmount >= _capacity;
    public bool IsConnected => _connectedSource != null;
    public GridSandSource ConnectedSource => _connectedSource;
    public IReadOnlyList<GridConnectionPoint> ConnectionPoints => _connectionPoints;

    private void Awake()
    {
        if (_gridObject == null) _gridObject = GetComponent<GridObject>();
    }

    private void Reset()
    {
        _gridObject = GetComponent<GridObject>();
    }

    private void OnDestroy()
    {
        SessionManager.Instance?.RemoveReceiver(this);
    }

    private bool _isClearing;

    public bool CanConnect(GridSandSource source)
    {
        return !IsFull && !_isClearing;
    }

    public void Connect(GridSandSource source, List<GridConnectionPoint> connectionPoints)
    {
        if (_connectedSource == source && ArePointsEqual(_connectionPoints, connectionPoints))
            return;

        if (_connectedSource != null && _connectedSource != source)
        {
            _connectedSource.DisconnectReceiver(this);
        }

        _connectedSource = source;
        _connectionPoints.Clear();
        _connectionPoints.AddRange(connectionPoints);

        _connectedSource.ConnectReceiver(this, connectionPoints);
        OnConnected?.Invoke(source, _connectionPoints);
    }

    public void Disconnect()
    {
        if (!IsConnected) return;

        GridSandSource prevSource = _connectedSource;
        _connectedSource = null;
        _connectionPoints.Clear();

        prevSource.DisconnectReceiver(this);
        OnDisconnected?.Invoke();
    }

    private static bool ArePointsEqual(List<GridConnectionPoint> a, List<GridConnectionPoint> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            if (a[i] != b[i]) return false;
        }
        return true;
    }

    public void AddAmount(int amount)
    {
        if (IsFull || _isClearing) return;

        _currentAmount = Mathf.Min(_capacity, _currentAmount + amount);
        if (IsFull)
        {
            OnFilled?.Invoke();
            PlayCompleteAndDestroyAnimation();
        }
    }

    /// <summary>
    /// Animation sequence triggered when receiver is filled.
    /// Removes the object from the grid and destroys the GameObject at the end of the animation.
    /// </summary>
    public void PlayCompleteAndDestroyAnimation()
    {
        if (_isClearing) return;
        _isClearing = true;

        Disconnect();

        if (_gridObject != null && GridManager.Instance != null)
        {
            GridManager.Instance.RemoveObject(_gridObject);
        }

        StartCoroutine(CompleteAnimationRoutine());
    }

    private System.Collections.IEnumerator CompleteAnimationRoutine()
    {
        // Placeholder animation: scales down smoothly. Replace with custom animation when available.
        float duration = 0.35f;
        float elapsed = 0f;
        Vector3 startScale = transform.localScale;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
            yield return null;
        }

        Destroy(gameObject);
    }
}
