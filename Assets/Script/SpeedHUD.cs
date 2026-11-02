
using UnityEngine;
using TMPro;

public class SpeedHUD : MonoBehaviour
{
    public Rigidbody carRigidbody;
    public TMP_Text speedText;

    void Update()
    {
        if (carRigidbody == null || speedText == null)
            return;

        // Measure horizontal speed
        Vector3 horizontalVelocity =
            Vector3.ProjectOnPlane(
                carRigidbody.linearVelocity,
                Vector3.up
            );

        float speed = horizontalVelocity.magnitude;

        speedText.text = "Speed: " +
                         speed.ToString("F1");
    }
}
