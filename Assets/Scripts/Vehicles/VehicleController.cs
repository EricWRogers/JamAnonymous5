using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[Serializable]
public sealed class VehicleAxle
{
    [Header("Physics Wheels")]
    [SerializeField] private WheelCollider leftWheel;
    [SerializeField] private WheelCollider rightWheel;

    [Header("Visual Wheels")]
    [SerializeField] private Transform leftVisual;
    [SerializeField] private Transform rightVisual;
    [SerializeField] private Vector3 leftVisualEulerOffset;
    [SerializeField] private Vector3 rightVisualEulerOffset;

    [Header("Axle Responsibilities")]
    [SerializeField] private bool steering;
    [SerializeField] private bool driven = true;
    [SerializeField] private bool serviceBrake = true;
    [SerializeField] private bool handbrake;

    public WheelCollider LeftWheel => leftWheel;
    public WheelCollider RightWheel => rightWheel;
    public Transform LeftVisual => leftVisual;
    public Transform RightVisual => rightVisual;
    public Vector3 LeftVisualEulerOffset => leftVisualEulerOffset;
    public Vector3 RightVisualEulerOffset => rightVisualEulerOffset;
    public bool Steering => steering;
    public bool Driven => driven;
    public bool ServiceBrake => serviceBrake;
    public bool Handbrake => handbrake;
}

/// <summary>
/// Shared force-based vehicle simulation for player-driven and AI vehicles.
/// When network-spawned, physics runs on the owner. The host authorizes seat
/// changes; the seated owner supplies input locally without input RPCs.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(NetworkObject), typeof(NetworkTransform))]
[RequireComponent(typeof(NetworkRigidbody))]
public sealed class VehicleController : NetworkBehaviour
{
    private const float KilometersPerHourPerMeterPerSecond = 3.6f;
    private const float DirectionChangeSpeedThreshold = 0.5f;

    [Header("Vehicle Setup")]
    [SerializeField] private Rigidbody vehicleBody;
    [Tooltip("Optional marker used to override the Rigidbody center of mass.")]
    [SerializeField] private Transform centerOfMassMarker;
    [SerializeField] private List<VehicleAxle> axles = new();

    [Header("Engine")]
    [Tooltip("Total forward torque divided across every driven wheel.")]
    [Min(0f)] [SerializeField] private float forwardMotorTorque = 4500f;
    [Tooltip("Total reverse torque divided across every driven wheel.")]
    [Min(0f)] [SerializeField] private float reverseMotorTorque = 2500f;
    [Min(0.1f)] [SerializeField] private float maximumForwardSpeedKph = 110f;
    [Min(0.1f)] [SerializeField] private float maximumReverseSpeedKph = 30f;

    [Header("Steering")]
    [Range(0f, 60f)] [SerializeField] private float maximumSteeringAngle = 32f;
    [Range(0.05f, 1f)] [SerializeField] private float steeringAtMaximumSpeed = 0.35f;
    [Min(0f)] [SerializeField] private float steeringInputResponse = 5f;

    [Header("Braking")]
    [Min(0f)] [SerializeField] private float serviceBrakeTorque = 7000f;
    [Min(0f)] [SerializeField] private float directionChangeBrakeTorque = 4500f;
    [Min(0f)] [SerializeField] private float coastingBrakeTorque = 100f;
    [Min(0f)] [SerializeField] private float handbrakeTorque = 11000f;

    [Header("Body Stability")]
    [Tooltip("Optional force per metre/second that presses a grounded vehicle toward its local down direction.")]
    [Min(0f)] [SerializeField] private float downforceCoefficient;
    [Tooltip("Optional axle roll resistance. Leave low or zero for an easier-to-tip vehicle.")]
    [Min(0f)] [SerializeField] private float antiRollForce;
    [Min(0.1f)] [SerializeField] private float maximumAngularSpeed = 7f;
    [Range(1, 30)] [SerializeField] private int solverIterations = 12;
    [Range(1, 20)] [SerializeField] private int solverVelocityIterations = 4;

    [Header("Input Safety")]
    [Tooltip("Brakes the vehicle if its input source stops sending commands. Set to zero to disable.")]
    [Min(0f)] [SerializeField] private float staleInputTimeout = 0.5f;
    [Range(0f, 1f)] [SerializeField] private float staleInputBrake = 1f;
    [Min(0f)] [SerializeField] private float throttleInputResponse = 4f;
    [Min(0f)] [SerializeField] private float brakeInputResponse = 8f;

    private readonly HashSet<WheelCollider> uniqueWheels = new();
    private VehicleInputState targetInput;
    private VehicleInputState appliedInput;
    private float lastInputTime;
    private int drivenWheelCount;
    private int groundedWheelCount;
    private NetworkTransform networkTransform;
    private bool setupIsValid;
    private bool settleOnNextPhysicsStep;
    private Vector3 previousVisualPosition;
    private float previousVisualYaw;
    private float observedSpeedKph;
    private float remoteSteering;
    private readonly Dictionary<WheelCollider, float> remoteWheelSpin = new();

    public Rigidbody VehicleBody => vehicleBody;
    public VehicleInputState AppliedInput => appliedInput;
    public float SpeedKph => vehicleBody != null
        ? vehicleBody.linearVelocity.magnitude * KilometersPerHourPerMeterPerSecond
        : 0f;
    public float ForwardSpeedKph => vehicleBody != null
        ? Vector3.Dot(vehicleBody.linearVelocity, transform.forward) * KilometersPerHourPerMeterPerSecond
        : 0f;
    public bool IsGrounded => groundedWheelCount > 0;
    public bool CanSimulate => enabled && setupIsValid && CanRunAuthoritativePhysics();
    public float ObservedSpeedKph => CanSimulate ? SpeedKph : observedSpeedKph;

    private void Reset()
    {
        vehicleBody = GetComponent<Rigidbody>();

        NetworkTransform foundNetworkTransform = GetComponent<NetworkTransform>();
        if (foundNetworkTransform != null)
        {
            foundNetworkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        }
    }

    private void Awake()
    {
        if (vehicleBody == null)
        {
            vehicleBody = GetComponent<Rigidbody>();
        }

        networkTransform = GetComponent<NetworkTransform>();
        setupIsValid = ValidateSetup(logErrors: true);

        if (!setupIsValid)
        {
            enabled = false;
            return;
        }

        ApplyBodyConfiguration();
        lastInputTime = Time.time;
        previousVisualPosition = transform.position;
        previousVisualYaw = transform.eulerAngles.y;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (networkTransform != null &&
            networkTransform.AuthorityMode != NetworkTransform.AuthorityModes.Owner)
        {
            Debug.LogError(
                $"[{nameof(VehicleController)}] {name} requires its NetworkTransform Authority Mode to be Owner.",
                this
            );
            enabled = false;
            return;
        }

        ResetAuthorityState();
    }

    protected override void OnOwnershipChanged(ulong previous, ulong current)
    {
        base.OnOwnershipChanged(previous, current);
        ResetAuthorityState();
    }

    private void ResetAuthorityState()
    {
        ClearInputInternal(applySafetyBrake: true);
        ClearWheelForces();
        groundedWheelCount = 0;
        settleOnNextPhysicsStep = IsOwner;
        previousVisualPosition = transform.position;
        previousVisualYaw = transform.eulerAngles.y;
        observedSpeedKph = 0f;
    }

    public void SettleAndBrake()
    {
        if (!CanSimulate) return;
        ClearInputInternal(applySafetyBrake: true);
        if (!vehicleBody.isKinematic)
        {
            vehicleBody.linearVelocity = Vector3.zero;
            vehicleBody.angularVelocity = Vector3.zero;
        }
        settleOnNextPhysicsStep = true;
    }

    public override void OnNetworkDespawn()
    {
        ClearInputInternal(applySafetyBrake: false);
        ClearWheelForces();
        base.OnNetworkDespawn();
    }

    private void FixedUpdate()
    {
        if (!setupIsValid || !CanRunAuthoritativePhysics())
        {
            return;
        }

        // NetworkRigidbody owns this switch online; only enable offline simulation here.
        if (!IsSpawned && vehicleBody.isKinematic) vehicleBody.isKinematic = false;
        if (vehicleBody.isKinematic) return;
        if (settleOnNextPhysicsStep)
        {
            vehicleBody.linearVelocity = Vector3.zero;
            vehicleBody.angularVelocity = Vector3.zero;
            settleOnNextPhysicsStep = false;
        }
        ApplyStaleInputSafety();
        SmoothAppliedInput(Time.fixedDeltaTime);

        float forwardSpeedMetersPerSecond = Vector3.Dot(vehicleBody.linearVelocity, transform.forward);
        float absoluteSpeedKph = vehicleBody.linearVelocity.magnitude * KilometersPerHourPerMeterPerSecond;
        float steeringAngle = CalculateSteeringAngle(absoluteSpeedKph);
        float motorTorque = CalculateMotorTorque(forwardSpeedMetersPerSecond);
        float brakingTorque = CalculateBrakingTorque(forwardSpeedMetersPerSecond);

        groundedWheelCount = 0;

        for (int i = 0; i < axles.Count; i++)
        {
            VehicleAxle axle = axles[i];
            ApplyAxleForces(axle, steeringAngle, motorTorque, brakingTorque);
            ApplyAntiRoll(axle);
        }

        if (groundedWheelCount > 0 && downforceCoefficient > 0f)
        {
            float downforce = downforceCoefficient * vehicleBody.linearVelocity.magnitude;
            vehicleBody.AddForce(-transform.up * downforce, ForceMode.Force);
        }
    }

    private void LateUpdate()
    {
        if (!setupIsValid)
        {
            return;
        }

        Vector3 displacement = transform.position - previousVisualPosition;
        float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
        float signedDistance = Vector3.Dot(displacement, transform.forward);
        float yawDelta = Mathf.DeltaAngle(previousVisualYaw, transform.eulerAngles.y);
        observedSpeedKph = Mathf.Lerp(observedSpeedKph, displacement.magnitude / deltaTime * 3.6f,
            1f - Mathf.Exp(-12f * deltaTime));
        float estimatedSteer = Mathf.Abs(signedDistance) > 0.002f
            ? Mathf.Clamp(Mathf.Atan(yawDelta * Mathf.Deg2Rad * 3.4f / signedDistance) * Mathf.Rad2Deg,
                -maximumSteeringAngle, maximumSteeringAngle) : 0f;
        remoteSteering = Mathf.Lerp(remoteSteering, estimatedSteer, 1f - Mathf.Exp(-8f * deltaTime));
        previousVisualPosition = transform.position;
        previousVisualYaw = transform.eulerAngles.y;

        for (int i = 0; i < axles.Count; i++)
        {
            VehicleAxle axle = axles[i];
            if (CanSimulate)
            {
                UpdateWheelVisual(axle.LeftWheel, axle.LeftVisual, axle.LeftVisualEulerOffset);
                UpdateWheelVisual(axle.RightWheel, axle.RightVisual, axle.RightVisualEulerOffset);
            }
            else
            {
                UpdateRemoteWheel(axle.LeftWheel, axle.LeftVisual, axle.LeftVisualEulerOffset, axle.Steering, signedDistance);
                UpdateRemoteWheel(axle.RightWheel, axle.RightVisual, axle.RightVisualEulerOffset, axle.Steering, signedDistance);
            }
        }
    }

    private void UpdateRemoteWheel(WheelCollider wheel, Transform visual, Vector3 offset, bool steers, float distance)
    {
        if (visual == null) return;
        remoteWheelSpin.TryGetValue(wheel, out float spin);
        spin = Mathf.Repeat(spin + distance / Mathf.Max(wheel.radius, 0.01f) * Mathf.Rad2Deg, 360f);
        remoteWheelSpin[wheel] = spin;
        visual.SetPositionAndRotation(wheel.transform.TransformPoint(wheel.center - Vector3.up * wheel.suspensionDistance * 0.5f),
            wheel.transform.rotation * Quaternion.Euler(0f, steers ? remoteSteering : 0f, 0f) *
            Quaternion.Euler(spin, 0f, 0f) * Quaternion.Euler(offset));
    }

    private void OnDisable()
    {
        if (setupIsValid && CanRunAuthoritativePhysics())
        {
            ClearWheelForces();
        }
    }

    public bool TrySetInput(VehicleInputState input)
    {
        if (!setupIsValid || !CanRunAuthoritativePhysics())
        {
            return false;
        }

        if (!input.HasFiniteValues)
        {
            ClearInputInternal(applySafetyBrake: true);
            return false;
        }
        targetInput = input.Sanitized();
        lastInputTime = Time.time;
        return true;
    }

    public void ClearInput()
    {
        if (!CanRunAuthoritativePhysics())
        {
            return;
        }

        ClearInputInternal(applySafetyBrake: true);
    }


    public Vector3 GetVelocityAtPoint(Vector3 worldPoint)
    {
        return vehicleBody != null ? vehicleBody.GetPointVelocity(worldPoint) : Vector3.zero;
    }

    [ContextMenu("Validate Vehicle Setup")]
    private void ValidateSetupFromInspector()
    {
        ValidateSetup(logErrors: true);
    }

    private bool ValidateSetup(bool logErrors)
    {
        uniqueWheels.Clear();
        drivenWheelCount = 0;

        if (vehicleBody == null)
        {
            return ReportSetupError("A Rigidbody reference is required.", logErrors);
        }

        if (axles == null || axles.Count == 0)
        {
            return ReportSetupError("At least one axle must be configured.", logErrors);
        }

        bool hasSteeringAxle = false;

        for (int i = 0; i < axles.Count; i++)
        {
            VehicleAxle axle = axles[i];

            if (axle == null)
            {
                return ReportSetupError($"Axle {i} is missing.", logErrors);
            }

            if (axle.LeftWheel == null || axle.RightWheel == null)
            {
                return ReportSetupError($"Axle {i} requires both left and right WheelColliders.", logErrors);
            }

            if (!uniqueWheels.Add(axle.LeftWheel) || !uniqueWheels.Add(axle.RightWheel))
            {
                return ReportSetupError($"Axle {i} reuses a WheelCollider assigned to another axle.", logErrors);
            }

            if (axle.Driven)
            {
                drivenWheelCount += 2;
            }

            hasSteeringAxle |= axle.Steering;
        }

        if (drivenWheelCount == 0)
        {
            return ReportSetupError("At least one axle must be driven.", logErrors);
        }

        if (!hasSteeringAxle)
        {
            return ReportSetupError("At least one axle must steer.", logErrors);
        }

        return true;
    }

    private bool ReportSetupError(string message, bool logErrors)
    {
        if (logErrors)
        {
            Debug.LogError($"[{nameof(VehicleController)}] {name}: {message}", this);
        }

        return false;
    }

    private void ApplyBodyConfiguration()
    {
        vehicleBody.maxAngularVelocity = maximumAngularSpeed;
        vehicleBody.solverIterations = solverIterations;
        vehicleBody.solverVelocityIterations = solverVelocityIterations;

        if (centerOfMassMarker != null)
        {
            vehicleBody.centerOfMass = vehicleBody.transform.InverseTransformPoint(centerOfMassMarker.position);
        }
    }

    private bool CanRunAuthoritativePhysics()
    {
        if (IsSpawned)
        {
            return IsOwner;
        }

        NetworkManager manager = NetworkManager.Singleton;
        return manager == null || !manager.IsListening;
    }

    private void ApplyStaleInputSafety()
    {
        if (staleInputTimeout <= 0f || Time.time - lastInputTime <= staleInputTimeout)
        {
            return;
        }

        targetInput = new VehicleInputState(0f, 0f, staleInputBrake);
    }

    private void SmoothAppliedInput(float deltaTime)
    {
        appliedInput.Steering = Mathf.MoveTowards(
            appliedInput.Steering,
            targetInput.Steering,
            steeringInputResponse * deltaTime
        );
        appliedInput.Throttle = Mathf.MoveTowards(
            appliedInput.Throttle,
            targetInput.Throttle,
            throttleInputResponse * deltaTime
        );
        appliedInput.Brake = Mathf.MoveTowards(
            appliedInput.Brake,
            targetInput.Brake,
            brakeInputResponse * deltaTime
        );
        appliedInput.Handbrake = targetInput.Handbrake;
    }

    private float CalculateSteeringAngle(float absoluteSpeedKph)
    {
        float speedRatio = Mathf.Clamp01(absoluteSpeedKph / maximumForwardSpeedKph);
        float steeringScale = Mathf.Lerp(1f, steeringAtMaximumSpeed, speedRatio);
        return appliedInput.Steering * maximumSteeringAngle * steeringScale;
    }

    private float CalculateMotorTorque(float forwardSpeedMetersPerSecond)
    {
        float throttle = appliedInput.Throttle;

        if (Mathf.Abs(throttle) <= Mathf.Epsilon)
        {
            return 0f;
        }

        bool changingDirection =
            forwardSpeedMetersPerSecond > DirectionChangeSpeedThreshold && throttle < 0f ||
            forwardSpeedMetersPerSecond < -DirectionChangeSpeedThreshold && throttle > 0f;

        if (changingDirection)
        {
            return 0f;
        }

        float forwardSpeedKph = forwardSpeedMetersPerSecond * KilometersPerHourPerMeterPerSecond;

        if (throttle > 0f)
        {
            return forwardSpeedKph < maximumForwardSpeedKph
                ? throttle * forwardMotorTorque
                : 0f;
        }

        return forwardSpeedKph > -maximumReverseSpeedKph
            ? throttle * reverseMotorTorque
            : 0f;
    }

    private float CalculateBrakingTorque(float forwardSpeedMetersPerSecond)
    {
        float brakingTorque = appliedInput.Brake * serviceBrakeTorque;
        bool changingDirection =
            forwardSpeedMetersPerSecond > DirectionChangeSpeedThreshold && appliedInput.Throttle < 0f ||
            forwardSpeedMetersPerSecond < -DirectionChangeSpeedThreshold && appliedInput.Throttle > 0f;

        if (changingDirection)
        {
            brakingTorque = Mathf.Max(
                brakingTorque,
                Mathf.Abs(appliedInput.Throttle) * directionChangeBrakeTorque
            );
        }
        else if (Mathf.Abs(appliedInput.Throttle) < 0.01f)
        {
            brakingTorque = Mathf.Max(brakingTorque, coastingBrakeTorque);
        }

        return brakingTorque;
    }

    private void ApplyAxleForces(
        VehicleAxle axle,
        float steeringAngle,
        float motorTorque,
        float brakingTorque)
    {
        ApplyWheelForces(axle, axle.LeftWheel, steeringAngle, motorTorque, brakingTorque);
        ApplyWheelForces(axle, axle.RightWheel, steeringAngle, motorTorque, brakingTorque);
    }

    private void ApplyWheelForces(
        VehicleAxle axle,
        WheelCollider wheel,
        float steeringAngle,
        float motorTorque,
        float brakingTorque)
    {
        wheel.steerAngle = axle.Steering ? steeringAngle : 0f;
        wheel.motorTorque = axle.Driven ? motorTorque / drivenWheelCount : 0f;

        float wheelBrakeTorque = axle.ServiceBrake ? brakingTorque : 0f;
        if (axle.Handbrake && appliedInput.Handbrake)
        {
            wheelBrakeTorque = Mathf.Max(wheelBrakeTorque, handbrakeTorque);
        }

        wheel.brakeTorque = wheelBrakeTorque;

        if (wheel.isGrounded)
        {
            groundedWheelCount++;
        }
    }

    private void ApplyAntiRoll(VehicleAxle axle)
    {
        if (antiRollForce <= 0f)
        {
            return;
        }

        bool leftGrounded = TryGetSuspensionTravel(axle.LeftWheel, out float leftTravel);
        bool rightGrounded = TryGetSuspensionTravel(axle.RightWheel, out float rightTravel);
        float force = (leftTravel - rightTravel) * antiRollForce;

        if (leftGrounded)
        {
            vehicleBody.AddForceAtPosition(
                axle.LeftWheel.transform.up * -force,
                axle.LeftWheel.transform.position,
                ForceMode.Force
            );
        }

        if (rightGrounded)
        {
            vehicleBody.AddForceAtPosition(
                axle.RightWheel.transform.up * force,
                axle.RightWheel.transform.position,
                ForceMode.Force
            );
        }
    }

    private static bool TryGetSuspensionTravel(WheelCollider wheel, out float travel)
    {
        if (wheel.GetGroundHit(out WheelHit hit) && wheel.suspensionDistance > Mathf.Epsilon)
        {
            float localHitHeight = wheel.transform.InverseTransformPoint(hit.point).y;
            travel = (-localHitHeight - wheel.radius) / wheel.suspensionDistance;
            return true;
        }

        travel = 1f;
        return false;
    }

    private static void UpdateWheelVisual(
        WheelCollider wheel,
        Transform visual,
        Vector3 visualEulerOffset)
    {
        if (wheel == null || visual == null)
        {
            return;
        }

        wheel.GetWorldPose(out Vector3 worldPosition, out Quaternion worldRotation);
        visual.SetPositionAndRotation(
            worldPosition,
            worldRotation * Quaternion.Euler(visualEulerOffset)
        );
    }

    private void ClearInputInternal(bool applySafetyBrake)
    {
        float brake = applySafetyBrake ? staleInputBrake : 0f;
        targetInput = new VehicleInputState(0f, 0f, brake);
        appliedInput = targetInput;
        lastInputTime = Time.time;
    }

    private void ClearWheelForces()
    {
        if (axles == null)
        {
            return;
        }

        for (int i = 0; i < axles.Count; i++)
        {
            VehicleAxle axle = axles[i];
            if (axle == null) continue;

            ClearWheel(axle.LeftWheel);
            ClearWheel(axle.RightWheel);
        }
    }

    private static void ClearWheel(WheelCollider wheel)
    {
        if (wheel == null)
        {
            return;
        }

        wheel.motorTorque = 0f;
        wheel.brakeTorque = 0f;
        wheel.steerAngle = 0f;
    }
}
