using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class LapCounter : MonoBehaviour
{
    public TMP_Text lapText;
    public RaceTimer raceTimer;
    public LapDataSO lapData;

    public int totalLaps = 3;
    public int totalCheckpointIDs = 4; 
    
    private int currentCheckpointID = 0;
    private int currentLap = 0;
    private bool raceFinished = false;
    
    private float lapStartTime;
    private List<float> currentLapTimes = new List<float>();

    void Start()
    {
        if (lapData != null)
        {
            lapData = Instantiate(lapData); // runtime copy so Editor and build behave the same
            LapSaveSystem.Load(lapData);
        }
        
        UpdateLapText();
    }

    public bool ReachCheckpoint(int checkpointID, bool master)
    {
        if (raceFinished) return false;
        if (checkpointID != currentCheckpointID + 1) return false;

        currentCheckpointID++;

        if (master)
        {
            if (currentLap > 0) currentLapTimes.Add(Time.time - lapStartTime);
            
            if (currentLap == totalLaps)
            {
                ReachFinishLine();
                return true;
            }

            currentLap++;
            lapStartTime = Time.time;
            UpdateLapText();
        }

        return true;
    }

    public void ReachFinishLine()
    {
        if (raceFinished) return;
        raceFinished = true;

        if (lapText != null) lapText.text = "Finished! " + totalLaps + "/" + totalLaps;

        if (raceTimer != null) raceTimer.StopTimer();
        
        if (lapData != null)
        {
            lapData.AddResult(currentLapTimes);
            LapSaveSystem.Save(lapData);
        }
        
    }

    private void UpdateLapText()
    {
        if (lapText != null)
            lapText.text = "Lap " + Mathf.Max(1, currentLap) + " / " + totalLaps;
    }
}