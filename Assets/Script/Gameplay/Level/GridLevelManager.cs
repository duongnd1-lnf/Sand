using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages level progression, loading, and switching using Level Prefabs.
/// </summary>
public class GridLevelManager : MonoBehaviour
{
    public static GridLevelManager Instance { get; private set; }

    [SerializeField] private List<GridLevel> _levelPrefabs = new List<GridLevel>();
    [SerializeField] private Transform _levelContainer;
    [SerializeField] private int _startLevelIndex = 0;
    [SerializeField] private bool _loadOnStart = true;

    private GridLevel _currentLevelInstance;
    private int _currentLevelIndex = 0;

    public int CurrentLevelIndex => _currentLevelIndex;
    public int LevelCount => _levelPrefabs.Count;
    public GridLevel CurrentLevel => _currentLevelInstance;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        if (_loadOnStart && _levelPrefabs.Count > 0)
        {
            LoadLevel(_startLevelIndex);
        }
    }

    public void LoadLevel(int index)
    {
        if (index < 0 || index >= _levelPrefabs.Count) return;

        if (_currentLevelInstance != null)
        {
            Destroy(_currentLevelInstance.gameObject);
        }

        _currentLevelIndex = index;
        _currentLevelInstance = Instantiate(_levelPrefabs[_currentLevelIndex], _levelContainer);
        _currentLevelInstance.InitializeLevel();
    }

    public void NextLevel()
    {
        int next = (_currentLevelIndex + 1) % _levelPrefabs.Count;
        LoadLevel(next);
    }

    public void RestartLevel()
    {
        LoadLevel(_currentLevelIndex);
    }
}
