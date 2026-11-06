

using UnityEngine;
using UnityEngine.InputSystem;


[RequireComponent(typeof(Rigidbody))]
public class KartController : MonoBehaviour
{
    [Header("Wheel Colliders")]
    public WheelCollider frontLeftWheel;
    public WheelCollider frontRightWheel;
    public WheelCollider rearLeftWheel;
    public WheelCollider rearRightWheel;


    [Header("Wheel Meshes")]
    public Transform frontLeftMesh;
    public Transform frontRightMesh;
    public Transform rearLeftMesh;
    public Transform rearRightMesh;


    [Header("Engine")]
    public float motorTorqueNm = 28000f;
    public float maxSpeedMps = 35f;
    public float reverseSpeedMps = 15f;
    public float throttleResponsePerSec = 2.5f;


    [Header("Braking")]
    public float brakeTorqueNm = 6000f;
    public float idleBrakeTorqueNm = 250f;


    [Header("Steering")]
    public float maxSteerAngleDeg = 28f;
    public float steeringResponseDegPerSec = 160f;


    [Header("Traction")]
    [Range(0.1f, 2f)]
    public float normalTraction = 1f;


    [Range(0.1f, 2f)]
    public float driftTraction = 0.72f;


    [Header("Drifting")]
    public float minDriftSpeedMps = 12f;
    public float driftSteerAngleDeg = 34f;


    [Header("Stability")]
    public float centerOfMassYMeters = -0.35f;


    private Rigidbody rb;


    private float throttle;
    private float steering;
    private float currentSteering;
    private float currentTraction = 1f;


    private float originalLeftGrip;
    private float originalRightGrip;


    private bool gasPressed;
    private bool brakePressed;
    private bool spacePressed;
    private bool isDrifting;


    public bool IsDrifting => isDrifting;


    public float CurrentSpeed =>
        rb != null ? rb.linearVelocity.magnitude : 0f;


    public float ThrottleAmount => throttle;


    void Start()
    {
        rb = GetComponent<Rigidbody>();


        if (frontLeftWheel == null ||
            frontRightWheel == null ||
            rearLeftWheel == null ||
            rearRightWheel == null)
        {
            Debug.LogError("Assign all 4 WheelColliders!");
            enabled = false;
            return;
        }


        rb.centerOfMass =
            new Vector3(0f, centerOfMassYMeters, 0f);


        originalLeftGrip =
            rearLeftWheel.sidewaysFriction.stiffness;


        originalRightGrip =
            rearRightWheel.sidewaysFriction.stiffness;
    }


    void Update()
    {
        if (Keyboard.current == null)
        {
            gasPressed = false;
            brakePressed = false;
            spacePressed = false;
            steering = 0f;
            return;
        }


        gasPressed = Keyboard.current.wKey.isPressed;
        brakePressed = Keyboard.current.sKey.isPressed;
        spacePressed = Keyboard.current.spaceKey.isPressed;


        bool left = Keyboard.current.aKey.isPressed;
        bool right = Keyboard.current.dKey.isPressed;


        steering = left == right ? 0f :
                   left ? -1f : 1f;
    }


    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;


        Vector3 localVelocity =
            transform.InverseTransformDirection(
                rb.linearVelocity
            );


        float forwardSpeed = localVelocity.z;
        float speed = rb.linearVelocity.magnitude;


        HandleDrift(forwardSpeed, dt);
        HandleSteering(speed, dt);
        HandleEngine(forwardSpeed, dt);
    }



    void HandleDrift(float forwardSpeed, float dt)
    {
        // Calculate horizontal speed
        Vector3 flatVelocity = Vector3.ProjectOnPlane(
            rb.linearVelocity,
            Vector3.up
        );

        float speed = flatVelocity.magnitude;

        // Speed needed to start drifting
        bool canStartDrift =
            forwardSpeed >= minDriftSpeedMps &&
            Mathf.Abs(steering) > 0.1f;

        // Once drifting, allow lower speed
        // and countersteering without cancelling drift
        bool canContinueDrift =
            speed >= minDriftSpeedMps * 0.5f;

        if (!spacePressed)
        {
            isDrifting = false;
        }
        else if (isDrifting)
        {
            isDrifting = canContinueDrift;
        }
        else
        {
            isDrifting = canStartDrift;
        }

        // Smoothly change rear tire traction
        float targetTraction =
            isDrifting ? driftTraction : normalTraction;

        currentTraction = Mathf.MoveTowards(
            currentTraction,
            targetTraction,
            3f * dt
        );

        ApplyRearTraction(currentTraction);
    }



    
    void HandleSteering(float speed, float dt)
    {
        float speedRatio =
            Mathf.Clamp01(speed / Mathf.Max(1f, maxSpeedMps));

        // Keep enough steering angle for tight corners
        float steeringMultiplier =
            Mathf.Lerp(1f, 1f, speedRatio);

        float angle = isDrifting
            ? driftSteerAngleDeg
            : maxSteerAngleDeg;

        float targetSteering =
            steering * angle * steeringMultiplier;

        // Smooth steering input at high speeds
        float responseMultiplier =
            Mathf.Lerp(1f, 0.65f, speedRatio);

        float response =
            steeringResponseDegPerSec * responseMultiplier;

        currentSteering = Mathf.MoveTowards(
            currentSteering,
            targetSteering,
            response * dt
        );

        frontLeftWheel.steerAngle = currentSteering;
        frontRightWheel.steerAngle = currentSteering;
    }



    void HandleEngine(float forwardSpeed, float dt)
    {
        float driveDirection = 0f;
        float targetBrake = 0f;


        if (gasPressed && brakePressed)
        {
            targetBrake = brakeTorqueNm;
        }
        else if (gasPressed)
        {
            if (forwardSpeed < -0.5f)
                targetBrake = brakeTorqueNm;
            else
                driveDirection = 1f;
        }
        else if (brakePressed)
        {
            if (forwardSpeed > 0.5f)
                targetBrake = brakeTorqueNm;
            else
                driveDirection = -1f;
        }
        else
        {
            targetBrake = idleBrakeTorqueNm;
        }


        float targetThrottle =
            driveDirection == 0f ? 0f : 1f;


        throttle = Mathf.MoveTowards(
            throttle,
            targetThrottle,
            throttleResponsePerSec * dt
        );


        float limit = driveDirection > 0f
            ? maxSpeedMps : reverseSpeedMps;


        float directionSpeed = driveDirection > 0f
            ? Mathf.Max(0f, forwardSpeed)
            : Mathf.Max(0f, -forwardSpeed);


        float speedRatio =
            Mathf.Clamp01(
                directionSpeed / Mathf.Max(0.1f, limit)
            );


        float powerFactor =
            1f - speedRatio * speedRatio;


        float motor = 0f;


        if (driveDirection != 0f)
        {
            motor = motorTorqueNm *
                    throttle *
                    powerFactor *
                    driveDirection;
        }


        rearLeftWheel.motorTorque = motor;
        rearRightWheel.motorTorque = motor;


        frontLeftWheel.brakeTorque = targetBrake;
        frontRightWheel.brakeTorque = targetBrake;
        rearLeftWheel.brakeTorque = targetBrake;
        rearRightWheel.brakeTorque = targetBrake;
    }


    void ApplyRearTraction(float traction)
    {
        WheelFrictionCurve left =
            rearLeftWheel.sidewaysFriction;


        WheelFrictionCurve right =
            rearRightWheel.sidewaysFriction;


        left.stiffness = originalLeftGrip * traction;
        right.stiffness = originalRightGrip * traction;


        rearLeftWheel.sidewaysFriction = left;
        rearRightWheel.sidewaysFriction = right;
    }


    void LateUpdate()
    {
        UpdateWheel(frontLeftWheel, frontLeftMesh);
        UpdateWheel(frontRightWheel, frontRightMesh);
        UpdateWheel(rearLeftWheel, rearLeftMesh);
        UpdateWheel(rearRightWheel, rearRightMesh);
    }


    void UpdateWheel(WheelCollider wheel, Transform mesh)
    {
        if (wheel == null || mesh == null)
            return;


        wheel.GetWorldPose(
            out Vector3 position,
            out Quaternion rotation
        );


        mesh.position = position;
        mesh.rotation = rotation;
    }


    void OnDisable()
    {
        if (rearLeftWheel != null &&
            originalLeftGrip > 0f)
        {
            WheelFrictionCurve left =
                rearLeftWheel.sidewaysFriction;


            left.stiffness = originalLeftGrip;
            rearLeftWheel.sidewaysFriction = left;
        }


        if (rearRightWheel != null &&
            originalRightGrip > 0f)
        {
            WheelFrictionCurve right =
                rearRightWheel.sidewaysFriction;


            right.stiffness = originalRightGrip;
            rearRightWheel.sidewaysFriction = right;
        }
    }
}





