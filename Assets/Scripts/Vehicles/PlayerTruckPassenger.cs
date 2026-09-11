using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>Owner-driven, collision-query movement in a moving truck reference frame.</summary>
[DefaultExecutionOrder(-150)]
[RequireComponent(typeof(PlayerMovement), typeof(CapsuleCollider), typeof(Rigidbody))]
public sealed class PlayerTruckPassenger : NetworkBehaviour
{
    private readonly NetworkVariable<TruckRelativePose> pose = new(TruckRelativePose.Detached,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly List<TruckTransformSuspension> sync = new();
    private readonly Dictionary<NetworkRigidbody, bool> autoKinematic = new();
    private readonly List<Collider> ignored = new();
    private PlayerMovement movement;
    private PlayerVehicleDriver driver;
    private PlayerCamera look;
    private CapsuleCollider capsule;
    private Rigidbody body;
    private VehicleKitchen kitchen;
    private Vector3 localPosition, relativeVelocity;
    private float previousYaw, sendAt, jumpGrace, collisionRestoreAt;
    private bool wasKinematic;
    private double simulationTime;
    private bool jumpQueued;
    private Collider[] overlapBuffer = new Collider[16];
    private InputSystem_Actions input;
    public bool IsAboard => sync.Count > 0;
    public bool Grounded { get; private set; }
    public float RelativeSpeed => Vector3.ProjectOnPlane(relativeVelocity, Vector3.up).magnitude;
    public Vector3 DepartureVelocity => kitchen != null ? kitchen.PointVelocity(transform.position) : Vector3.zero;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        driver = GetComponent<PlayerVehicleDriver>();
        look = GetComponentInChildren<PlayerCamera>(true);
        capsule = GetComponent<CapsuleCollider>();
        body = GetComponent<Rigidbody>();
        input = new InputSystem_Actions();
    }
    public override void OnNetworkSpawn()
    {
        if (IsOwner) input.Player.Enable();
    }
    public void PrepareForSeat()
    {
        if (IsAboard) Leave(false);
        // Seat code now owns collision suppression and must capture these pairs.
        RestoreCollisions(true);
    }
    private void LateUpdate()
    {
        if (!IsSpawned) return;
        if (!IsAboard && Time.time >= collisionRestoreAt) RestoreCollisions();
        if (driver != null && driver.IsSeated) return;
        if (!IsOwner)
        {
            FollowRemote();
            return;
        }
        if (!IsAboard)
        {
            VehicleKitchen candidate = VehicleKitchen.FindAt(transform.position);
            if (candidate == null) return;
            Enter(candidate);
        }
        if (kitchen == null || !kitchen.IsSpawned) { Leave(true); return; }
        Vector3 carriedPosition = kitchen.transform.TransformPoint(localPosition);
        float yawDelta = Mathf.DeltaAngle(previousYaw, kitchen.transform.eulerAngles.y);
        transform.rotation = Quaternion.Euler(0f, yawDelta, 0f) * transform.rotation;
        if (look != null) look.AddPlatformYaw(yawDelta);
        previousYaw = kitchen.transform.eulerAngles.y;
        transform.position = carriedPosition;
        IgnoreTruckCollisions();
        Physics.SyncTransforms();

        RecoverOverlaps();
        bool blocked = NetworkSessionMenu.IsGameMenuOpen || !Application.isFocused;
        Vector2 command = blocked ? Vector2.zero : input.Player.Move.ReadValue<Vector2>();
        bool sprinting = input.Player.Sprint.IsPressed();
        if (blocked) jumpQueued = false;
        else jumpQueued |= input.Player.Jump.WasPressedThisFrame();

        simulationTime += Time.deltaTime;
        float step = Time.fixedDeltaTime;
        // Bound catch-up work, but retain any unprocessed time for the next frame.
        for (int i = 0; i < 16 && simulationTime >= step; i++)
        {
            simulationTime -= step;
            SimulateStep(step, command, sprinting, blocked);
            RecoverOverlaps();
            if (!kitchen.Contains(transform.position, 0.2f)) break;
        }
        body.position = transform.position;
        body.rotation = transform.rotation;
        localPosition = kitchen.transform.InverseTransformPoint(transform.position);
        if (!kitchen.Contains(transform.position, 0.2f)) { Leave(true); return; }
        foreach (TruckTransformSuspension state in sync) state.Suspend();
        if (Time.unscaledTime >= sendAt)
        {
            PublishPose(new TruckRelativePose { Support = kitchen.NetworkObjectId, Position = localPosition,
                Rotation = Quaternion.Inverse(kitchen.transform.rotation) * transform.rotation });
            sendAt = Time.unscaledTime + 1f / Mathf.Max(1f, NetworkManager.NetworkConfig.TickRate);
        }
    }
    private void SimulateStep(float dt, Vector2 command, bool sprinting, bool blocked)
    {
        Grounded = relativeVelocity.y <= 0f && Cast(Vector3.down, 0.08f, out RaycastHit ground) && ground.normal.y > 0.55f;
        jumpGrace = Grounded ? movement.coyoteTime : Mathf.Max(0f, jumpGrace - dt);


        Vector3 wish = Vector3.ClampMagnitude(transform.forward * command.y + transform.right * command.x, 1f);
        float speed = sprinting ? movement.sprintSpeed : movement.walkSpeed;
        Vector3 horizontal = Vector3.ProjectOnPlane(relativeVelocity, Vector3.up);
        float acceleration = Grounded ? (wish.sqrMagnitude > 0f ? movement.acceleration : movement.deceleration) : movement.airAcceleration;
        horizontal = blocked ? Vector3.zero : Vector3.MoveTowards(horizontal, wish * speed, acceleration * dt);
        relativeVelocity = new Vector3(horizontal.x, relativeVelocity.y, horizontal.z);
        if (Grounded && relativeVelocity.y < 0f) relativeVelocity.y = 0f;
        if (!blocked && jumpQueued && jumpGrace > 0f)
        {
            relativeVelocity.y = movement.jumpForce;
            Grounded = false;
            jumpGrace = 0f;
        }
        jumpQueued = false;
        if (!Grounded)
            relativeVelocity.y += Physics.gravity.y * (relativeVelocity.y < 0f ? movement.fallGravityMultiplier : movement.riseGravityMultiplier) * dt;
        Move(relativeVelocity * dt);
    }

    private void PublishPose(TruckRelativePose state)
    {
        if (IsServer) AcceptPose(state);
        else SubmitPoseRpc(state);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitPoseRpc(TruckRelativePose state, RpcParams rpc = default)
    {
        if (rpc.Receive.SenderClientId != OwnerClientId) return;
        AcceptPose(state);
    }

    private void AcceptPose(TruckRelativePose state)
    {
        if (!IsServer || !IsSpawned || !ItemPlacement.IsFinite(state.Position)) return;
        Quaternion q = state.Rotation;
        float length = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
        if (!float.IsFinite(length) || length < 0.5f || length > 1.5f) return;
        bool seated = driver != null && driver.IsSeated;
        if (seated && state.Support != TruckRelativePose.None) return;
        if (state.Support != TruckRelativePose.None)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(state.Support, out NetworkObject support) ||
                !support.TryGetComponent(out VehicleKitchen target) ||
                !target.Contains(target.transform.TransformPoint(state.Position), 0.25f)) return;
        }
        pose.Value = state;
        // RPCs for pickup/placement can run before LateUpdate. Make their range
        // and eye-origin checks see the latest accepted passenger pose now.
        if (!IsOwner && !seated)
        {
            FollowRemote();
            Physics.SyncTransforms();
        }
    }
    private void Enter(VehicleKitchen target)
    {
        simulationTime = 0;
        jumpQueued = false;
        RestoreCollisions();
        kitchen = target;
        localPosition = target.transform.InverseTransformPoint(transform.position);
        previousYaw = target.transform.eulerAngles.y;
        relativeVelocity = body.isKinematic ? Vector3.zero : body.linearVelocity - DepartureVelocity;
        wasKinematic = body.isKinematic;
        foreach (NetworkRigidbody networkBody in GetComponentsInChildren<NetworkRigidbody>(true))
        {
            autoKinematic[networkBody] = networkBody.AutoUpdateKinematicState;
            networkBody.AutoUpdateKinematicState = false;
        }
        body.isKinematic = true;
        foreach (NetworkTransform nt in NetworkObject.GetComponentsInChildren<NetworkTransform>(true))
            if (nt.GetComponentInParent<Item>() == null) sync.Add(new TruckTransformSuspension(nt));
        IgnoreTruckCollisions();
        sendAt = 0f;
    }
    private void Leave(bool inheritVelocity)
    {
        simulationTime = 0;
        jumpQueued = false;
        Vector3 velocity = relativeVelocity + (inheritVelocity ? DepartureVelocity : Vector3.zero);
        kitchen = null;
        if (IsOwner && IsSpawned)
            PublishPose(new TruckRelativePose { Support = TruckRelativePose.None, Position = transform.position, Rotation = transform.rotation });
        foreach (TruckTransformSuspension state in sync) state.Restore();
        sync.Clear();
        foreach (var pair in autoKinematic) if (pair.Key != null) pair.Key.AutoUpdateKinematicState = pair.Value;
        autoKinematic.Clear();
        body.isKinematic = wasKinematic;
        if (!body.isKinematic) body.linearVelocity = velocity;
        collisionRestoreAt = Time.time + 0.25f;
        Grounded = false;
    }
    private void FollowRemote()
    {
        TruckRelativePose state = pose.Value;
        if (state.Support == TruckRelativePose.None)
        {
            if (IsAboard)
            {
                transform.SetPositionAndRotation(state.Position, state.Rotation);
                body.position = state.Position;
                body.rotation = state.Rotation;
                Leave(false);
            }
            return;
        }
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(state.Support, out NetworkObject obj) ||
            !obj.TryGetComponent(out VehicleKitchen target))
        {
            if (IsAboard && (kitchen == null || !kitchen.IsSpawned)) Leave(false);
            return;
        }
        if (kitchen != target)
        {
            if (IsAboard) Leave(false);
            Enter(target);
            localPosition = state.Position;
        }
        Vector3 previous = localPosition;
        localPosition = IsServer ? state.Position :
            Vector3.Lerp(localPosition, state.Position, 1f - Mathf.Exp(-20f * Time.deltaTime));
        relativeVelocity = target.transform.TransformVector(localPosition - previous) / Mathf.Max(Time.deltaTime, 0.001f);
        transform.SetPositionAndRotation(target.transform.TransformPoint(localPosition), target.transform.rotation * state.Rotation);
        body.position = transform.position;
        body.rotation = transform.rotation;
        Grounded = Cast(Vector3.down, 0.12f, out RaycastHit hit) && hit.normal.y > 0.55f;
        IgnoreTruckCollisions();
        foreach (TruckTransformSuspension suspension in sync) suspension.Suspend();
    }
    private bool IsOwnOrPlayerCollider(Collider other) =>
        other.transform.IsChildOf(NetworkObject.transform) || other.GetComponentInParent<PlayerPickup>() != null;

    private void GetCapsuleGeometry(out Vector3 top, out Vector3 bottom, out float radius)
    {
        Vector3 scale = transform.lossyScale;
        radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float halfSegment = Mathf.Max(0f, capsule.height * Mathf.Abs(scale.y) * 0.5f - radius);
        Vector3 center = transform.TransformPoint(capsule.center);
        top = center + transform.up * halfSegment;
        bottom = center - transform.up * halfSegment;
    }

    private void RecoverOverlaps()
    {
        // Carrying the upright capsule through a tilted frame can put it inside
        // the floor or furniture. Casts alone cannot resolve that initial overlap.
        for (int iteration = 0; iteration < 8; iteration++)
        {
            GetCapsuleGeometry(out Vector3 top, out Vector3 bottom, out float radius);
            int count;
            while (true)
            {
                count = Physics.OverlapCapsuleNonAlloc(top, bottom, radius, overlapBuffer,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (count < overlapBuffer.Length) break;
                System.Array.Resize(ref overlapBuffer, overlapBuffer.Length * 2);
            }
            bool moved = false;
            for (int i = 0; i < count; i++)
            {
                Collider other = overlapBuffer[i];
                if (other == null || IsOwnOrPlayerCollider(other)) continue;
                if (!Physics.ComputePenetration(capsule, transform.position, transform.rotation,
                    other, other.transform.position, other.transform.rotation,
                    out Vector3 direction, out float depth) || depth <= 0f) continue;
                transform.position += direction * (depth + 0.002f);
                float inwardSpeed = Vector3.Dot(relativeVelocity, direction);
                if (inwardSpeed < 0f) relativeVelocity -= direction * inwardSpeed;
                moved = true;
            }
            if (!moved) break;
        }
    }

    private bool Cast(Vector3 direction, float distance, out RaycastHit nearest)
    {
        GetCapsuleGeometry(out Vector3 top, out Vector3 bottom, out float radius);
        nearest = default;
        float closest = float.PositiveInfinity;
        foreach (RaycastHit hit in Physics.CapsuleCastAll(top,
            bottom, Mathf.Max(0.01f, radius - 0.015f), direction,
            distance + 0.015f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (IsOwnOrPlayerCollider(hit.collider)) continue;
            if (hit.distance < closest) { closest = hit.distance; nearest = hit; }
        }
        return nearest.collider != null;
    }
    private void Move(Vector3 displacement)
    {
        for (int i = 0; i < 3 && displacement.sqrMagnitude > 0.000001f; i++)
        {
            float distance = displacement.magnitude;
            if (!Cast(displacement / distance, distance, out RaycastHit hit)) { transform.position += displacement; break; }
            float travel = Mathf.Clamp(hit.distance - 0.015f, 0f, distance);
            transform.position += displacement / distance * travel;
            displacement = Vector3.ProjectOnPlane(displacement * (1f - travel / distance), hit.normal);
            relativeVelocity = Vector3.ProjectOnPlane(relativeVelocity, hit.normal);
        }
    }
    private void IgnoreTruckCollisions()
    {
        foreach (Collider collider in kitchen.GetComponentsInChildren<Collider>())
        {
            if (collider.isTrigger || collider == capsule || ignored.Contains(collider)) continue;
            if (!Physics.GetIgnoreCollision(capsule, collider))
            {
                Physics.IgnoreCollision(capsule, collider, true);
                ignored.Add(collider);
            }
        }
    }
    private void RestoreCollisions(bool force = false)
    {
        for (int i = ignored.Count - 1; i >= 0; i--)
        {
            Collider other = ignored[i];
            if (!force && other != null && capsule.enabled && other.enabled && capsule.bounds.Intersects(other.bounds)) continue;
            if (other != null) Physics.IgnoreCollision(capsule, other, false);
            ignored.RemoveAt(i);
        }
    }
    public override void OnNetworkDespawn()
    {
        if (IsAboard) Leave(false);
        foreach (Collider other in ignored) if (other != null) Physics.IgnoreCollision(capsule, other, false);
        ignored.Clear();
        input.Player.Disable();
    }
    public override void OnDestroy() { input?.Dispose(); base.OnDestroy(); }
}
