using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;


// ============================================================
//  PHOTO MODE CAR
//
//  Puts a copy of the car prefab in the scene. It cannot be
//  driven: the kart scripts are switched off and the body is
//  frozen.
//
//  STICK TO SURFACE (on by default)
//  The car is lowered onto the ground under its wheels and
//  tilted to match the slope, including banked corners.
//  Heading (yaw) turns the car around the surface. Pitch and
//  roll are extra tilt on top of the surface angle.
//
//  KEYS
//    Arrow keys      = move (relative to the camera)
//    Page Up / Down  = up / down (turns stick to surface off)
//    [ and ]         = rotate
//    Shift / Ctrl    = faster / slower
// ============================================================
public class PhotoModeCar : MonoBehaviour
{
    // ----- Settings (the menu changes these) -----

    // Let the arrow keys move the car
    public bool keyboardControl = true;

    // Meters moved by one press of the "+" or "-" menu button
    public float stepMeters = 0.5f;

    // ----- Fixed numbers -----

    private const float KeyMoveSpeed = 2f;
    private const float KeyRotateSpeed = 60f;
    private const float FastMultiplier = 4f;
    private const float SlowMultiplier = 0.2f;

    // Which way the car faces when first placed, compared to the camera
    private const float StartYawOffset = 145f;

    // How far ahead of the camera the car is placed
    private const float PlaceDistance = 8f;

    // Surfaces steeper than this (degrees) count as walls: ignore the tilt
    private const float MaxSurfaceSlopeDeg = 60f;

    // How far down to look for the ground
    private const float GroundSearchDistance = 1000f;

    // ----- State -----

    private GameObject prefab;
    private PhotoModeCamera cameraRig;
    private Material[] paintMaterials = new Material[0];
    private int paintIndex;

    // Front wheel steering (degrees). Positive = right.
    private float steerAngle;
    private Transform[] frontWheels = new Transform[0];
    private Quaternion[] frontWheelStartRotation = new Quaternion[0];
    private GameObject instance;

    private bool stickToGround = true;

    // The car's origin, and its direction
    private Vector3 position;
    private float yaw;
    private float pitch;
    private float roll;

    // Extra height above the ground, in meters (fine tuning)
    private float rideHeight;

    // Measured once when the car spawns (in local car space, scaled):
    // where each wheel touches down, and the height of its lowest point
    private Vector2[] footprint = new Vector2[0];
    private float[] footprintBottom = new float[0];

    // Tells the menu that something changed
    public event Action Changed;


    // ============================================================
    //  PROPERTIES
    // ============================================================

    public bool Exists
    {
        get { return instance != null; }
    }

    public bool Visible
    {
        get { return instance != null && instance.activeSelf; }
        set
        {
            if (instance != null)
                instance.SetActive(value);

            RaiseChanged();
        }
    }

    // On = the car sits on the surface below it and follows its angle
    public bool StickToGround
    {
        get { return stickToGround; }
        set
        {
            stickToGround = value;
            Apply();
        }
    }

    public Vector3 Position
    {
        get { return position; }
        set
        {
            position = value;
            Apply();
        }
    }

    // Heading in degrees, kept between 0 and 360
    public float Yaw
    {
        get { return yaw; }
        set
        {
            yaw = Mathf.Repeat(value, 360f);
            Apply();
        }
    }

    // Extra tilt on top of the surface angle
    public float Pitch
    {
        get { return pitch; }
        set
        {
            pitch = value;
            Apply();
        }
    }

    public float Roll
    {
        get { return roll; }
        set
        {
            roll = value;
            Apply();
        }
    }

    // Lifts the car off the ground (or sinks it) by this many meters
    public float RideHeight
    {
        get { return rideHeight; }
        set
        {
            rideHeight = value;
            Apply();
        }
    }

    // Middle of the car, handy for aiming the camera
    public Vector3 Center
    {
        get { return GetBounds().center; }
    }


    public void Init(GameObject carPrefab, PhotoModeCamera rig, Material[] paints)
    {
        prefab = carPrefab;
        cameraRig = rig;
        paintMaterials = paints != null ? paints : new Material[0];
    }

    // ----- Paint -----

    public int PaintCount
    {
        get { return paintMaterials.Length; }
    }

    public int PaintIndex
    {
        get { return paintIndex; }
    }

    // Swap the body paint for one of the materials already in the project
    public void SetPaint(int index)
    {
        if (paintMaterials.Length == 0)
            return;

        paintIndex = Mathf.Clamp(index, 0, paintMaterials.Length - 1);

        if (instance != null)
            ApplyPaint();
    }

    void ApplyPaint()
    {
        Material paint = paintMaterials[paintIndex];

        if (paint == null)
            return;

        foreach (Renderer part in instance.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = part.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < materials.Length; i++)
            {
                // Only the body paint materials are replaced (not tires, glass...)
                if (materials[i] != null && materials[i].name.StartsWith("AFRC_Mat_Col"))
                {
                    materials[i] = paint;
                    changed = true;
                }
            }

            if (changed)
                part.sharedMaterials = materials;
        }
    }

    // ----- Steering -----

    public float SteerAngle
    {
        get { return steerAngle; }
        set
        {
            steerAngle = Mathf.Clamp(value, -45f, 45f);
            ApplySteering();
        }
    }

    // Turns the front wheels around the car's up direction
    void ApplySteering()
    {
        if (instance == null)
            return;

        Transform car = instance.transform;

        for (int i = 0; i < frontWheels.Length; i++)
        {
            if (frontWheels[i] == null)
                continue;

            frontWheels[i].rotation = car.rotation
                * Quaternion.AngleAxis(steerAngle, Vector3.up)
                * frontWheelStartRotation[i];
        }
    }

    // Remember how the front wheels sit when the car is straight
    void FindFrontWheels()
    {
        KartController kart = instance.GetComponent<KartController>();

        if (kart == null)
            return;

        frontWheels = new Transform[] { kart.frontLeftMesh, kart.frontRightMesh };
        frontWheelStartRotation = new Quaternion[frontWheels.Length];

        for (int i = 0; i < frontWheels.Length; i++)
        {
            if (frontWheels[i] != null)
                frontWheelStartRotation[i] = Quaternion.Inverse(instance.transform.rotation) * frontWheels[i].rotation;
        }
    }


    // ============================================================
    //  SPAWN AND REMOVE
    // ============================================================

    public void Spawn()
    {
        if (instance != null)
            return;

        if (prefab == null)
        {
            Debug.LogWarning("Photo Mode: no car prefab is assigned.");
            return;
        }

        // Spawn upright at the origin so the measuring below is exact
        instance = Instantiate(prefab, Vector3.zero, Quaternion.identity);
        instance.name = "Photo Mode Car";

        // Keep it in the photo mode scene, so it survives when
        // the background scene is swapped
        SceneManager.MoveGameObjectToScene(instance, gameObject.scene);

        MakeUndrivable(instance);
        MeasureCar();
        FindFrontWheels();

        if (paintMaterials.Length > 0)
            ApplyPaint();

        PlaceInFrontOfCamera();
    }

    public void Remove()
    {
        if (instance != null)
            Destroy(instance);

        instance = null;

        RaiseChanged();
    }

    // Switch off the driving scripts and freeze the body
    void MakeUndrivable(GameObject car)
    {
        MonoBehaviour[] scripts = car.GetComponentsInChildren<MonoBehaviour>(true);

        foreach (MonoBehaviour script in scripts)
        {
            if (script == null)
                continue;

            // Only our own game scripts (not Unity ones)
            if (script.GetType().Assembly.GetName().Name == "Assembly-CSharp")
                script.enabled = false;
        }

        Rigidbody body = car.GetComponent<Rigidbody>();

        if (body != null)
        {
            body.isKinematic = true;
            body.useGravity = false;
        }
    }

    // Find where the car touches the ground. This uses the four wheel
    // models (the tires are what touch the road). If they are not set up,
    // it falls back to the lowest point of the visible car.
    void MeasureCar()
    {
        float scale = instance.transform.lossyScale.y;

        List<Vector2> spots = new List<Vector2>();
        List<float> bottoms = new List<float>();

        KartController kart = instance.GetComponent<KartController>();

        if (kart != null)
        {
            Transform[] wheelMeshes = new Transform[]
            {
                kart.frontLeftMesh,
                kart.frontRightMesh,
                kart.rearLeftMesh,
                kart.rearRightMesh
            };

            foreach (Transform wheelMesh in wheelMeshes)
            {
                Bounds wheelBounds;

                if (wheelMesh != null && TryGetLocalBounds(wheelMesh, out wheelBounds))
                {
                    spots.Add(new Vector2(wheelBounds.center.x * scale, wheelBounds.center.z * scale));
                    bottoms.Add(wheelBounds.min.y * scale);
                }
            }
        }

        if (spots.Count < 3)
        {
            spots.Clear();
            bottoms.Clear();

            Bounds body;

            if (TryGetLocalBounds(instance.transform, out body))
            {
                float bottom = body.min.y * scale;

                spots.Add(new Vector2(body.min.x * scale, body.min.z * scale));
                spots.Add(new Vector2(body.max.x * scale, body.min.z * scale));
                spots.Add(new Vector2(body.min.x * scale, body.max.z * scale));
                spots.Add(new Vector2(body.max.x * scale, body.max.z * scale));

                for (int i = 0; i < 4; i++)
                    bottoms.Add(bottom);
            }
            else
            {
                spots.Add(Vector2.zero);
                bottoms.Add(0f);
            }
        }

        footprint = spots.ToArray();
        footprintBottom = bottoms.ToArray();
    }

    // Box around the visible parts under "root", in the car's own space.
    // Hidden parts, particles and trails are ignored.
    bool TryGetLocalBounds(Transform root, out Bounds result)
    {
        Transform car = instance.transform;

        bool found = false;
        result = new Bounds();

        foreach (Renderer part in root.GetComponentsInChildren<Renderer>())
        {
            if (!part.enabled || !part.gameObject.activeInHierarchy)
                continue;

            if (part is ParticleSystemRenderer || part is TrailRenderer || part is LineRenderer)
                continue;

            foreach (Vector3 corner in GetCorners(part.bounds))
            {
                Vector3 local = car.InverseTransformPoint(corner);

                if (!found)
                {
                    result = new Bounds(local, Vector3.zero);
                    found = true;
                }
                else
                {
                    result.Encapsulate(local);
                }
            }
        }

        return found;
    }

    Vector3[] GetCorners(Bounds bounds)
    {
        Vector3 c = bounds.center;
        Vector3 e = bounds.extents;

        return new Vector3[]
        {
            c + new Vector3(-e.x, -e.y, -e.z),
            c + new Vector3( e.x, -e.y, -e.z),
            c + new Vector3(-e.x,  e.y, -e.z),
            c + new Vector3( e.x,  e.y, -e.z),
            c + new Vector3(-e.x, -e.y,  e.z),
            c + new Vector3( e.x, -e.y,  e.z),
            c + new Vector3(-e.x,  e.y,  e.z),
            c + new Vector3( e.x,  e.y,  e.z)
        };
    }


    // ============================================================
    //  PLACING
    // ============================================================

    // Put the car on the ground ahead of the camera, facing it
    public void PlaceInFrontOfCamera()
    {
        if (instance == null || cameraRig == null)
            return;

        Transform view = cameraRig.transform;

        Vector3 flatForward = Vector3.ProjectOnPlane(view.forward, Vector3.up);

        if (flatForward.sqrMagnitude < 0.001f)
            flatForward = Vector3.forward;

        position = view.position + flatForward.normalized * PlaceDistance;

        yaw = Mathf.Repeat(cameraRig.Yaw + StartYawOffset, 360f);
        pitch = 0f;
        roll = 0f;
        stickToGround = true;

        // Look for the ground from the camera's height downward
        SnapToSurface(view.position.y + 1f);

        Physics.SyncTransforms();
        RaiseChanged();
    }

    // Put the car where the middle of the screen points at
    public void PlaceAtCrosshair()
    {
        if (instance == null || cameraRig == null)
            return;

        Transform view = cameraRig.transform;

        RaycastHit hit;

        if (RaycastIgnoringCar(new Ray(view.position, view.forward), 1000f, out hit))
        {
            position = hit.point;
            stickToGround = true;

            Apply();
        }
    }

    // Turn "stick to surface" on and drop the car onto the ground
    public void SnapToGround()
    {
        StickToGround = true;
    }

    public void ResetRotation()
    {
        pitch = 0f;
        roll = 0f;

        Apply();
    }

    // Move in world space
    public void Nudge(Vector3 worldDelta)
    {
        if (instance == null)
            return;

        position += worldDelta;

        // Moving up or down by hand turns "stick to surface" off
        if (Mathf.Abs(worldDelta.y) > 0f)
            stickToGround = false;

        Apply();
    }


    // ============================================================
    //  KEYBOARD CONTROL
    // ============================================================

    void Update()
    {
        if (instance == null || !instance.activeSelf)
            return;

        if (!keyboardControl || Keyboard.current == null || cameraRig == null)
            return;

        Keyboard keyboard = Keyboard.current;
        float dt = Time.unscaledDeltaTime;

        // Directions on the ground, based on where the camera looks
        Vector3 forward = Vector3.ProjectOnPlane(cameraRig.transform.forward, Vector3.up);

        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.forward;

        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward);

        Vector3 move = Vector3.zero;

        if (keyboard.upArrowKey.isPressed) move += forward;
        if (keyboard.downArrowKey.isPressed) move -= forward;
        if (keyboard.rightArrowKey.isPressed) move += right;
        if (keyboard.leftArrowKey.isPressed) move -= right;

        float up = 0f;

        if (keyboard.pageUpKey.isPressed) up += 1f;
        if (keyboard.pageDownKey.isPressed) up -= 1f;

        float turn = 0f;

        if (keyboard.rightBracketKey.isPressed) turn += 1f;
        if (keyboard.leftBracketKey.isPressed) turn -= 1f;

        float speedScale = 1f;

        if (keyboard.leftShiftKey.isPressed)
            speedScale *= FastMultiplier;

        if (keyboard.leftCtrlKey.isPressed)
            speedScale *= SlowMultiplier;

        if (move != Vector3.zero)
            Nudge(move.normalized * KeyMoveSpeed * speedScale * dt);

        if (up != 0f)
            Nudge(Vector3.up * up * KeyMoveSpeed * speedScale * dt);

        if (turn != 0f)
        {
            yaw = Mathf.Repeat(yaw + turn * KeyRotateSpeed * speedScale * dt, 360f);
            Apply();
        }
    }


    // ============================================================
    //  APPLYING THE POSITION AND ROTATION
    // ============================================================

    // Send the state to the real object
    void Apply()
    {
        if (instance == null)
            return;

        if (stickToGround)
        {
            // Look for the ground from just above the car
            float topY = position.y + GetBounds().size.y + 1f;

            SnapToSurface(topY);
        }
        else
        {
            instance.transform.SetPositionAndRotation(
                position,
                Quaternion.Euler(pitch, yaw, roll)
            );
        }

        ApplySteering();

        Physics.SyncTransforms();

        RaiseChanged();
    }

    // Rests the car on the ground under it.
    //  1. Shoot a ray down at every wheel to find the ground there
    //  2. Fit a flat plane through those points (the slope or banking)
    //  3. Tilt the car to match the plane
    //  4. Lower the car until its lowest wheel touches the plane
    // "searchFromY" is the highest height the ground can be at.
    void SnapToSurface(float searchFromY)
    {
        Quaternion heading = Quaternion.Euler(0f, yaw, 0f);

        // 1. Find the ground under each wheel
        List<Vector3> groundPoints = new List<Vector3>();

        foreach (Vector2 spot in footprint)
        {
            Vector3 offset = heading * new Vector3(spot.x, 0f, spot.y);

            Vector3 origin = new Vector3(
                position.x + offset.x,
                searchFromY,
                position.z + offset.z
            );

            RaycastHit hit;

            if (RaycastIgnoringCar(new Ray(origin, Vector3.down), GroundSearchDistance, out hit))
                groundPoints.Add(hit.point);
        }

        // No ground found: just place it freely
        if (groundPoints.Count == 0)
        {
            instance.transform.SetPositionAndRotation(
                position,
                Quaternion.Euler(pitch, yaw, roll)
            );

            return;
        }

        // 2. The ground plane:  height = slopeX * x + slopeZ * z + offset
        float slopeX = 0f;
        float slopeZ = 0f;
        float offsetY = 0f;

        bool fitted =
            groundPoints.Count >= 3 &&
            FitPlane(groundPoints, out slopeX, out slopeZ, out offsetY);

        Vector3 normal = Vector3.up;

        if (fitted)
        {
            normal = new Vector3(-slopeX, 1f, -slopeZ).normalized;

            // A wall is not a road: ignore very steep tilts
            if (Vector3.Angle(normal, Vector3.up) > MaxSurfaceSlopeDeg)
                fitted = false;
        }

        if (!fitted)
        {
            // Flat ground at the average height
            normal = Vector3.up;
            slopeX = 0f;
            slopeZ = 0f;
            offsetY = Average(groundPoints);
        }

        // 3. Turn the car: heading along the surface, then the extra tilt
        Vector3 forward = Vector3.ProjectOnPlane(heading * Vector3.forward, normal).normalized;

        Quaternion onSurface = Quaternion.LookRotation(forward, normal);
        Quaternion rotation = onSurface * Quaternion.Euler(pitch, 0f, roll);

        // 4. Lower or raise the car until its lowest wheel just touches
        float lift = float.MinValue;

        for (int i = 0; i < footprint.Length; i++)
        {
            Vector3 wheelBottom = position + rotation * new Vector3(footprint[i].x, footprintBottom[i], footprint[i].y);

            float groundHeight = slopeX * wheelBottom.x + slopeZ * wheelBottom.z + offsetY;

            lift = Mathf.Max(lift, groundHeight - wheelBottom.y);
        }

        position.y += lift + rideHeight;

        instance.transform.SetPositionAndRotation(position, rotation);
    }

    // Best flat plane through the points (least squares).
    // Gives:  height = slopeX * x + slopeZ * z + offset
    bool FitPlane(List<Vector3> points, out float slopeX, out float slopeZ, out float offset)
    {
        slopeX = 0f;
        slopeZ = 0f;
        offset = 0f;

        float meanX = 0f;
        float meanY = 0f;
        float meanZ = 0f;

        foreach (Vector3 p in points)
        {
            meanX += p.x;
            meanY += p.y;
            meanZ += p.z;
        }

        meanX /= points.Count;
        meanY /= points.Count;
        meanZ /= points.Count;

        float sumXX = 0f;
        float sumZZ = 0f;
        float sumXZ = 0f;
        float sumXY = 0f;
        float sumZY = 0f;

        foreach (Vector3 p in points)
        {
            float dx = p.x - meanX;
            float dy = p.y - meanY;
            float dz = p.z - meanZ;

            sumXX += dx * dx;
            sumZZ += dz * dz;
            sumXZ += dx * dz;
            sumXY += dx * dy;
            sumZY += dz * dy;
        }

        float determinant = sumXX * sumZZ - sumXZ * sumXZ;

        // Points in a straight line: no single plane fits
        if (Mathf.Abs(determinant) < 0.0001f)
            return false;

        slopeX = (sumXY * sumZZ - sumZY * sumXZ) / determinant;
        slopeZ = (sumZY * sumXX - sumXY * sumXZ) / determinant;
        offset = meanY - slopeX * meanX - slopeZ * meanZ;

        return true;
    }

    float Average(List<Vector3> points)
    {
        float total = 0f;

        foreach (Vector3 p in points)
            total += p.y;

        return total / points.Count;
    }


    // ============================================================
    //  HELPERS
    // ============================================================

    void RaiseChanged()
    {
        if (Changed != null)
            Changed();
    }

    // Box around everything drawn for the car
    public Bounds GetBounds()
    {
        if (instance == null)
            return new Bounds(position, Vector3.one);

        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();

        if (renderers.Length == 0)
            return new Bounds(position, Vector3.one);

        Bounds bounds = renderers[0].bounds;

        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);

        return bounds;
    }

    // Nearest thing hit by the ray that is not part of the car
    bool RaycastIgnoringCar(Ray ray, float distance, out RaycastHit result)
    {
        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            distance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit hit in hits)
        {
            bool isCar = instance != null && hit.transform.IsChildOf(instance.transform);

            if (!isCar)
            {
                result = hit;
                return true;
            }
        }

        result = new RaycastHit();
        return false;
    }
}
