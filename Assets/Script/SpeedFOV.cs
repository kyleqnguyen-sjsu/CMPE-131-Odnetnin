using UnityEngine;
using Unity.Cinemachine;

public class SpeedFov : MonoBehaviour
{
    public CinemachineCamera cam;
    public KartController kart;

    public float minFov = 60f;
    public float maxFov = 85f;
    
    public float maxFovSpeedMph = 120f;

    public float response = 5f;

    void LateUpdate()
    {
        if (!cam || !kart)
            return;

        float t = Mathf.Clamp01(kart.CurrentSpeed / (maxFovSpeedMph * 0.44704f));
        float target = Mathf.Lerp(minFov, maxFov, t);

        var lens = cam.Lens;
        lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, target, response * Time.deltaTime);
        cam.Lens = lens;
    }
}