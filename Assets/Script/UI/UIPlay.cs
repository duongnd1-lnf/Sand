using TMPro;
using UnityEngine;

/// <summary>
/// Gameplay UI that displays current session remaining time and provides visibility controls.
/// </summary>
public class UIPlay : MonoBehaviour
{
    [SerializeField] private TMP_Text _timeText;

    private void Start()
    {
        SessionManager.Instance.OnTimerTick += HandleTimerTick;
    }

    private void OnDestroy()
    {
        SessionManager.Instance.OnTimerTick -= HandleTimerTick;
    }

    public void Show()
    {
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    public void UpdateTimeText(float remainingSeconds)
    {
        int totalSeconds = Mathf.CeilToInt(Mathf.Max(0f, remainingSeconds));
        int minutes = totalSeconds / 60;
        int seconds = totalSeconds % 60;
        _timeText.text = $"{minutes:D2}:{seconds:D2}";
    }

    private void HandleTimerTick(float remainingTime)
    {
        UpdateTimeText(remainingTime);
    }
}
