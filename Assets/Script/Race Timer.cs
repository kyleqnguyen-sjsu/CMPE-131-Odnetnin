
using UnityEngine;
using TMPro;

public class RaceTimer : MonoBehaviour
{
    public TMP_Text timerText;
    public LapCounter lapCounter;
    private double elapsedTime = 0.0;
    private bool isRunning = false;

    void Start()
    {
        lapCounter = FindAnyObjectByType<LapCounter>();
    }
    void Update()
    {
        UpdateTimerText();
        if (lapCounter.currentCheckpointID == 1) isRunning = true;
        if (!isRunning)
            return;

        elapsedTime += Time.deltaTime;
    }

    void UpdateTimerText()
    {
        int totalMilliseconds = (int)(elapsedTime * 1000.0);

        int minutes = totalMilliseconds / 60000;
        int seconds = (totalMilliseconds / 1000) % 60;
        int milliseconds = totalMilliseconds % 1000;

        if (timerText != null)
        {
            timerText.text =
                $"{minutes:00}:{seconds:00}:{milliseconds:000}";
        }
    }

    public void StopTimer()
    {
        isRunning = false;
        UpdateTimerText();
    }

    public void ResetTimer()
    {
        elapsedTime = 0.0;
        isRunning = true;
        UpdateTimerText();
    }
}
