using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class RaceResult
{
    public float totalTime;
    public List<float> lapTimes = new List<float>();
    public string date;
}

[CreateAssetMenu(menuName = "Racing/Lap Data")]
public class LapDataSO : ScriptableObject
{
    public string trackId = "track01"; 

    public float bestLapTime = 0f;                            
    public RaceResult bestRace = new RaceResult();           
    public List<RaceResult> history = new List<RaceResult>(); 

    public const int MaxHistory = 20;

    public bool HasBestRace => bestRace != null && bestRace.totalTime > 0f;

    public void AddResult(List<float> lapTimes)
    {
        var result = new RaceResult
        {
            lapTimes = new List<float>(lapTimes),
            date = DateTime.Now.ToString("yyyy-MM-dd HH:mm")
        };

        foreach (float t in lapTimes)
        {
            result.totalTime += t;
            if (bestLapTime <= 0f || t < bestLapTime) bestLapTime = t;
        }
        
        if (!HasBestRace || result.totalTime < bestRace.totalTime)
        {
            bestRace = new RaceResult
            {
                totalTime = result.totalTime,
                lapTimes = new List<float>(result.lapTimes),
                date = result.date
            };
        }

        history.Add(result);
        if (history.Count > MaxHistory) history.RemoveAt(0);
    }
}