using System;

/// <summary>
/// Lifecycle state of a gameplay session.
/// </summary>
public enum SessionState
{
    None,
    Playing,
    Won,
    Lost
}

/// <summary>
/// Represents an active play session for a level.
/// </summary>
[Serializable]
public class SessionPlay
{
    public LevelData LevelData { get; private set; }
    public float RemainingTime { get; set; }
    public SessionState State { get; set; }

    public SessionPlay(LevelData levelData)
    {
        LevelData = levelData;
        RemainingTime = levelData.maxTime;
        State = SessionState.Playing;
    }
}
