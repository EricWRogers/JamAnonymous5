using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>Local driving input and locally derived seated presentation on every peer.</summary>
[DefaultExecutionOrder(-100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public sealed class PlayerVehicleDriver : NetworkBehaviour, IVehicleInputSource
{
    private VehicleDriverSeat seat;
    private Rigidbody body;
    private CapsuleCollider capsule;
    private Camera playerCamera;
    private PlayerCamera cameraLook;
    private InputSystem_Actions inputs;
    private bool focused = true;
    private bool wasKinematic;
    private Vector3 cameraLocalPosition;
    private Quaternion cameraLocalRotation;
    private float yaw;
    private float pitch;
    private int enteredFrame;
    private readonly Dictionary<Behaviour, bool> suppressed = new();
    private readonly Dictionary<Collider, bool> colliders = new();
    private readonly Dictionary<Renderer, bool> renderers = new();
    private readonly List<TransformSyncState> transforms = new();
    private readonly Dictionary<NetworkRigidbody, bool> rigidbodyAutoState = new();
    private readonly List<SeatCollisionPair> seatCollisionPairs = new();
    private float restoreSeatCollisionsAfter;
    private static string notice;
    private static float noticeUntil;

    public bool IsSeated => seat != null;
    public VehicleDriverSeat CurrentSeat => seat;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();
        playerCamera = GetComponentInChildren<Camera>(true);
        cameraLook = GetComponentInChildren<PlayerCamera>(true);
        inputs = new InputSystem_Actions();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner) inputs.Player.Enable();
        foreach (AudioListener listener in GetComponentsInChildren<AudioListener>(true)) listener.enabled = IsOwner;
    }

    public override void OnNetworkDespawn()
    {
        if (IsSeated) LeaveSeat(transform.position, transform.rotation);
        RestoreSeatCollisions();
        inputs.Player.Disable();
    }

    public override void OnDestroy()
    {
        RestoreSeatCollisions();
        inputs?.Dispose();
        base.OnDestroy();
    }

    public void EnterSeat(VehicleDriverSeat target)
    {
        if (seat == target) return;
        if (IsSeated) LeaveSeat(transform.position, transform.rotation);
        RestoreSeatCollisions();
        if (TryGetComponent(out PlayerTruckPassenger passenger)) passenger.PrepareForSeat();
        seat = target;
        // A remote player's interpolated pose can lag its replicated seat state.
        // Keep it from pushing the truck during ownership and exit transitions.
        foreach (Collider playerCollider in GetComponentsInChildren<Collider>(true))
        foreach (Collider truckCollider in target.GetComponentsInChildren<Collider>(true))
        {
            if (!Physics.GetIgnoreCollision(playerCollider, truckCollider))
                seatCollisionPairs.Add(new SeatCollisionPair(playerCollider, truckCollider));
        }
        SetSeatCollisionsIgnored();
        enteredFrame = Time.frameCount;
        yaw = pitch = 0f;
        wasKinematic = body.isKinematic;
        foreach (NetworkRigidbody networkBody in GetComponentsInChildren<NetworkRigidbody>(true))
        {
            rigidbodyAutoState[networkBody] = networkBody.AutoUpdateKinematicState;
            networkBody.AutoUpdateKinematicState = false;
        }
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        body.isKinematic = true;
        foreach (Collider item in GetComponentsInChildren<Collider>(true))
        {
            colliders[item] = item.enabled;
            item.enabled = false;
        }
        foreach (Behaviour item in GetComponentsInChildren<Behaviour>(true))
        {
            if (item is PlayerMovement || item is PlayerCamera || item is PlayerInteraction ||
                item is PlayerPickup || item is WorldSpaceUIInteractor || item is PlayerEmotes)
            {
                suppressed[item] = item.enabled;
                item.enabled = false;
            }
        }
        // The normal player has a network root above its physics/movement child.
        foreach (NetworkTransform item in NetworkObject.GetComponentsInChildren<NetworkTransform>(true))
            transforms.Add(new TransformSyncState(item));

        if (IsOwner)
        {
            if (playerCamera != null)
            {
                cameraLocalPosition = playerCamera.transform.localPosition;
                cameraLocalRotation = playerCamera.transform.localRotation;
            }
            foreach (Renderer item in GetComponentsInChildren<Renderer>(true))
            {
                renderers[item] = item.enabled;
                item.enabled = false;
            }
        }
        FollowSeat();
    }

    public void LeaveSeat(Vector3 position, Quaternion rotation)
    {
        if (!IsSeated) return;
        if (IsOwner && seat.Vehicle.CanSimulate) seat.Vehicle.SettleAndBrake();
        seat = null;
        transform.SetPositionAndRotation(position, rotation);
        body.position = position;
        body.rotation = rotation;
        foreach (TransformSyncState state in transforms) state.Restore(IsOwner);
        transforms.Clear();
        foreach (var item in rigidbodyAutoState) if (item.Key != null) item.Key.AutoUpdateKinematicState = item.Value;
        rigidbodyAutoState.Clear();
        body.isKinematic = wasKinematic;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        foreach (var item in colliders) if (item.Key != null) item.Key.enabled = item.Value;
        colliders.Clear();
        // Reapply after enabling colliders; disabling them can reset ignore state.
        SetSeatCollisionsIgnored();
        restoreSeatCollisionsAfter = Time.unscaledTime + 0.5f;
        foreach (var item in renderers) if (item.Key != null) item.Key.enabled = item.Value;
        renderers.Clear();
        if (IsOwner && playerCamera != null)
        {
            playerCamera.transform.localPosition = cameraLocalPosition;
            playerCamera.transform.localRotation = cameraLocalRotation;
            if (cameraLook != null) cameraLook.ResetLookToCurrentPose();
        }
        foreach (var item in suppressed) if (item.Key != null) item.Key.enabled = item.Value;
        suppressed.Clear();
    }

    private void Update()
    {
        if (!IsOwner || !IsSeated || NetworkSessionMenu.IsGameMenuOpen || !focused) return;
        if (Time.frameCount > enteredFrame && inputs.Player.Interact.WasPressedThisFrame()) seat.RequestExit();
        Vector2 look = inputs.Player.Look.ReadValue<Vector2>();
        yaw = Mathf.Clamp(yaw + look.x * (cameraLook != null ? cameraLook.sensitivityX : 0.15f), -100f, 100f);
        pitch = Mathf.Clamp(pitch - look.y * (cameraLook != null ? cameraLook.sensitivityY : 0.15f), -60f, 60f);
    }

    private void FixedUpdate()
    {
        TryRestoreSeatCollisions();
        if (TryGetVehicleInput(out VehicleInputState input))
        {
            if (!focused || NetworkSessionMenu.IsGameMenuOpen) seat.Vehicle.ClearInput();
            else seat.Vehicle.TrySetInput(input);
        }
    }

    private void SetSeatCollisionsIgnored()
    {
        foreach (SeatCollisionPair pair in seatCollisionPairs)
            if (pair.Player != null && pair.Truck != null)
                Physics.IgnoreCollision(pair.Player, pair.Truck, true);
    }

    private void TryRestoreSeatCollisions()
    {
        if (IsSeated || seatCollisionPairs.Count == 0 || Time.unscaledTime < restoreSeatCollisionsAfter) return;
        // Bounds are deliberately conservative: don't restore a pair while any
        // part of the driver still overlaps the truck on this peer.
        foreach (SeatCollisionPair pair in seatCollisionPairs)
            if (pair.Player != null && pair.Truck != null && pair.Player.enabled && pair.Truck.enabled &&
                pair.Player.bounds.Intersects(pair.Truck.bounds)) return;
        RestoreSeatCollisions();
    }

    private void RestoreSeatCollisions()
    {
        foreach (SeatCollisionPair pair in seatCollisionPairs)
            if (pair.Player != null && pair.Truck != null)
                Physics.IgnoreCollision(pair.Player, pair.Truck, false);
        seatCollisionPairs.Clear();
    }

    private readonly struct SeatCollisionPair
    {
        public readonly Collider Player;
        public readonly Collider Truck;
        public SeatCollisionPair(Collider player, Collider truck) { Player = player; Truck = truck; }
    }

    public bool TryGetVehicleInput(out VehicleInputState input)
    {
        input = new VehicleInputState(0f, 0f, 1f);
        if (!IsSpawned || !IsOwner || !IsSeated || seat.DriverClientId != OwnerClientId ||
            seat.OwnerClientId != OwnerClientId || !seat.Vehicle.CanSimulate) return false;
        if (!focused || NetworkSessionMenu.IsGameMenuOpen) return true;
        Vector2 move = inputs.Player.Move.ReadValue<Vector2>();
        input = new VehicleInputState(move.x, move.y, 0f, inputs.Player.Jump.IsPressed());
        return true;
    }

    private void LateUpdate()
    {
        if (IsSeated) FollowSeat();
    }

    private void FollowSeat()
    {
        // Disabling NetworkTransform alone does not unregister NGO's network tick.
        foreach (TransformSyncState state in transforms) state.Suspend();
        Vector3 centerOffset = Vector3.Scale(capsule.center, transform.lossyScale);
        transform.SetPositionAndRotation(seat.SeatMarker.position - seat.SeatMarker.rotation * centerOffset,
            seat.SeatMarker.rotation);
        body.position = transform.position;
        body.rotation = transform.rotation;
        if (IsOwner && playerCamera != null)
            playerCamera.transform.SetPositionAndRotation(seat.CameraMarker.position,
                seat.CameraMarker.rotation * Quaternion.Euler(pitch, yaw, 0f));
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        focused = hasFocus;
        if (!hasFocus && IsOwner && IsSeated) seat.Vehicle.ClearInput();
    }

    public bool IsExitClear(Vector3 position, Quaternion rotation, LayerMask layers)
    {
        // Match the actual vertical capsule, including the normal player's feet-based pivot.
        if (capsule == null || capsule.direction != 1) return false;
        Vector3 scale = transform.lossyScale;
        float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        float halfSegment = Mathf.Max(0f, capsule.height * Mathf.Abs(scale.y) * 0.5f - radius);
        Vector3 center = position + rotation * Vector3.Scale(capsule.center, scale);
        Vector3 up = rotation * Vector3.up * halfSegment;
        foreach (Collider hit in Physics.OverlapCapsule(center + up, center - up, radius, layers, QueryTriggerInteraction.Ignore))
        {
            if (!hit.transform.IsChildOf(transform)) return false;
        }
        return true;
    }

    public Vector3 GroundExitPosition(Vector3 markerPosition, LayerMask layers)
    {
        if (capsule == null) return markerPosition;
        // Suspension moves the marker up/down. Keep the capsule just above the nearby floor.
        float scaleY = Mathf.Abs(transform.lossyScale.y);
        float bottomOffset = (capsule.center.y - capsule.height * 0.5f) * scaleY;
        float searchDistance = capsule.height * scaleY + 0.75f;
        if (Physics.Raycast(markerPosition + Vector3.up * 0.25f, Vector3.down, out RaycastHit hit,
                searchDistance, layers, QueryTriggerInteraction.Ignore))
        {
            float groundedY = hit.point.y - bottomOffset + 0.05f;
            if (Mathf.Abs(groundedY - markerPosition.y) < searchDistance) markerPosition.y = groundedY;
        }
        return markerPosition;
    }

    public static void ShowNotice(string text)
    {
        notice = text;
        noticeUntil = Time.unscaledTime + 3f;
    }

    private void OnGUI()
    {
        if (!IsOwner) return;
        if (Time.unscaledTime < noticeUntil)
            GUI.Box(new Rect(Screen.width * 0.5f - 220f, Screen.height - 110f, 440f, 35f), notice);
    }

    private sealed class TransformSyncState
    {
        private readonly NetworkTransform target;
        private readonly bool px, py, pz, rx, ry, rz, sx, sy, sz, quaternion;
        public TransformSyncState(NetworkTransform value)
        {
            target = value;
            px = value.SyncPositionX; py = value.SyncPositionY; pz = value.SyncPositionZ;
            rx = value.SyncRotAngleX; ry = value.SyncRotAngleY; rz = value.SyncRotAngleZ;
            sx = value.SyncScaleX; sy = value.SyncScaleY; sz = value.SyncScaleZ;
            quaternion = value.UseQuaternionSynchronization;
            Suspend();
        }
        public void Suspend()
        {
            if (target == null) return;
            target.SyncPositionX = target.SyncPositionY = target.SyncPositionZ = false;
            target.SyncRotAngleX = target.SyncRotAngleY = target.SyncRotAngleZ = false;
            target.SyncScaleX = target.SyncScaleY = target.SyncScaleZ = false;
            target.UseQuaternionSynchronization = false;
        }
        public void Restore(bool owner)
        {
            if (target == null) return;
            target.SyncPositionX = px; target.SyncPositionY = py; target.SyncPositionZ = pz;
            target.SyncRotAngleX = rx; target.SyncRotAngleY = ry; target.SyncRotAngleZ = rz;
            target.SyncScaleX = sx; target.SyncScaleY = sy; target.SyncScaleZ = sz;
            target.UseQuaternionSynchronization = quaternion;
            if (owner && target.IsOwner && target.IsSpawned && !target.IsServerAuthoritative())
                target.Teleport(target.transform.position, target.transform.rotation, target.transform.localScale);
        }
    }
}
