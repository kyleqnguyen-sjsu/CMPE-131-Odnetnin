
using UnityEngine;
using UnityEngine.InputSystem;

public class KartCameraController : MonoBehaviour
{
    public Transform car;

    public Vector3 thirdPersonOffset = new Vector3(0f, 4f, -7f);
    public Vector3 firstPersonOffset = new Vector3(0f, 1.5f, 0.5f);

    private bool firstPerson = false;

    void Update()
    {
        if (Keyboard.current == null)
            return;

        if (Keyboard.current.cKey.wasPressedThisFrame)
        {
            firstPerson = !firstPerson;
        }
    }

    void LateUpdate()
    {
        if (car == null)
            return;

        if (firstPerson)
        {
            transform.position = car.TransformPoint(firstPersonOffset);
            transform.rotation = car.rotation;
        }
        else
        {
            transform.position = car.TransformPoint(thirdPersonOffset);
            transform.LookAt(car.position + Vector3.up);
        }
    }
}
