using System;
using UnityEngine;

/// <summary>
/// Receiver component for sand containers on the grid.
/// Accepts sand of a designated target color from an adjacent GridSandSource until capacity is reached.
/// Pure data receiver: holds amount, capacity, and target color without internal sand simulation.
/// </summary>
public class GridSandReceiver : MonoBehaviour
{
    [Header("Target & Capacity")]
    [SerializeField] private SandColor _targetColor;
    [SerializeField] private int _capacity = 100;
    [SerializeField] private int _currentAmount = 0;

    private GridObject _gridObject;
    private GridSandSource _connectedSource;

    public event Action<GridSandSource> OnConnected;
    public event Action OnDisconnected;
    public event Action OnFilled;

    public GridObject GridObject => _gridObject;
    public SandColor TargetColor { get => _targetColor; set => _targetColor = value; }
    public int Capacity { get => _capacity; set => _capacity = value; }
    public int CurrentAmount { get => _currentAmount; set => _currentAmount = value; }
    public float FillPercentage => Mathf.Clamp01((float)_currentAmount / Mathf.Max(1, _capacity));
    public bool IsFull => _currentAmount >= _capacity;
    public bool IsConnected => _connectedSource != null;
    public GridSandSource ConnectedSource => _connectedSource;

    private void Awake()
    {
        _gridObject = GetComponent<GridObject>();

        // Ensure any SandFill child created previously is removed
        Transform sandFill = transform.Find("SandFill");
        if (sandFill != null) Destroy(sandFill.gameObject);
    }

    public bool CanConnect(GridSandSource source)
    {
        if (IsConnected || IsFull || source.IsConnectedTo(this)) return false;
        if (!source.HasContent) return false;
        if (_targetColor != source.CurrentColor) return false;

        return true;
    }

    public void Connect(GridSandSource source)
    {
        if (_connectedSource == source) return;
        _connectedSource = source;
        OnConnected?.Invoke(source);
    }

    public void Disconnect()
    {
        if (!IsConnected) return;
        GridSandSource source = _connectedSource;
        _connectedSource = null;
        if (source.IsConnectedTo(this))
        {
            source.Disconnect(this);
        }
        OnDisconnected?.Invoke();
    }

    public void AddAmount(int amount)
    {
        _currentAmount = Mathf.Min(_capacity, _currentAmount + amount);
        if (IsFull)
        {
            OnFilled?.Invoke();
            Disconnect();
        }
    }
}
