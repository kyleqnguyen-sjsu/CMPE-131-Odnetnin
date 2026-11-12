using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;


// ============================================================
//  PHOTO MODE FREE CAMERA
//
//  Fly the camera anywhere.
//    Hold right mouse button = look around
//    W A S D                 = move
//    Q / E                   = down / up
//    Shift / Ctrl            = faster / slower
//    Mouse wheel             = change move speed
// ============================================================
public class PhotoModeCamera : MonoBehaviour
{
    // ----- Settings (the menu changes these) -----

    // Meters per second
    public float moveSpeed = 8f;

    // Degrees turned per pixel of mouse movement
    public float lookSensitivity = 0.15f;

    // 0 = camera follows instantly, 1 = very floaty
    [Range(0f, 1f)]
    public float smoothing = 0.3f;

    // Tilt of the camera in degrees
    public float roll;

    // ----- Fixed numbers -----

    private const float FastMultiplier = 4f;
    private const float SlowMultiplier = 0.2f;
    private const float MaxPitch = 89f;
    private const float MinMoveSpeed = 0.5f;
    private const float MaxMoveSpeed = 200f;

    // ----- State -----

    private Camera cam;
    private bool looking;

    // Where the camera is heading (the real camera follows these)
    private Vector3 targetPosition;
    private float targetYaw;
    private float targetPitch;

    // Where the camera is now
    private float yaw;
    private float pitch;

    public Camera Camera
    {
        get { return cam; }
    }


    public void Init(Camera camera)
    {
        cam = camera;

        Vector3 angles = transform.eulerAngles;

        yaw = angles.y;
        pitch = NormalizeAngle(angles.x);

        targetYaw = yaw;
        targetPitch = pitch;
        targetPosition = transform.position;
    }

    void Update()
    {
        if (cam == null || Mouse.current == null || Keyboard.current == null)
            return;

        float dt = Time.unscaledDeltaTime;

        HandleLook();
        HandleMove(dt);
        ApplyToTransform(dt);
    }

    void OnDisable()
    {
        SetCursorLocked(false);
    }


    // ----- Public helpers -----

    // Jump straight to a position and direction
    public void SetPose(Vector3 position, Quaternion rotation)
    {
        Vector3 angles = rotation.eulerAngles;

        targetPosition = position;
        targetYaw = angles.y;
        targetPitch = Mathf.Clamp(NormalizeAngle(angles.x), -MaxPitch, MaxPitch);

        yaw = targetYaw;
        pitch = targetPitch;

        transform.position = position;
        transform.rotation = Quaternion.Euler(pitch, yaw, roll);
    }

    // Back up (keeping the current direction) until the box fits on screen
    public void FrameBounds(Bounds bounds)
    {
        float halfFov = cam.fieldOfView * 0.5f * Mathf.Deg2Rad;

        float distance =
            bounds.extents.magnitude / Mathf.Sin(halfFov) * 1.1f;

        Vector3 forward = Quaternion.Euler(targetPitch, targetYaw, 0f) * Vector3.forward;

        targetPosition = bounds.center - forward * distance;
    }

    // Heading in degrees (0 = looking along world +Z)
    public float Yaw
    {
        get { return targetYaw; }
    }


    // ----- Input -----

    void HandleLook()
    {
        Mouse mouse = Mouse.current;

        // Start looking when the right button goes down (not over the menu)
        if (mouse.rightButton.wasPressedThisFrame && !PointerIsOverUi())
            looking = true;

        if (!mouse.rightButton.isPressed)
            looking = false;

        SetCursorLocked(looking);

        if (!looking)
            return;

        Vector2 delta = mouse.delta.ReadValue();

        targetYaw += delta.x * lookSensitivity;
        targetPitch -= delta.y * lookSensitivity;

        targetPitch = Mathf.Clamp(targetPitch, -MaxPitch, MaxPitch);
    }

    void HandleMove(float dt)
    {
        Keyboard keyboard = Keyboard.current;

        // Mouse wheel changes the speed
        float scroll = Mouse.current.scroll.ReadValue().y;

        if (scroll != 0f && !PointerIsOverUi())
        {
            float factor = 1f + Mathf.Sign(scroll) * 0.15f;

            moveSpeed = Mathf.Clamp(
                moveSpeed * factor,
                MinMoveSpeed,
                MaxMoveSpeed
            );
        }

        Quaternion look = Quaternion.Euler(targetPitch, targetYaw, 0f);

        Vector3 direction = Vector3.zero;

        if (keyboard.wKey.isPressed) direction += look * Vector3.forward;
        if (keyboard.sKey.isPressed) direction -= look * Vector3.forward;
        if (keyboard.dKey.isPressed) direction += look * Vector3.right;
        if (keyboard.aKey.isPressed) direction -= look * Vector3.right;
        if (keyboard.eKey.isPressed) direction += Vector3.up;
        if (keyboard.qKey.isPressed) direction -= Vector3.up;

        float speed = moveSpeed;

        if (keyboard.leftShiftKey.isPressed)
            speed *= FastMultiplier;

        if (keyboard.leftCtrlKey.isPressed)
            speed *= SlowMultiplier;

        if (direction.sqrMagnitude > 1f)
            direction.Normalize();

        targetPosition += direction * speed * dt;
    }

    // Move the real camera toward where it is heading
    void ApplyToTransform(float dt)
    {
        // Higher = snappier. Smoothing 0 is close to instant.
        float followSpeed = Mathf.Lerp(60f, 4f, smoothing);
        float t = 1f - Mathf.Exp(-followSpeed * dt);

        yaw = Mathf.Lerp(yaw, targetYaw, t);
        pitch = Mathf.Lerp(pitch, targetPitch, t);

        transform.position = Vector3.Lerp(transform.position, targetPosition, t);
        transform.rotation = Quaternion.Euler(pitch, yaw, roll);
    }


    // ----- Small helpers -----

    bool PointerIsOverUi()
    {
        return EventSystem.current != null &&
               EventSystem.current.IsPointerOverGameObject();
    }

    // Hide and lock the mouse while looking around
    void SetCursorLocked(bool locked)
    {
        CursorLockMode wanted = locked ? CursorLockMode.Locked : CursorLockMode.None;

        if (Cursor.lockState != wanted)
            Cursor.lockState = wanted;

        if (Cursor.visible == locked)
            Cursor.visible = !locked;
    }

    // Turn 0..360 into -180..180
    static float NormalizeAngle(float angle)
    {
        while (angle > 180f) angle -= 360f;
        while (angle < -180f) angle += 360f;

        return angle;
    }
}
