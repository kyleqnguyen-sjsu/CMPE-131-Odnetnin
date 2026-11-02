
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
    public float motorTorque = 2800f;
    public float maxSpeed = 35f;
    public float reverseSpeed = 15f;
    public float throttleResponse = 2.5f;

    [Header("Braking")]
    public float brakeForce = 6000f;
    public float bothKeysBrakeForce = 7000f;
    public float idleBrakeForce = 250f;
    public float brakeResponse = 5f;
    public float stopThreshold = 0.5f;

    [Header("Steering")]
    public float maxSteerAngle = 28f;
    public float steeringResponse = 160f;
    public float highSpeedSteerMultiplier = 0.7f;

    [Header("Drift")]
    public float minimumDriftSpeed = 12f;
    public float driftExitSpeed = 6f;
    public float driftSteerAngle = 34f;
    public float driftBuildSpeed = 0.6f;
    public float counterSteerRecovery = 1.8f;

    [Header("Drift Traction")]
    public float driftGrip = 0.82f;
    public float minimumDriftGrip = 0.68f;
    public float gripChangeSpeed = 2f;
    public float gripRecoverySpeed = 3.5f;

    [Header("Drift Momentum")]
    public float normalDamping = 0.02f;
    public float driftDamping = 0.22f;
    public float dampingResponse = 2f;
    public float driftMotorMultiplier = 0.90f;

    [Header("Stability")]
    public bool useCustomCenterOfMass = true;
    public float centerOfMassOffsetY = -0.35f;

    [Header("Slide Recovery")]
    public bool enableSlideRecovery = true;
    public float slideRecoveryAngle = 35f;
    public float slideRecoveryExitAngle = 15f;
    public float slideRecoveryMinSpeed = 8f;

    // NEW: Arcade Drift 6.0 - Phase 1
    [Header("Arcade Cornering Assist")]
    public bool enableCorneringAssist = true;
    public float assistMinSpeed = 14f;
    public float assistFullSpeed = 25f;
    public float assistAcceleration = 8f;
    public float assistMaxSlipAngle = 50f;
    public float assistResponse = 5f;

    [Header("Debug")]
    public bool showDebug = false;
    public bool showWheelSlipDebug = true;
    public float debugInterval = 0.5f;

    private Rigidbody rb;

    private bool gasPressed;
    private bool brakePressed;
    private bool spacePressed;

    private float steeringInput;
    private float throttleAmount;
    private float currentBrake;
    private float currentSteering;
    private float currentGrip = 1f;

    private bool isDrifting;
    private float driftDirection;
    private float driftTightness;

    private bool recoveringFromSlide;

    // NEW
    private float currentAssist;

    private float originalRearLeftGrip;
    private float originalRearRightGrip;
    private float originalDamping;

    private float debugTimer;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        originalDamping = rb.linearDamping;

        if (useCustomCenterOfMass)
        {
            rb.centerOfMass = new Vector3(
                0f,
                centerOfMassOffsetY,
                0f
            );
        }
        motorTorque = motorTorque * 20f; // Adjust for Unity units (m/s)
        if (frontLeftWheel == null ||
            frontRightWheel == null ||
            rearLeftWheel == null ||
            rearRightWheel == null)
        {
            Debug.LogError(
                "Assign all four WheelColliders!"
            );
            enabled = false;
            return;
        }

        originalRearLeftGrip =
            rearLeftWheel.sidewaysFriction.stiffness;

        originalRearRightGrip =
            rearRightWheel.sidewaysFriction.stiffness;
    }

    void Update()
    {
        if (Keyboard.current == null)
        {
            gasPressed = false;
            brakePressed = false;
            spacePressed = false;
            steeringInput = 0f;
            return;
        }

        gasPressed = Keyboard.current.wKey.isPressed;
        brakePressed = Keyboard.current.sKey.isPressed;
        spacePressed = Keyboard.current.spaceKey.isPressed;

        bool left = Keyboard.current.aKey.isPressed;
        bool right = Keyboard.current.dKey.isPressed;

        steeringInput = 0f;

        if (left && !right)
            steeringInput = -1f;
        else if (right && !left)
            steeringInput = 1f;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        Vector3 localVelocity =
            transform.InverseTransformDirection(
                rb.linearVelocity
            );

        float forwardSpeed = localVelocity.z;

        Vector3 flatVelocity =
            Vector3.ProjectOnPlane(
                rb.linearVelocity,
                Vector3.up
            );

        float speed = flatVelocity.magnitude;

        float slipAngle = Mathf.Atan2(
            localVelocity.x,
            Mathf.Max(
                0.1f,
                Mathf.Abs(forwardSpeed)
            )
        ) * Mathf.Rad2Deg;

        // =========================
        // DRIFT STATE
        // =========================

        bool shouldExitDrift =
            !spacePressed ||
            brakePressed ||
            speed < driftExitSpeed;

        if (isDrifting && shouldExitDrift)
        {
            isDrifting = false;
            driftTightness = 0f;
            driftDirection = 0f;
        }

        if (!isDrifting &&
            spacePressed &&
            !brakePressed &&
            forwardSpeed >= minimumDriftSpeed &&
            Mathf.Abs(steeringInput) > 0.1f)
        {
            isDrifting = true;
            driftDirection = Mathf.Sign(steeringInput);
            driftTightness = 0.15f;
        }

        if (isDrifting)
        {
            if (Mathf.Abs(steeringInput) > 0.1f)
            {
                if (steeringInput == driftDirection)
                {
                    driftTightness +=
                        driftBuildSpeed * dt;
                }
                else
                {
                    driftTightness -=
                        counterSteerRecovery * dt;
                }
            }
            else
            {
                driftTightness -= 0.3f * dt;
            }

            driftTightness =
                Mathf.Clamp01(driftTightness);
        }

        // =========================
        // STEERING
        // =========================

        float speedFactor = Mathf.Clamp01(
            speed / Mathf.Max(1f, maxSpeed)
        );

        float steeringMultiplier = Mathf.Lerp(
            1f,
            highSpeedSteerMultiplier,
            speedFactor
        );

        float steeringLimit = maxSteerAngle;

        if (isDrifting &&
            steeringInput == driftDirection)
        {
            steeringLimit = Mathf.Lerp(
                maxSteerAngle,
                driftSteerAngle,
                driftTightness
            );
        }

        float targetSteering =
            steeringInput *
            steeringLimit *
            steeringMultiplier;

        currentSteering = Mathf.MoveTowards(
            currentSteering,
            targetSteering,
            steeringResponse * dt
        );

        frontLeftWheel.steerAngle = currentSteering;
        frontRightWheel.steerAngle = currentSteering;

        // =========================
        // DRIFT GRIP
        // =========================

        float targetGrip = 1f;

        if (isDrifting)
        {
            targetGrip = Mathf.Lerp(
                driftGrip,
                minimumDriftGrip,
                driftTightness
            );
        }

        float gripSpeed = isDrifting
            ? gripChangeSpeed
            : gripRecoverySpeed;

        currentGrip = Mathf.MoveTowards(
            currentGrip,
            targetGrip,
            gripSpeed * dt
        );

        ApplyRearGrip(currentGrip);

        // =========================
        // SLIDE RECOVERY (5.2)
        // =========================

        bool wantsForward =
            gasPressed && !brakePressed;

        bool wantsReverse =
            brakePressed && !gasPressed;

        float absoluteSlip = Mathf.Abs(slipAngle);

        if (!enableSlideRecovery || !wantsForward)
        {
            recoveringFromSlide = false;
        }
        else if (!recoveringFromSlide)
        {
            if (speed >= slideRecoveryMinSpeed &&
                absoluteSlip >= slideRecoveryAngle)
            {
                recoveringFromSlide = true;
            }
        }
        else
        {
            if (speed < slideRecoveryMinSpeed ||
                (absoluteSlip <= slideRecoveryExitAngle &&
                 forwardSpeed >= 0f))
            {
                recoveringFromSlide = false;
            }
        }

        // =========================
        // ENGINE AND BRAKING
        // =========================

        bool bothKeys = gasPressed && brakePressed;

        bool movingForward =
            forwardSpeed > stopThreshold;

        bool movingBackward =
            forwardSpeed < -stopThreshold;

        float targetThrottle = 0f;
        float targetBrake = 0f;
        float driveDirection = 0f;

        if (bothKeys)
        {
            targetBrake = bothKeysBrakeForce;
        }
        else if (wantsForward)
        {
            if (movingBackward && !recoveringFromSlide)
            {
                targetBrake = brakeForce;
            }
            else
            {
                targetThrottle = 1f;
                driveDirection = 1f;
            }
        }
        else if (wantsReverse)
        {
            if (movingForward)
            {
                targetBrake = brakeForce;
            }
            else
            {
                targetThrottle = 1f;
                driveDirection = -1f;
            }
        }
        else
        {
            targetBrake = idleBrakeForce;
        }

        throttleAmount = Mathf.MoveTowards(
            throttleAmount,
            targetThrottle,
            throttleResponse * dt
        );

        currentBrake = Mathf.MoveTowards(
            currentBrake,
            targetBrake,
            brakeForce * brakeResponse * dt
        );

        float motor = 0f;

        if (driveDirection != 0f &&
            targetBrake == 0f)
        {
            float speedLimit =
                driveDirection > 0f
                    ? maxSpeed
                    : reverseSpeed;

            float directionSpeed =
                driveDirection > 0f
                    ? Mathf.Max(0f, forwardSpeed)
                    : Mathf.Max(0f, -forwardSpeed);

            float powerFactor =
                GetPowerFactor(
                    directionSpeed,
                    speedLimit
                );

            motor =
                motorTorque *
                throttleAmount *
                powerFactor *
                driveDirection;

            if (isDrifting)
                motor *= driftMotorMultiplier;
        }

        if (targetBrake > 0f)
            motor = 0f;

        rearLeftWheel.motorTorque = motor;
        rearRightWheel.motorTorque = motor;

        frontLeftWheel.brakeTorque = currentBrake;
        frontRightWheel.brakeTorque = currentBrake;
        rearLeftWheel.brakeTorque = currentBrake;
        rearRightWheel.brakeTorque = currentBrake;

        // =========================
        // DRIFT MOMENTUM
        // =========================

        float targetDamping = isDrifting
            ? driftDamping
            : normalDamping;

        rb.linearDamping = Mathf.MoveTowards(
            rb.linearDamping,
            targetDamping,
            dampingResponse * dt
        );

        // =========================
        // NEW: ARCADE CORNERING ASSIST
        // =========================

        ApplyArcadeCorneringAssist(
            speed,
            forwardSpeed,
            slipAngle,
            flatVelocity,
            dt
        );

        // =========================
        // DEBUG
        // =========================

        if (showDebug)
        {
            debugTimer += dt;

            if (debugTimer >=
                Mathf.Max(0.1f, debugInterval))
            {
                debugTimer = 0f;

                Debug.Log(
                    "Speed: " + speed.ToString("F1") +
                    " | Forward: " +
                    forwardSpeed.ToString("F1") +
                    " | Slip: " +
                    slipAngle.ToString("F1") +
                    " | Motor: " +
                    motor.ToString("F0") +
                    " | Brake: " +
                    currentBrake.ToString("F0") +
                    " | Throttle: " +
                    throttleAmount.ToString("F2") +
                    " | Recovery: " +
                    recoveringFromSlide +
                    " | Drift: " + isDrifting +
                    " | Steer: " +
                    currentSteering.ToString("F1") +
                    " | Assist: " +
                    currentAssist.ToString("F2") +
                    " | Grounded: " +
                    CountGroundedWheels()
                );

                if (showWheelSlipDebug)
                {
                    DebugWheelSlip(frontLeftWheel, "Front Left");
                    DebugWheelSlip(frontRightWheel, "Front Right");
                    DebugWheelSlip(rearLeftWheel, "Rear Left");
                    DebugWheelSlip(rearRightWheel, "Rear Right");
                }
            }
        }
    }

    // =====================================
    // NEW: ARCADE DRIFT CORNERING ASSIST
    // =====================================

    void ApplyArcadeCorneringAssist(
        float speed,
        float forwardSpeed,
        float slipAngle,
        Vector3 flatVelocity,
        float dt
    )
    {
        float targetAssist = 0f;

        bool sameDriftDirection =
            Mathf.Abs(steeringInput) > 0.1f &&
            steeringInput * driftDirection > 0f;

        bool canAssist =
            enableCorneringAssist &&
            isDrifting &&
            gasPressed &&
            !brakePressed &&
            sameDriftDirection &&
            forwardSpeed > 2f &&
            speed >= assistMinSpeed &&
            CountGroundedWheels() >= 3;

        if (canAssist)
        {
            float speedStrength =
                Mathf.InverseLerp(
                    assistMinSpeed,
                    Mathf.Max(
                        assistMinSpeed + 0.1f,
                        assistFullSpeed
                    ),
                    speed
                );

            // Fade assistance at extreme slip.
            float slipStrength =
                1f - Mathf.InverseLerp(
                    assistMaxSlipAngle * 0.6f,
                    Mathf.Max(
                        assistMaxSlipAngle * 0.6f + 0.1f,
                        assistMaxSlipAngle
                    ),
                    Mathf.Abs(slipAngle)
                );

            targetAssist =
                speedStrength *
                slipStrength;
        }

        currentAssist = Mathf.MoveTowards(
            currentAssist,
            targetAssist,
            Mathf.Max(0f, assistResponse) * dt
        );

        if (!canAssist ||
            currentAssist <= 0f ||
            flatVelocity.sqrMagnitude < 0.01f)
        {
            return;
        }

        // Direction perpendicular to velocity.
        // This bends the path without directly
        // accelerating along the travel direction.

        Vector3 velocityDirection =
            flatVelocity.normalized;

        Vector3 rightOfVelocity =
            Vector3.Cross(
                Vector3.up,
                velocityDirection
            ).normalized;

        Vector3 corneringDirection =
            rightOfVelocity *
            Mathf.Sign(driftDirection);

        float acceleration =
            Mathf.Max(0f, assistAcceleration) *
            currentAssist;

        rb.AddForce(
            corneringDirection * acceleration,
            ForceMode.Acceleration
        );
    }

    // =========================
    // ENGINE POWER CURVE
    // =========================

    float GetPowerFactor(float speed, float limit)
    {
        if (limit <= 0f)
            return 0f;

        float ratio = Mathf.Clamp01(speed / limit);

        return 1f - Mathf.SmoothStep(
            0f,
            1f,
            ratio
        );
    }

    // =========================
    // REAR WHEEL GRIP
    // =========================

    void ApplyRearGrip(float multiplier)
    {
        WheelFrictionCurve left =
            rearLeftWheel.sidewaysFriction;

        WheelFrictionCurve right =
            rearRightWheel.sidewaysFriction;

        left.stiffness =
            originalRearLeftGrip * multiplier;

        right.stiffness =
            originalRearRightGrip * multiplier;

        rearLeftWheel.sidewaysFriction = left;
        rearRightWheel.sidewaysFriction = right;
    }

    // =========================
    // WHEEL SLIP DEBUG
    // =========================

    void DebugWheelSlip(
        WheelCollider wheel,
        string wheelName
    )
    {
        if (wheel == null)
            return;

        if (wheel.GetGroundHit(out WheelHit hit))
        {
            Debug.Log(
                wheelName +
                " | SideSlip: " +
                hit.sidewaysSlip.ToString("F2") +
                " | ForwardSlip: " +
                hit.forwardSlip.ToString("F2")
            );
        }
        else
        {
            Debug.Log(wheelName + " | Not Grounded");
        }
    }

    int CountGroundedWheels()
    {
        int count = 0;

        if (frontLeftWheel.isGrounded) count++;
        if (frontRightWheel.isGrounded) count++;
        if (rearLeftWheel.isGrounded) count++;
        if (rearRightWheel.isGrounded) count++;

        return count;
    }

    // =========================
    // VISUAL WHEELS
    // =========================

    void LateUpdate()
    {
        UpdateWheel(frontLeftWheel, frontLeftMesh);
        UpdateWheel(frontRightWheel, frontRightMesh);
        UpdateWheel(rearLeftWheel, rearLeftMesh);
        UpdateWheel(rearRightWheel, rearRightMesh);
    }

    void UpdateWheel(
        WheelCollider wheel,
        Transform mesh
    )
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
        if (rb != null)
            rb.linearDamping = originalDamping;

        if (rearLeftWheel != null &&
            originalRearLeftGrip > 0f)
        {
            WheelFrictionCurve left =
                rearLeftWheel.sidewaysFriction;

            left.stiffness = originalRearLeftGrip;
            rearLeftWheel.sidewaysFriction = left;
        }

        if (rearRightWheel != null &&
            originalRearRightGrip > 0f)
        {
            WheelFrictionCurve right =
                rearRightWheel.sidewaysFriction;

            right.stiffness = originalRearRightGrip;
            rearRightWheel.sidewaysFriction = right;
        }
    }
}
