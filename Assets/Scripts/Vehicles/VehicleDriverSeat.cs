using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>Infrequent, atomic seat transitions, including the agreed exit pose.</summary>
public struct VehicleSeatState : INetworkSerializable, IEquatable<VehicleSeatState>
{
    public ulong Driver;
    public uint Revision;
    public Vector3 ExitPosition;
    public Quaternion ExitRotation;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Driver);
        serializer.SerializeValue(ref Revision);
        serializer.SerializeValue(ref ExitPosition);
        serializer.SerializeValue(ref ExitRotation);
    }

    public bool Equals(VehicleSeatState other) => Driver == other.Driver && Revision == other.Revision &&
        ExitPosition.Equals(other.ExitPosition) && ExitRotation.Equals(other.ExitRotation);
}

[DefaultExecutionOrder(-200)]
[DisallowMultipleComponent]
[RequireComponent(typeof(VehicleController))]
public sealed class VehicleDriverSeat : NetworkBehaviour, IInteractable
{
    public const ulong NoDriver = ulong.MaxValue;
    [SerializeField] private Transform seatMarker;
    [SerializeField] private Transform cameraMarker;
    [SerializeField] private Transform entryMarker;
    [SerializeField] private Transform exitMarker;
    [SerializeField, Min(0.1f)] private float entryRange = 3f;
    [SerializeField, Min(0f)] private float maximumSeatChangeSpeedKph = 2f;
    [SerializeField] private LayerMask exitBlockingLayers = ~0;

    private readonly NetworkVariable<VehicleSeatState> state = new(
        new VehicleSeatState { Driver = NoDriver, ExitRotation = Quaternion.identity },
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private VehicleController vehicle;
    private PlayerVehicleDriver visibleDriver;
    private double nextSeatChangeTime;

    public ulong DriverClientId => state.Value.Driver;
    public bool IsOccupied => DriverClientId != NoDriver;
    public Transform SeatMarker => seatMarker;
    public Transform CameraMarker => cameraMarker;
    public VehicleController Vehicle => vehicle;

    // Kitchen colliders share the truck hierarchy, but are not driving controls.
    public bool IsEntryInteractionCollider(Collider candidate) => enabled && entryMarker != null &&
        candidate != null && candidate.transform.IsChildOf(entryMarker);

    private void Awake() => vehicle = GetComponent<VehicleController>();

    public override void OnNetworkSpawn()
    {
        if (seatMarker == null || cameraMarker == null || entryMarker == null || exitMarker == null)
        {
            Debug.LogError("VehicleDriverSeat requires seat, camera, entry and exit markers.", this);
            enabled = false;
            return;
        }
        NetworkObject.DontDestroyWithOwner = true;
        state.OnValueChanged += OnStateChanged;
        if (IsServer) NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        ReconcileDriver();
    }

    public override void OnNetworkDespawn()
    {
        state.OnValueChanged -= OnStateChanged;
        if (NetworkManager != null) NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        if (visibleDriver != null) visibleDriver.LeaveSeat(transform.position + transform.right * 3f, Quaternion.identity);
        visibleDriver = null;
    }

    private void OnStateChanged(VehicleSeatState previous, VehicleSeatState current) => ReconcileDriver();

    private void Update()
    {
        if (IsSpawned) ReconcileDriver(); // Also resolves players that spawn after a late-join seat snapshot.
    }

    private void FixedUpdate()
    {
        if (IsSpawned && IsOwner && !IsOccupied) vehicle.ClearInput();
    }

    private void ReconcileDriver()
    {
        if (visibleDriver != null && visibleDriver.OwnerClientId != DriverClientId)
        {
            visibleDriver.LeaveSeat(state.Value.ExitPosition, state.Value.ExitRotation);
            visibleDriver = null;
        }
        if (visibleDriver == null && IsOccupied)
        {
            visibleDriver = ResolvePlayer(DriverClientId);
            if (visibleDriver != null) visibleDriver.EnterSeat(this);
        }
    }

    private PlayerVehicleDriver ResolvePlayer(ulong clientId)
    {
        if (NetworkManager == null) return null;
        foreach (NetworkObject candidate in NetworkManager.SpawnManager.SpawnedObjectsList)
        {
            if (!candidate.IsPlayerObject || candidate.OwnerClientId != clientId) continue;
            PlayerVehicleDriver driver = candidate.GetComponentInChildren<PlayerVehicleDriver>(true);
            if (driver != null && driver.NetworkObject == candidate) return driver;
        }
        return null;
    }

    public void Interact(PlayerInteraction interactor)
    {
        if (!IsSpawned || !enabled || !interactor.IsOwner) return;
        RequestEnterServerRpc();
    }

    public void RequestExit()
    {
        if (!IsSpawned || DriverClientId != NetworkManager.LocalClientId) return;
        RequestExitServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestEnterServerRpc(RpcParams rpc = default)
    {
        ulong sender = rpc.Receive.SenderClientId;
        PlayerVehicleDriver player = ResolvePlayer(sender);
        if (!enabled || player == null) return;
        if (IsOccupied) { Reject(sender, "The driver's seat is occupied."); return; }
        if (player.IsSeated) { Reject(sender, "You are already seated."); return; }
        if (player.TryGetComponent(out PlayerPickup pickup) && pickup.IsHoldingItem())
        { Reject(sender, "Put down your item before driving."); return; }
        if (Vector3.Distance(player.transform.position, entryMarker.position) > entryRange)
        { Reject(sender, "Move closer to the driver's door."); return; }
        if (vehicle.ObservedSpeedKph >= maximumSeatChangeSpeedKph || Time.unscaledTimeAsDouble < nextSeatChangeTime)
        { Reject(sender, "Wait for the truck to stop."); return; }

        vehicle.SettleAndBrake();
        VehicleSeatState next = state.Value;
        next.Driver = sender;
        next.Revision++;
        state.Value = next;
        NetworkObject.ChangeOwnership(sender);
        nextSeatChangeTime = Time.unscaledTimeAsDouble + 0.5;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestExitServerRpc(RpcParams rpc = default)
    {
        ulong sender = rpc.Receive.SenderClientId;
        if (sender != DriverClientId || sender != OwnerClientId) return;
        PlayerVehicleDriver player = ResolvePlayer(sender);
        if (player == null) return;
        if (Time.unscaledTimeAsDouble < nextSeatChangeTime)
        { Reject(sender, "Wait a moment before leaving the seat."); return; }

        Quaternion rotation = Quaternion.Euler(0f, exitMarker.eulerAngles.y, 0f);
        Vector3 exitPosition = player.GroundExitPosition(exitMarker.position, exitBlockingLayers);
        if (!player.IsExitClear(exitPosition, rotation, exitBlockingLayers))
        { Reject(sender, "The exit is blocked."); return; }
        ReleaseDriver(exitPosition, rotation);
    }

    private void ReleaseDriver(Vector3 position, Quaternion rotation)
    {
        // Reliable state and ownership changes replace continuous input/velocity RPCs.
        VehicleSeatState next = state.Value;
        next.Driver = NoDriver;
        next.Revision++;
        next.ExitPosition = position;
        next.ExitRotation = rotation;
        state.Value = next;
        NetworkObject.RemoveOwnership();
        vehicle.SettleAndBrake();
        nextSeatChangeTime = Time.unscaledTimeAsDouble + 0.5;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (IsServer && IsSpawned && clientId == DriverClientId)
            ReleaseDriver(exitMarker.position, Quaternion.Euler(0f, exitMarker.eulerAngles.y, 0f));
    }

    private void Reject(ulong clientId, string reason)
    {
        NoticeClientRpc(reason, new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        });
    }

    [ClientRpc]
    private void NoticeClientRpc(string reason, ClientRpcParams rpc = default) => PlayerVehicleDriver.ShowNotice(reason);
}
