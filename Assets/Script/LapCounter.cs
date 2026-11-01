
using UnityEngine;
using TMPro;

public class LapCounter : MonoBehaviour
{
    public TMP_Text lapText;
    public RaceTimer raceTimer;

    public int totalLaps = 3;

    private int currentLap = 1;
    private int nextCheckpoint = 1;
    private bool raceFinished = false;

    void Start()
    {
        UpdateLapText();
    }

    public void ReachCheckpoint(int checkpoint)
    {
        if (raceFinished)
            return;

        if (checkpoint == nextCheckpoint)
        {
            nextCheckpoint++;
            Debug.Log("Checkpoint " + checkpoint + " passed!");
        }
    }

    public void ReachFinishLine()
    {
        if (raceFinished)
            return;

        if (nextCheckpoint != 3)
            return;

        nextCheckpoint = 1;

        if (currentLap >= totalLaps)
        {
            raceFinished = true;

            if (lapText != null)
                lapText.text = "Finished! " + totalLaps + "/" + totalLaps;

            if (raceTimer != null)
                raceTimer.StopTimer();

            Debug.Log("Race Finished!");
        }
        else
        {
            currentLap++;
            UpdateLapText();

            Debug.Log("Lap completed!");
        }
    }

    void UpdateLapText()
    {
        if (lapText != null)
            lapText.text = "Lap " + currentLap + " / " + totalLaps;
    }
}
