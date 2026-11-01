
using UnityEngine;

public class RaceCheckpoint : MonoBehaviour
{
    public LapCounter lapCounter;
    public Rigidbody playerCar;

    // 0 = Finish Line
    // 1 = Checkpoint 1
    // 2 = Checkpoint 2
    public int checkpointID;

    void OnTriggerEnter(Collider other)
    {
        if (lapCounter == null || playerCar == null)
            return;

        // Only detect our player's car
        if (other.attachedRigidbody != playerCar)
            return;

        if (checkpointID == 0)
        {
            lapCounter.ReachFinishLine();
        }
        else
        {
            lapCounter.ReachCheckpoint(checkpointID);
        }
    }
}
