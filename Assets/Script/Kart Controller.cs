using UnityEngine;
using UnityEngine.InputSystem;


// ============================================================
//  KART CONTROLLER
//
//  Drives the kart using real car units (horsepower, mph,
//  g-force, degrees). The starting numbers are modeled on the
//  Dodge Challenger SRT Hellcat.
//
//  KEYS
//    W = gas     S = brake / reverse     A, D = steer     Space = drift
//
//  HOW IT WORKS
//  Every physics step, the script does these jobs in order:
//    1. Drift      - decide if the kart is drifting
//    2. Steering   - turn the front wheels
//    3. Pedals     - engine power and brakes
//    4. Stability  - stop the kart from spinning out
//  Driving assists (traction control, ABS and more) help with
//  jobs 2, 3 and 4. They are in the DRIVING ASSISTS section.
//
//  QUICK TUNING (all the settings are right below)
//    Too fast?                 lower topSpeedMph
//    Too slow off the line?    raise enginePowerHp
//    Brakes too weak?          raise brakingDecelG
//    Hard to turn?             raise maxSteerAngleDeg or tireGripG
//    Slides too easily?        raise tireGripG or driftGripG
//
//  SETUP
//  Set the Rigidbody mass to the car's weight in kg (the Hellcat
//  is about 2017 kg). Power and braking depend on it, and the
//  suspension adjusts itself to match.
// ============================================================
[RequireComponent(typeof(Rigidbody))]
public class KartController : MonoBehaviour
{
    // ========================================================
    //  WHEELS  (drag these in on the Inspector)
    // ========================================================

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


    // ========================================================
    //  SETTINGS  (change these to tune the kart)
    // ========================================================

    [Header("Engine")]

    // Engine power in horsepower. Higher = faster acceleration.
    // Hellcat: 707
    public float enginePowerHp = 707f;

    // Fastest the kart can go forward, in mph.
    // Hellcat: about 199
    public float topSpeedMph = 199f;

    // Fastest the kart can go in reverse, in mph.
    public float reverseSpeedMph = 30f;

    // Seconds for the gas to go from nothing to full.
    // Lower = more sudden.
    public float throttleResponseSeconds = 0.4f;


    [Header("Braking")]

    // Brake strength in g. Higher = stops sooner.
    // 1 g stops from 60 mph in about 120 feet.
    public float brakingDecelG = 1f;

    // How much the kart slows down by itself with no pedal pressed,
    // in g. Higher = slows down faster when you let go of the gas.
    public float coastDecelG = 0.5f;


    [Header("Steering")]

    // How far the front wheels can turn, in degrees.
    // At higher speeds the limit shrinks on its own.
    // Hellcat: about 27
    public float maxSteerAngleDeg = 27f;

    // How fast the wheels turn, in degrees per second.
    // Higher = quicker steering.
    public float steeringResponseDegPerSec = 160f;


    [Header("Traction")]

    // Tire grip in g, which is how hard the kart can corner.
    // Higher = more grip.
    // Hellcat: about 1
    [Range(0.1f, 2f)]
    public float tireGripG = 1f;

    // Rear tire grip while drifting, in g.
    // Lower = slides more.
    [Range(0.1f, 2f)]
    public float driftGripG = 0.72f;


    [Header("Drifting")]

    // Slowest speed a drift can start at, in mph.
    // To drift: hold Space while steering.
    public float minDriftSpeedMph = 27f;

    // How far the front wheels can turn during a drift, in degrees.
    public float driftSteerAngleDeg = 34f;


    [Header("Stability")]

    // Height of the kart's center of mass, in meters.
    // Lower = more stable.
    public float centerOfMassYMeters = -0.35f;


    // ========================================================
    //  FIXED NUMBERS  (fine to leave alone)
    // ========================================================

    // ----- Unit conversions -----

    private const float MphToMps = 0.44704f;
    private const float HpToWatts = 745.7f;
    private const float Gravity = 9.81f;

    // ----- The car -----

    // 4 wheels, and the 2 rear ones are driven by the engine
    private const float WheelCount = 4f;
    private const float DrivenWheelCount = 2f;

    // ----- Engine and pedals -----

    // Share of 1 g the rear tires can use to launch the car.
    // Not all of the weight is on the rear tires, so this is below 1.
    private const float LaunchTraction = 0.7f;

    // Below this speed (m/s) the car counts as stopped.
    // Used to tell "rolling backwards" from "stopped" for the pedals.
    private const float StoppedSpeedMps = 0.5f;

    // ----- Steering -----

    // The tires grip best a few degrees past the angle that
    // matches the turn, so allow that much extra steering.
    private const float PeakSlipAngleDeg = 7f;

    // At top speed the wheels turn this share as fast as at low speed
    private const float HighSpeedSteerRate = 0.65f;

    // ----- Drifting -----

    // Steering input smaller than this does not start a drift
    private const float DriftSteerDeadzone = 0.1f;

    // A drift keeps going down to this share of the starting speed
    private const float DriftKeepSpeedShare = 0.5f;

    // How fast rear grip changes when a drift starts or ends
    private const float GripChangePerSecond = 3f;

    // ----- Suspension (scaled to the car's mass) -----

    private const float SuspensionFrequencyHz = 1.8f;
    private const float SuspensionDampingRatio = 0.75f;


    // ========================================================
    //  DRIVING ASSIST NUMBERS
    //  Assists are always on. They are skipped during a drift,
    //  so Space still gives you a slide.
    // ========================================================

    // Assists only work above this speed (m/s). Slip readings
    // are unreliable when the car is slow.
    private const float AssistMinSpeedMps = 5f;

    // Slip readings: 0 means gripping fine, higher means sliding.
    // This is how much more slip it takes to go from
    // "no help" to "full help".
    private const float AssistSlipRange = 0.3f;

    // The least power or brake left when an assist is fully active
    private const float AssistMinScale = 0.25f;

    // Traction control: rear wheel slip where power starts to be cut
    private const float TcsSlipStart = 0.35f;

    // ABS: wheel slip where the brakes start to ease off
    private const float AbsSlipStart = 0.35f;

    // Steering assist: front wheel slip where steering starts to ease off,
    // and the least steering left (0.5 = half)
    private const float FrontSlipStart = 0.3f;
    private const float SteerReliefMin = 0.5f;

    // Drift assist: how strongly the wheels follow the travel direction
    private const float DriftCounterSteer = 0.8f;

    // Stability control: how far (degrees) the nose can point away
    // from the travel direction before the assist turns the car back.
    // Gain = how hard it pushes, MaxAccel = the strongest push.
    private const float StabilitySlipDeg = 10f;
    private const float StabilityGain = 12f;
    private const float StabilityMaxAccel = 6f;


    // ========================================================
    //  INTERNAL STATE  (the script's memory)
    // ========================================================

    // What the pedals are asking the car to do right now
    private enum PedalAction
    {
        Coast,
        Brake,
        DriveForward,
        DriveReverse
    }

    private Rigidbody rb;

    // Input (set from the keyboard)
    private bool gasPressed;
    private bool brakePressed;
    private bool spacePressed;
    private float steering;

    // Current state of the car
    private bool isDrifting;
    private float throttle;
    private float currentSteerAngle;
    private float currentRearGrip;
    private float slipAngleDeg;

    // Measured once at the start
    private float wheelRadius;
    private float wheelbase;
    private float rearLeftBaseGrip;
    private float rearRightBaseGrip;

    // Other scripts can read these
    public bool IsDrifting
    {
        get { return isDrifting; }
    }

    public float ThrottleAmount
    {
        get { return throttle; }
    }

    public float CurrentSpeed
    {
        get { return rb != null ? rb.linearVelocity.magnitude : 0f; }
    }

    // The settings in m/s, which is what the physics uses
    private float TopSpeedMps
    {
        get { return topSpeedMph * MphToMps; }
    }

    private float ReverseSpeedMps
    {
        get { return reverseSpeedMph * MphToMps; }
    }

    private float MinDriftSpeedMps
    {
        get { return minDriftSpeedMph * MphToMps; }
    }


    // ========================================================
    //  UNITY EVENTS  (Unity calls these for us)
    // ========================================================

    void Start()
    {
        rb = GetComponent<Rigidbody>();

        if (!AllWheelsAssigned())
        {
            Debug.LogError("Assign all 4 WheelColliders!");
            enabled = false;
            return;
        }

        rb.centerOfMass = new Vector3(0f, centerOfMassYMeters, 0f);

        // Remember the rear grip from the scene so we can scale it later
        rearLeftBaseGrip = rearLeftWheel.sidewaysFriction.stiffness;
        rearRightBaseGrip = rearRightWheel.sidewaysFriction.stiffness;
        currentRearGrip = tireGripG;

        wheelRadius = MeasureWheelRadius();
        wheelbase = MeasureWheelbase();

        ApplySuspension();
    }

    // Runs every frame: read the keyboard
    void Update()
    {
        ReadInput();
    }

    // Runs every physics step: drive the car
    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;

        // Speed along the kart's nose. Negative = rolling backwards.
        Vector3 localVelocity =
            transform.InverseTransformDirection(rb.linearVelocity);

        float forwardSpeed = localVelocity.z;
        float speed = rb.linearVelocity.magnitude;

        slipAngleDeg = GetSlipAngleDeg();

        HandleDrift(forwardSpeed, dt);
        HandleSteering(speed, dt);
        HandlePedals(forwardSpeed, dt);
        HandleStability();
    }

    // Runs after everything else: move the wheel models
    void LateUpdate()
    {
        UpdateWheelMesh(frontLeftWheel, frontLeftMesh);
        UpdateWheelMesh(frontRightWheel, frontRightMesh);
        UpdateWheelMesh(rearLeftWheel, rearLeftMesh);
        UpdateWheelMesh(rearRightWheel, rearRightMesh);
    }

    // Put the rear grip back how we found it
    void OnDisable()
    {
        if (rearLeftWheel != null && rearLeftBaseGrip > 0f)
            SetSidewaysGrip(rearLeftWheel, rearLeftBaseGrip);

        if (rearRightWheel != null && rearRightBaseGrip > 0f)
            SetSidewaysGrip(rearRightWheel, rearRightBaseGrip);
    }


    // ========================================================
    //  INPUT
    // ========================================================

    void ReadInput()
    {
        Keyboard keyboard = Keyboard.current;

        // No keyboard found: release everything
        if (keyboard == null)
        {
            gasPressed = false;
            brakePressed = false;
            spacePressed = false;
            steering = 0f;
            return;
        }

        gasPressed = keyboard.wKey.isPressed;
        brakePressed = keyboard.sKey.isPressed;
        spacePressed = keyboard.spaceKey.isPressed;

        bool left = keyboard.aKey.isPressed;
        bool right = keyboard.dKey.isPressed;

        // -1 = full left, 0 = straight, 1 = full right.
        // Pressing both keys cancels out.
        steering = 0f;

        if (left && !right)
            steering = -1f;

        if (right && !left)
            steering = 1f;
    }


    // ========================================================
    //  1. DRIFT
    // ========================================================

    void HandleDrift(float forwardSpeed, float dt)
    {
        // Speed along the ground (ignores going up or down)
        Vector3 flatVelocity =
            Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);

        float flatSpeed = flatVelocity.magnitude;

        // A drift starts when you are fast enough AND steering
        bool canStartDrift =
            forwardSpeed >= MinDriftSpeedMps &&
            Mathf.Abs(steering) > DriftSteerDeadzone;

        // Once started, a drift can go on at a lower speed
        bool canKeepDrifting =
            flatSpeed >= MinDriftSpeedMps * DriftKeepSpeedShare;

        if (!spacePressed)
        {
            isDrifting = false;
        }
        else if (isDrifting)
        {
            isDrifting = canKeepDrifting;
        }
        else
        {
            isDrifting = canStartDrift;
        }

        // Less rear grip while drifting, changed smoothly
        float targetGrip = tireGripG;

        if (isDrifting)
            targetGrip = driftGripG;

        currentRearGrip = Mathf.MoveTowards(
            currentRearGrip,
            targetGrip,
            GripChangePerSecond * dt
        );

        ApplyRearGrip(currentRearGrip);
    }


    // ========================================================
    //  2. STEERING
    // ========================================================

    void HandleSteering(float speed, float dt)
    {
        // How far to turn the wheels with the key fully pressed
        float angleLimit = maxSteerAngleDeg;

        if (isDrifting)
        {
            angleLimit = driftSteerAngleDeg;
        }
        else
        {
            // Less lock at high speed, or the front tires just slide
            angleLimit = Mathf.Min(
                maxSteerAngleDeg,
                GetGripLimitedSteerAngle(speed)
            );
        }

        float targetAngle = steering * angleLimit;

        // Assists: ease off if the front slides, follow the slide in a drift
        targetAngle *= GetUndersteerRelief();
        targetAngle += GetDriftCounterSteer();

        targetAngle = Mathf.Clamp(
            targetAngle,
            -driftSteerAngleDeg,
            driftSteerAngleDeg
        );

        // Turn the wheels toward the target, a little slower at high speed
        float speedShare =
            Mathf.Clamp01(speed / Mathf.Max(1f, TopSpeedMps));

        float rateScale =
            Mathf.Lerp(1f, HighSpeedSteerRate, speedShare);

        float turnRate = steeringResponseDegPerSec * rateScale;

        currentSteerAngle = Mathf.MoveTowards(
            currentSteerAngle,
            targetAngle,
            turnRate * dt
        );

        frontLeftWheel.steerAngle = currentSteerAngle;
        frontRightWheel.steerAngle = currentSteerAngle;
    }

    // Biggest steering angle the front tires can hold at this speed.
    // The faster you go, the smaller the angle. Steering more than
    // this only makes the front tires slide.
    float GetGripLimitedSteerAngle(float speed)
    {
        float maxSideAccel = tireGripG * Gravity;

        // Avoid dividing by zero when stopped
        float safeSpeed = Mathf.Max(speed, 0.1f);

        // Physics of a turn:
        // sideways acceleration = speed^2 * tan(angle) / wheelbase
        float holdAngle = Mathf.Atan(
            wheelbase * maxSideAccel / (safeSpeed * safeSpeed)
        ) * Mathf.Rad2Deg;

        return holdAngle + PeakSlipAngleDeg;
    }


    // ========================================================
    //  3. PEDALS  (engine and brakes)
    // ========================================================

    void HandlePedals(float forwardSpeed, float dt)
    {
        PedalAction action = GetPedalAction(forwardSpeed);

        UpdateThrottle(action, dt);
        ApplyEngine(action, forwardSpeed);
        ApplyBrakes(action);
    }

    // Work out what the pedals mean right now
    PedalAction GetPedalAction(float forwardSpeed)
    {
        bool rollingForward = forwardSpeed > StoppedSpeedMps;
        bool rollingBackward = forwardSpeed < -StoppedSpeedMps;

        // Both pedals = brake
        if (gasPressed && brakePressed)
            return PedalAction.Brake;

        // Gas: drive forward (brake first if rolling backwards)
        if (gasPressed)
        {
            if (rollingBackward)
                return PedalAction.Brake;

            return PedalAction.DriveForward;
        }

        // Brake pedal: reverse (brake first if rolling forwards)
        if (brakePressed)
        {
            if (rollingForward)
                return PedalAction.Brake;

            return PedalAction.DriveReverse;
        }

        // No pedals: the kart slows down on its own
        return PedalAction.Coast;
    }

    // Throttle eases in instead of jumping straight to full
    void UpdateThrottle(PedalAction action, float dt)
    {
        bool engineOn =
            action == PedalAction.DriveForward ||
            action == PedalAction.DriveReverse;

        float targetThrottle = 0f;

        if (engineOn)
            targetThrottle = 1f;

        // Throttle goes 0 to 1 in throttleResponseSeconds
        float throttleRate =
            1f / Mathf.Max(0.05f, throttleResponseSeconds);

        throttle = Mathf.MoveTowards(
            throttle,
            targetThrottle,
            throttleRate * dt
        );
    }

    // Send engine power to the rear wheels
    void ApplyEngine(PedalAction action, float forwardSpeed)
    {
        float motorTorque = 0f;

        if (action == PedalAction.DriveForward)
        {
            float speedForward = Mathf.Max(0f, forwardSpeed);
            float force = GetDriveForce(speedForward, TopSpeedMps);

            motorTorque = ForceToWheelTorque(force) * throttle;
        }

        if (action == PedalAction.DriveReverse)
        {
            float speedBackward = Mathf.Max(0f, -forwardSpeed);
            float force = GetDriveForce(speedBackward, ReverseSpeedMps);

            motorTorque = -ForceToWheelTorque(force) * throttle;
        }

        // Traction control: less power if the rear wheels spin
        motorTorque *= GetTractionControlScale();

        rearLeftWheel.motorTorque = motorTorque;
        rearRightWheel.motorTorque = motorTorque;
    }

    // Apply brakes (or coasting drag) to all four wheels
    void ApplyBrakes(PedalAction action)
    {
        float brakeTorque = 0f;

        if (action == PedalAction.Brake)
            brakeTorque = GetBrakeTorquePerWheel(brakingDecelG);

        if (action == PedalAction.Coast)
            brakeTorque = GetBrakeTorquePerWheel(coastDecelG);

        SetWheelBrake(frontLeftWheel, brakeTorque);
        SetWheelBrake(frontRightWheel, brakeTorque);
        SetWheelBrake(rearLeftWheel, brakeTorque);
        SetWheelBrake(rearRightWheel, brakeTorque);
    }

    // ABS lives inside this: a locking wheel gets less brake
    void SetWheelBrake(WheelCollider wheel, float brakeTorque)
    {
        wheel.brakeTorque = brakeTorque * GetAbsScale(wheel);
    }

    // How hard the engine pushes the car forward, in newtons.
    // A real engine pushes hard at low speed and less as speed climbs
    // (force = power / speed). Tire grip puts a cap on the push.
    float GetDriveForce(float speedInDirection, float speedLimit)
    {
        float powerWatts = enginePowerHp * HpToWatts;

        // Avoid dividing by zero when stopped
        float powerForce =
            powerWatts / Mathf.Max(1f, speedInDirection);

        float gripForce =
            LaunchTraction * tireGripG * Gravity * rb.mass;

        float force = Mathf.Min(powerForce, gripForce);

        // Ease off the power as we near the speed limit
        float speedShare = Mathf.Clamp01(
            speedInDirection / Mathf.Max(0.1f, speedLimit)
        );

        float powerLeft = 1f - speedShare * speedShare;

        return force * powerLeft;
    }

    // Turn a push on the car into a twist on each driven wheel
    float ForceToWheelTorque(float force)
    {
        return force * wheelRadius / DrivenWheelCount;
    }

    // Brake twist per wheel needed to slow the car at a given g.
    // Force = mass x deceleration, shared by all wheels.
    float GetBrakeTorquePerWheel(float decelG)
    {
        return rb.mass * decelG * Gravity * wheelRadius / WheelCount;
    }


    // ========================================================
    //  4. STABILITY
    // ========================================================

    // Stability control: if the back of the kart swings out too far,
    // spin the kart back toward the way it is travelling.
    void HandleStability()
    {
        if (isDrifting)
            return;

        float excessDeg = Mathf.Abs(slipAngleDeg) - StabilitySlipDeg;

        // Nose is close enough to the travel direction: nothing to fix
        if (excessDeg <= 0f)
            return;

        // The further off, the harder the push (up to a maximum)
        float turnPush = Mathf.Min(
            excessDeg * Mathf.Deg2Rad * StabilityGain,
            StabilityMaxAccel
        );

        // Positive angle = travelling to the right of the nose,
        // so turn right
        float direction = Mathf.Sign(slipAngleDeg);

        rb.AddTorque(
            Vector3.up * direction * turnPush,
            ForceMode.Acceleration
        );
    }


    // ========================================================
    //  DRIVING ASSISTS
    //  Each one returns a number between AssistMinScale and 1
    //  (1 = no help needed) or a steering amount.
    // ========================================================

    // The angle between where the kart points and where it travels.
    // Positive = travelling to the right of the nose.
    // 0 = going straight, or too slow to tell.
    float GetSlipAngleDeg()
    {
        Vector3 travel =
            Vector3.ProjectOnPlane(rb.linearVelocity, Vector3.up);

        Vector3 nose =
            Vector3.ProjectOnPlane(transform.forward, Vector3.up);

        if (travel.magnitude < AssistMinSpeedMps)
            return 0f;

        // Reversing does not count
        if (Vector3.Dot(travel, nose) <= 0f)
            return 0f;

        return Vector3.SignedAngle(nose, travel, Vector3.up);
    }

    // Traction control: rear wheels spinning means less power
    float GetTractionControlScale()
    {
        if (isDrifting || !FastEnoughForAssists())
            return 1f;

        float spin = Mathf.Max(
            Mathf.Abs(GetForwardSlip(rearLeftWheel)),
            Mathf.Abs(GetForwardSlip(rearRightWheel))
        );

        float help = GetAssistAmount(spin, TcsSlipStart);

        return Mathf.Lerp(1f, AssistMinScale, help);
    }

    // ABS: a wheel that starts to lock gets less brake, so it keeps gripping
    float GetAbsScale(WheelCollider wheel)
    {
        if (!FastEnoughForAssists())
            return 1f;

        float lockAmount = GetForwardSlip(wheel);
        float help = GetAssistAmount(lockAmount, AbsSlipStart);

        return Mathf.Lerp(1f, AssistMinScale, help);
    }

    // Steering assist: front tires sliding means less steering,
    // which lets them grip again
    float GetUndersteerRelief()
    {
        if (isDrifting || !FastEnoughForAssists())
            return 1f;

        float slide = Mathf.Max(
            Mathf.Abs(GetSidewaysSlip(frontLeftWheel)),
            Mathf.Abs(GetSidewaysSlip(frontRightWheel))
        );

        float help = GetAssistAmount(slide, FrontSlipStart);

        return Mathf.Lerp(1f, SteerReliefMin, help);
    }

    // Drift assist: point the wheels where the kart is travelling,
    // so you hold the slide with your steering instead of fighting it
    float GetDriftCounterSteer()
    {
        if (!isDrifting)
            return 0f;

        return slipAngleDeg * DriftCounterSteer;
    }

    // 0 = no help needed, 1 = full help.
    // Goes up as slip rises past its start value.
    float GetAssistAmount(float slip, float start)
    {
        float amount = (Mathf.Abs(slip) - start) / AssistSlipRange;

        return Mathf.Clamp01(amount);
    }

    bool FastEnoughForAssists()
    {
        return rb.linearVelocity.magnitude >= AssistMinSpeedMps;
    }

    // How much a wheel is spinning or locking (0 = perfect grip)
    float GetForwardSlip(WheelCollider wheel)
    {
        WheelHit hit;

        bool touchingGround = wheel.GetGroundHit(out hit);

        if (!touchingGround)
            return 0f;

        return hit.forwardSlip;
    }

    // How much a wheel is sliding sideways (0 = perfect grip)
    float GetSidewaysSlip(WheelCollider wheel)
    {
        WheelHit hit;

        bool touchingGround = wheel.GetGroundHit(out hit);

        if (!touchingGround)
            return 0f;

        return hit.sidewaysSlip;
    }


    // ========================================================
    //  SETUP HELPERS  (run once at the start)
    // ========================================================

    bool AllWheelsAssigned()
    {
        return frontLeftWheel != null &&
               frontRightWheel != null &&
               rearLeftWheel != null &&
               rearRightWheel != null;
    }

    // Average wheel radius in meters
    float MeasureWheelRadius()
    {
        float total =
            frontLeftWheel.radius +
            frontRightWheel.radius +
            rearLeftWheel.radius +
            rearRightWheel.radius;

        return total / WheelCount;
    }

    // Distance between the front and rear axles, in meters
    float MeasureWheelbase()
    {
        Vector3 frontMiddle = (
            frontLeftWheel.transform.position +
            frontRightWheel.transform.position
        ) * 0.5f;

        Vector3 rearMiddle = (
            rearLeftWheel.transform.position +
            rearRightWheel.transform.position
        ) * 0.5f;

        // Measure along the kart's own forward direction
        Vector3 front = transform.InverseTransformPoint(frontMiddle);
        Vector3 rear = transform.InverseTransformPoint(rearMiddle);

        float distance = Mathf.Abs(front.z - rear.z);

        return Mathf.Max(0.5f, distance);
    }

    // Set each wheel's spring and damper from the car's mass,
    // so a heavy car does not sink into its suspension
    void ApplySuspension()
    {
        float massPerWheel = rb.mass / WheelCount;
        float omega = 2f * Mathf.PI * SuspensionFrequencyHz;

        float spring = massPerWheel * omega * omega;

        float damper =
            2f * SuspensionDampingRatio *
            Mathf.Sqrt(spring * massPerWheel);

        SetSuspension(frontLeftWheel, spring, damper);
        SetSuspension(frontRightWheel, spring, damper);
        SetSuspension(rearLeftWheel, spring, damper);
        SetSuspension(rearRightWheel, spring, damper);
    }

    void SetSuspension(WheelCollider wheel, float spring, float damper)
    {
        JointSpring joint = wheel.suspensionSpring;

        joint.spring = spring;
        joint.damper = damper;

        wheel.suspensionSpring = joint;
    }


    // ========================================================
    //  WHEEL GRIP AND VISUALS
    // ========================================================

    // Scale the rear grip (1 = the grip set in the scene)
    void ApplyRearGrip(float gripScale)
    {
        SetSidewaysGrip(rearLeftWheel, rearLeftBaseGrip * gripScale);
        SetSidewaysGrip(rearRightWheel, rearRightBaseGrip * gripScale);
    }

    void SetSidewaysGrip(WheelCollider wheel, float stiffness)
    {
        WheelFrictionCurve friction = wheel.sidewaysFriction;

        friction.stiffness = stiffness;

        wheel.sidewaysFriction = friction;
    }

    // Make a wheel model follow its collider
    void UpdateWheelMesh(WheelCollider wheel, Transform mesh)
    {
        if (wheel == null || mesh == null)
            return;

        Vector3 position;
        Quaternion rotation;

        wheel.GetWorldPose(out position, out rotation);

        mesh.position = position;
        mesh.rotation = rotation;
    }
}
