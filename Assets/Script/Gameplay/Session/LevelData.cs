using System;

/// <summary>
/// Holds configuration data for a level session.
/// </summary>
[Serializable]
public class LevelData
{
    public int id;
    public float maxTime;

    public LevelData()
    {
    }

    public LevelData(int id, float maxTime)
    {
        this.id = id;
        this.maxTime = maxTime;
    }
}
