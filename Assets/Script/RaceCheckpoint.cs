using System.Collections.Generic;
using UnityEngine;

public class RaceCheckpoint : MonoBehaviour
{
    public int checkpointID = 1;
    public bool master = false;

    private static readonly List<RaceCheckpoint> all = new List<RaceCheckpoint>();
    private LapCounter lapCounter;

    private void OnEnable()
    {
        all.Add(this);
        lapCounter = FindAnyObjectByType<LapCounter>();
    }

    private void OnDisable()
    {
        all.Remove(this);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (lapCounter == null) return;

        Rigidbody rb = other.attachedRigidbody;
        if (rb == null || !rb.CompareTag("Player")) return;

        if (!lapCounter.ReachCheckpoint(checkpointID, master)) return;

        // Shift this ID and every branch sharing it into the next lap's range
        int hitID = checkpointID;
        foreach (var checkpoint in all)
        {
            if (checkpoint.checkpointID == hitID)
                checkpoint.checkpointID += lapCounter.totalCheckpointIDs;
        }
    }
}