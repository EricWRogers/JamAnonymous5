using Unity.Netcode;
using UnityEngine;

/// <summary>Server-owned service lifecycle. Ownership cannot pass to a driver until fully closed.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(VehicleController))]
public sealed class FoodTruckShop : NetworkBehaviour
{
    public enum ShopState : byte { Closed, Opening, Open, Closing }
    [SerializeField] private Transform orderShutter;
    [SerializeField] private Transform pickupShutter;
    [SerializeField] private FoodTruckShopControl control;
    [SerializeField, Min(.1f)] private float animationDuration = 1.2f;
    [SerializeField, Min(0f)] private float maximumOpeningSpeed = .1f;
    [SerializeField, Min(0f)] private float maximumOpeningAngularSpeed = .05f;
    private readonly NetworkVariable<ShopState> state = new(ShopState.Closed,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<double> transitionStart = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> transitionFrom = new(0,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private VehicleController vehicle;
    private RegisterTest register;
    private RigidbodyConstraints savedConstraints;
    private bool constrained;
    private Vector3 parkedPosition;
    private Quaternion parkedRotation;
    public ShopState State => state.Value;
    public bool IsOpen => IsSpawned && State == ShopState.Open;
    public bool DriveLocked => IsSpawned && State != ShopState.Closed;
    public float ShutterOpenness
    {
        get
        {
            if (State == ShopState.Open) return 1f;
            if (State == ShopState.Closed) return 0f;
            float t = Mathf.Clamp01((float)(NetworkManager.ServerTime.Time - transitionStart.Value) / animationDuration);
            return Mathf.Lerp(transitionFrom.Value, State == ShopState.Opening ? 1f : 0f, t);
        }
    }
    public bool IsStationary => vehicle != null &&
        vehicle.VehicleBody.linearVelocity.sqrMagnitude <= maximumOpeningSpeed * maximumOpeningSpeed &&
        vehicle.VehicleBody.angularVelocity.sqrMagnitude <= maximumOpeningAngularSpeed * maximumOpeningAngularSpeed &&
        vehicle.ObservedSpeedKph <= maximumOpeningSpeed * 3.6f;
    public Transform ControlTransform => control != null ? control.transform : transform;
    private void Awake() => vehicle = GetComponent<VehicleController>();
    public void Bind(RegisterTest target)
    {
        register = target;
        target.BindShop(this);
    }
    public override void OnNetworkSpawn()
    {
        state.OnValueChanged += OnStateChanged;
        ApplyPose();
    }
    private void OnStateChanged(ShopState previous, ShopState current) => ApplyPose();
    public void RequestOpen(bool open) { if (IsSpawned) SetOpenServerRpc(open); }
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void SetOpenServerRpc(bool open, RpcParams rpc = default)
    {
        if (!NetworkManager.ConnectedClients.TryGetValue(rpc.Receive.SenderClientId, out var client) ||
            client.PlayerObject == null) return;
        var player = client.PlayerObject.GetComponentInChildren<PlayerInteraction>();
        if (player == null) return;
        Vector3 position = player.transform.position;
        if (Vector3.Distance(position, ControlTransform.position) > 4f &&
            (register == null || Vector3.Distance(position, register.transform.position) > 4f)) return;
        if (open && !ServerTryOpen())
            NoticeClientRpc("Stop the truck and leave the driver's seat before opening.", new ClientRpcParams
            { Send = new ClientRpcSendParams { TargetClientIds = new[] { rpc.Receive.SenderClientId } } });
        else if (!open) ServerClose();
    }
    [ClientRpc] private void NoticeClientRpc(string message, ClientRpcParams rpc = default) => PlayerVehicleDriver.ShowNotice(message);
    public bool ServerTryOpen()
    {
        if (!IsServer || !IsSpawned || State != ShopState.Closed || register == null ||
            GameManager.Instance == null || !GameManager.Instance.IsSpawned || !IsStationary ||
            (TryGetComponent<VehicleDriverSeat>(out var seat) && seat.IsOccupied)) return false;
        // Owner-authoritative driving is retained while closed. Parked service always belongs to the server.
        NetworkObject.RemoveOwnership();
        parkedPosition = transform.position;
        parkedRotation = transform.rotation;
        savedConstraints = vehicle.VehicleBody.constraints;
        constrained = true;
        vehicle.SettleAndBrake();
        vehicle.VehicleBody.constraints = RigidbodyConstraints.FreezeAll;
        GameManager.Instance.ServerBeginShopShift();
        BeginTransition(ShopState.Opening);
        return true;
    }
    public void ServerClose()
    {
        if (!IsServer || !IsSpawned || State == ShopState.Closed || State == ShopState.Closing) return;
        // Gate admission/submission before invoking any customer callbacks.
        BeginTransition(ShopState.Closing);
        DrainService();
    }
    private void DrainService()
    {
        if (register != null) register.ServerDismissCustomers();
        if (GameManager.Instance != null && GameManager.Instance.IsSpawned)
            GameManager.Instance.ServerFinishShopShift();
    }
    private void BeginTransition(ShopState next)
    {
        transitionFrom.Value = ShutterOpenness;
        transitionStart.Value = NetworkManager.ServerTime.Time;
        state.Value = next;
    }
    private void Update()
    {
        if (!IsSpawned) return;
        if (IsServer && (State == ShopState.Opening || State == ShopState.Closing) &&
            NetworkManager.ServerTime.Time - transitionStart.Value >= animationDuration)
        {
            bool opened = State == ShopState.Opening;
            state.Value = opened ? ShopState.Open : ShopState.Closed;
            if (!opened) ReleaseParking();
        }
        ApplyPose();
    }
    private void FixedUpdate()
    {
        if (!IsServer || !DriveLocked) return;
        vehicle.VehicleBody.position = parkedPosition;
        vehicle.VehicleBody.rotation = parkedRotation;
        if (!vehicle.VehicleBody.isKinematic)
        {
            vehicle.VehicleBody.linearVelocity = Vector3.zero;
            vehicle.VehicleBody.angularVelocity = Vector3.zero;
        }
    }
    private void ApplyPose()
    {
        float openness = IsSpawned ? ShutterOpenness : 0;
        Quaternion rotation = Quaternion.Euler(0, 0, 100f * openness);
        if (orderShutter != null) orderShutter.localRotation = rotation;
        if (pickupShutter != null) pickupShutter.localRotation = rotation;
        if (control != null) control.ShowState(State);
    }
    private void ReleaseParking()
    {
        if (constrained && vehicle != null)
        {
            vehicle.VehicleBody.constraints = savedConstraints;
            vehicle.ClearInput();
        }
        constrained = false;
    }
    public override void OnNetworkDespawn()
    {
        state.OnValueChanged -= OnStateChanged;
        if (IsServer && State != ShopState.Closed && NetworkManager != null && !NetworkManager.ShutdownInProgress)
            DrainService();
        ReleaseParking();
        if (register != null) register.UnbindShop(this);
        register = null;
        base.OnNetworkDespawn();
    }
}
