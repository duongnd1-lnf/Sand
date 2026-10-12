using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Singleton manager that controls session lifecycle, timer countdown, receiver tracking, and game flow.
/// </summary>
public class SessionManager : MonoBehaviour
{
    public static SessionManager Instance { get; private set; }

    [SerializeField] private List<LevelData> _levels = new List<LevelData>();

    private readonly List<GridSandReceiver> _activeReceivers = new List<GridSandReceiver>();
    private LevelData _currentLevelData;
    private CancellationTokenSource _timerCts;

    public SessionPlay CurrentSession { get; private set; }
    public IReadOnlyList<LevelData> Levels => _levels;
    public LevelData CurrentLevelData => _currentLevelData;
    public IReadOnlyList<GridSandReceiver> ActiveReceivers => _activeReceivers;

    public event Action<SessionPlay> OnSessionEntered;
    public event Action<SessionPlay> OnSessionExited;
    public event Action<SessionPlay> OnSessionRetried;
    public event Action<SessionPlay> OnSessionCompleted;
    public event Action<SessionPlay> OnSessionFailed;
    public event Action<float> OnTimerTick;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        CancelTimer();
    }

    public void Enter(LevelData levelData)
    {
        _currentLevelData = levelData;
        CurrentSession = new SessionPlay(levelData);

        GridLevelManager.Instance.LoadLevel(levelData.id);
        CollectLevelReceivers();

        OnSessionEntered?.Invoke(CurrentSession);
        StartTimer();
    }

    private void Start()
    {
        Enter(0);
    }

    public void Enter(int levelId)
    {
        LevelData data = GetLevelData(levelId);
        Enter(data);
    }

    public void Retry()
    {
        CancelTimer();
        Enter(_currentLevelData);
        OnSessionRetried?.Invoke(CurrentSession);
    }

    public void CompleteLevel()
    {
        CancelTimer();
        CurrentSession.State = SessionState.Won;
        OnSessionCompleted?.Invoke(CurrentSession);
        Exit();
    }

    public void FailLevel()
    {
        CancelTimer();
        CurrentSession.State = SessionState.Lost;
        OnSessionFailed?.Invoke(CurrentSession);
        Exit();
    }

    public void Exit()
    {
        CancelTimer();
        _activeReceivers.Clear();

        SessionPlay exitedSession = CurrentSession;
        CurrentSession = null;
        OnSessionExited?.Invoke(exitedSession);
    }

    public void RemoveReceiver(GridSandReceiver receiver)
    {
        if (CurrentSession == null || CurrentSession.State != SessionState.Playing) return;

        _activeReceivers.Remove(receiver);
        if (_activeReceivers.Count <= 0)
        {
            CompleteLevel();
        }
    }

    public LevelData GetLevelData(int levelId)
    {
        for (int i = 0; i < _levels.Count; i++)
        {
            if (_levels[i].id == levelId)
            {
                return _levels[i];
            }
        }

        return new LevelData(levelId, 0f);
    }

    private void CollectLevelReceivers()
    {
        _activeReceivers.Clear();
        GridLevel currentLevel = GridLevelManager.Instance.CurrentLevel;
        GridSandReceiver[] receivers = currentLevel.GetComponentsInChildren<GridSandReceiver>();
        _activeReceivers.AddRange(receivers);
    }

    private void StartTimer()
    {
        CancelTimer();
        if (CurrentSession.RemainingTime <= 0f) return;

        _timerCts = new CancellationTokenSource();
        RunTimerAsync(_timerCts.Token).Forget();
    }

    private void CancelTimer()
    {
        _timerCts?.Cancel();
        _timerCts?.Dispose();
        _timerCts = null;
    }

    private async UniTaskVoid RunTimerAsync(CancellationToken ct)
    {
        while (CurrentSession.RemainingTime > 0f)
        {
            await UniTask.Yield(PlayerLoopTiming.Update, ct);
            CurrentSession.RemainingTime -= Time.deltaTime;
            OnTimerTick?.Invoke(CurrentSession.RemainingTime);
        }

        CurrentSession.RemainingTime = 0f;
        OnTimerTick?.Invoke(0f);

        if (CurrentSession.State == SessionState.Playing)
        {
            FailLevel();
        }
    }
}
