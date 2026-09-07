using Unity.Netcode;
using UnityEngine;


public class PlayerPickup : NetworkBehaviour
{
    private const ulong NoItem = ulong.MaxValue;

    [Header("Pickup Settings")]
    public float pickupRange = 2.5f;

    [Header("Throwing")]
    [SerializeField, Min(0f)] private float throwSpeed = 8f;

    [Header("References")]
    public Transform holdPoint;
    public Camera playerCamera;

    [Header("Layer Mask")]
    public LayerMask pickableLayer;

    private Item heldItem;

    private NetworkVariable<ulong> heldItemNetId = new NetworkVariable<ulong>(
        NoItem,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private InputSystem_Actions inputs;

    private bool IsHoldingItemLocally => heldItemNetId.Value != NoItem;
    public bool IsCarryModifierPressed => inputs.Player.Sprint.IsPressed();

    private void Awake()
    {
        inputs = new InputSystem_Actions();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner && playerCamera != null)
        {
            if (TryGetComponent(out PlayerVehicleDriver driver))
            {
                // Keep the driver's camera behaviour in NGO's spawn layout on every peer.
                playerCamera.enabled = false;
                if (playerCamera.TryGetComponent(out AudioListener listener)) listener.enabled = false;
            }
            else
            {
                playerCamera.gameObject.SetActive(false);
            }
        }
    }

    private readonly ItemPlacementPreview placementPreview = new();
    private bool isAimingPlacement;
    private bool applicationFocused = true;

    private void Update()
    {
        if (!IsOwner || !IsSpawned || NetworkSessionMenu.IsGameMenuOpen || !applicationFocused)
        {
            isAimingPlacement = false;
            placementPreview.Hide();
            return;
        }
        if (!IsHoldingItemLocally || !TryResolveHeldItem() || playerCamera == null)
        {
            isAimingPlacement = false;
            placementPreview.Clear();
            return;
        }
        if (inputs.Player.Throw.WasPressedThisFrame())
        {
            isAimingPlacement = false;
            placementPreview.Hide();
            RequestThrowServerRpc(heldItemNetId.Value, playerCamera.transform.forward);
            return;
        }
        if (inputs.Player.Drop.WasPressedThisFrame()) isAimingPlacement = true;
        bool releasePlacement = isAimingPlacement && inputs.Player.Drop.WasReleasedThisFrame();
        if (!isAimingPlacement || (!inputs.Player.Drop.IsPressed() && !releasePlacement))
        {
            isAimingPlacement = false;
            placementPreview.Hide();
            return;
        }
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f));
        bool hasPose = ItemPlacement.TryFindPose(heldItem, transform, ray, pickupRange,
            out Vector3 position, out Quaternion rotation, out bool valid, out NetworkObject surface);
        if (hasPose) placementPreview.Show(heldItem, position, rotation, valid);
        else placementPreview.Hide();
        if (releasePlacement)
        {
            isAimingPlacement = false;
            placementPreview.Hide();
            if (hasPose && valid)
                RequestPlaceServerRpc(heldItemNetId.Value,
                    surface != null ? surface.NetworkObjectId : TruckRelativePose.None,
                    surface != null ? surface.transform.InverseTransformPoint(ray.origin) : ray.origin,
                    surface != null ? surface.transform.InverseTransformDirection(ray.direction) : ray.direction,
                    surface != null ? surface.transform.InverseTransformPoint(position) : position);
        }
    }

    // Called only by PlayerInteraction, so F cannot pick up and interact twice.
    public void RequestPickUpItem(Item item)
    {
        if (!IsOwner || !enabled || NetworkSessionMenu.IsGameMenuOpen || IsHoldingItemLocally || item == null) return;
        RequestPickUpServerRpc(item.NetworkObjectId);
    }

    public bool TryDeliverFromView()
    {
        if (!IsOwner || !enabled || !IsHoldingItemLocally || !TryResolveHeldItem() ||
            !heldItem.TryGetComponent<ServingTray>(out _) || !TryGetCustomerInSight(out ulong customer)) return false;
        RequestDeliverServerRpc(customer);
        return true;
    }

    [ServerRpc]
    private void RequestPlaceServerRpc(ulong expectedItem, ulong supportId, Vector3 origin, Vector3 direction, Vector3 requestedPosition)
    {
        if (!enabled || expectedItem != heldItemNetId.Value || !TryResolveHeldItem()) return;
        if (TryGetComponent(out PlayerVehicleDriver driver) && driver.IsSeated) return;
        if (!ItemPlacement.IsFinite(origin) || !ItemPlacement.IsFinite(direction) || !ItemPlacement.IsFinite(requestedPosition)) return;
        NetworkObject requestedSurface = null;
        if (supportId != TruckRelativePose.None)
        {
            if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(supportId, out requestedSurface)) return;
            VehicleKitchen kitchen = requestedSurface.GetComponentInParent<VehicleKitchen>();
            if (kitchen == null && requestedSurface.TryGetComponent(out Item supportItem)) kitchen = supportItem.AttachedKitchen;
            if (kitchen == null) return;
            origin = requestedSurface.transform.TransformPoint(origin);
            direction = requestedSurface.transform.TransformDirection(direction);
            requestedPosition = requestedSurface.transform.TransformPoint(requestedPosition);
        }
        Physics.SyncTransforms();
        Vector3 eye = playerCamera != null ? playerCamera.transform.position : transform.position + Vector3.up;
        if (Vector3.Distance(eye, origin) > 1f || direction.sqrMagnitude < 0.5f || direction.sqrMagnitude > 1.5f) return;
        // Reject displaced ray origins that cross a wall between the host's eye and the client's eye.
        foreach (RaycastHit hit in Physics.RaycastAll(eye, origin - eye, Vector3.Distance(eye, origin),
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(heldItem.transform)) return;
        if (!ItemPlacement.TryFindPose(heldItem, transform, new Ray(origin, direction.normalized), pickupRange,
            out Vector3 position, out Quaternion rotation, out bool valid, out NetworkObject surface) || !valid) return;
        if (surface != requestedSurface || Vector3.Distance(position, requestedPosition) > 0.15f) return;
        Item placed = heldItem;
        if (ServerTryReleaseHeldItem(placed, position, rotation) && surface != null)
            placed.ServerAttachToSurface(surface, position, rotation);
    }
    [ServerRpc]
    private void RequestThrowServerRpc(ulong expectedItem, Vector3 direction)
    {
        if (!enabled || expectedItem != heldItemNetId.Value || !TryResolveHeldItem()) return;
        if (TryGetComponent(out PlayerVehicleDriver driver) && driver.IsSeated) return;
        if (!ItemPlacement.IsFinite(direction) || direction.sqrMagnitude < 0.5f || direction.sqrMagnitude > 1.5f) return;
        Item item = heldItem;
        Vector3 position = item.transform.position;
        Quaternion rotation = item.transform.rotation;
        // Keep the release at the hand, and reject throwing from inside/through a wall.
        if (!ItemPlacement.TryGetBounds(item, out Bounds bounds)) return;
        Vector3 scale = item.transform.lossyScale;
        Vector3 center = item.transform.TransformPoint(bounds.center);
        Vector3 extents = Vector3.Scale(bounds.extents,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        foreach (Collider overlap in Physics.OverlapBox(center, extents, rotation,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!overlap.transform.IsChildOf(transform) && !overlap.transform.IsChildOf(item.transform)) return;
        Vector3 eye = playerCamera != null ? playerCamera.transform.position : transform.position + Vector3.up;
        foreach (RaycastHit hit in Physics.RaycastAll(eye, center - eye, Vector3.Distance(eye, center),
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(item.transform)) return;
        if (ServerTryReleaseHeldItem(item, position, rotation))
        {
            Vector3 inherited = TryGetComponent(out PlayerTruckPassenger passenger) ? passenger.DepartureVelocity : Vector3.zero;
            item.ServerApplyThrow(direction.normalized * Mathf.Max(0f, throwSpeed) + inherited, transform);
        }
    }

    [ServerRpc]
    private void RequestPickUpServerRpc(ulong networkObjectId)
    {
        if (heldItemNetId.Value != NoItem) return;

        if (!TryResolveItem(networkObjectId, out Item target)) return;

        PerformPickUp(target);
    }

    private bool TryResolveItem(ulong networkObjectId, out Item item)
    {
        item = null;

        if (NetworkManager.Singleton == null)
        {
            return false;
        }

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject netObj))
        {
            Debug.LogWarning($"[Server] NetworkObject {networkObjectId} not found.");
            return false;
        }

        item = netObj.GetComponent<Item>();

        if (item == null)
        {
            Debug.LogWarning($"[Server] NetworkObject {networkObjectId} has no Item component.");
            return false;
        }

        if (item.IsHeld)
        {
            Debug.LogWarning($"[Server] Item {item.itemName} is already held.");
            return false;
        }

        float distance = Vector3.Distance(transform.position, item.transform.position);

        if (distance > pickupRange + 1f)
        {
            Debug.LogWarning($"[Server] Player too far from item. Distance: {distance:F2}");
            return false;
        }

        return true;
    }

    public bool ServerTryPickUpItem(Item item)
    {
        if (!IsServer) return false;

        return PerformPickUp(item);
    }

    private bool PerformPickUp(Item item)
    {
        if (!IsServer) return false;
        if (TryGetComponent(out PlayerVehicleDriver driver) && driver.IsSeated) return false;
        if (item == null) return false;
        if (heldItem != null) return false;
        if (item.TryGetComponent(out IngredientBox box) && !box.IsPurchased) return false;

        if (!PrepareItemForPickUp(item))
        {
            return false;
        }

        bool success = item.ServerStartHolding(this);

        if (!success)
        {
            Debug.LogWarning("[Server] Failed to start holding item.");
            return false;
        }

        heldItem = item;
        heldItemNetId.Value = item.NetworkObject.NetworkObjectId;

        Debug.Log($"[Server] {gameObject.name} picked up {item.itemName}");
        return true;
    }

    private bool PrepareItemForPickUp(Item item)
    {
        if (item == null) return false;

        FoodIngredient ingredient = item.GetComponent<FoodIngredient>();

        if (ingredient != null && ingredient.IsInFoodAssembly)
        {
            return false;
        }

        if (ingredient != null && ingredient.IsOnGrill)
        {
            Grill grill = ingredient.CurrentGrill;
            if (grill == null || !grill.ServerTryRemoveItem(ingredient))
            {
                return false;
            }
        }

        FoodAssemblyBase foodAssembly = item.GetComponent<FoodAssemblyBase>();

        if (foodAssembly != null && foodAssembly.IsOnServingTray)
        {
            return foodAssembly.ServerTryRemoveFromServingTray();
        }

        return true;
    }

    private bool TryResolveHeldItem()
    {
        if (heldItemNetId.Value == NoItem) return false;

        if (NetworkManager.Singleton == null) return false;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(heldItemNetId.Value, out NetworkObject netObj))
        {
            return false;
        }

        heldItem = netObj.GetComponent<Item>();

        return heldItem != null;
    }

    public Item GetHeldItem()
    {
        return heldItem;
    }

    public bool IsHoldingItem()
    {
        return IsHoldingItemLocally;
    }

    public bool ServerTryGetHeldItem(out Item item)
    {
        item = null;

        if (!IsServer) return false;

        if (heldItem == null)
        {
            TryResolveHeldItem();
        }

        item = heldItem;
        return item != null;
    }

    public bool ServerTryReleaseHeldItem(Item item, Vector3 releasePosition, Quaternion releaseRotation)
    {
        if (!IsServer) return false;
        if (item == null) return false;

        if (heldItem == null)
        {
            TryResolveHeldItem();
        }

        if (heldItem != item)
        {
            return false;
        }

        heldItem.ServerStopHolding(releasePosition, releaseRotation);

        heldItem = null;
        heldItemNetId.Value = NoItem;

        return true;
    }

    public bool ServerTryReleaseHeldItemPreserveWorldPose(Item item)
    {
        if (!IsServer) return false;
        if (item == null) return false;

        if (heldItem == null)
        {
            TryResolveHeldItem();
        }

        if (heldItem != item)
        {
            return false;
        }

        heldItem.ServerStopHoldingPreserveWorldPose();

        heldItem = null;
        heldItemNetId.Value = NoItem;

        return true;
    }

    private void OnEnable()
    {
        if (inputs != null)
        {
            inputs.Player.Enable();
        }
    }

    public override void OnNetworkDespawn() => placementPreview.Clear();

    public override void OnDestroy()
    {
        placementPreview.Clear();
        inputs?.Dispose();
        base.OnDestroy();
    }

    private void OnDisable()
    {
        isAimingPlacement = false;
        placementPreview.Clear();
        if (inputs != null)
        {
            inputs.Player.Disable();
        }
    }

    private void OnApplicationFocus(bool focused)
    {
        applicationFocused = focused;
        if (!focused)
        {
            isAimingPlacement = false;
            placementPreview.Hide();
        }
    }

    private bool TryGetCustomerInSight(out ulong customerNetId)
    {
        customerNetId = default;
        if (playerCamera == null) return false;

        Ray ray = playerCamera.ScreenPointToRay(
            new Vector3(Screen.width / 2f, Screen.height / 2f, 0f)
        );

        if (Physics.Raycast(ray, out RaycastHit hit, pickupRange * 2f))
            Debug.Log($"Raycast hit: {hit.collider.gameObject.name}");
        else
            Debug.Log("Raycast hit nothing");

        if (!Physics.Raycast(ray, out RaycastHit hit2, pickupRange * 2f)) return false;

        CustomerAI customer = hit2.collider.GetComponentInParent<CustomerAI>();
        Debug.Log($"Customer found: {customer != null}, State: {(customer != null ? customer.State.ToString() : "N/A")}");

        if (customer == null) return false;
        if (customer.State != CustomerAI.CustomerState.WaitingForFood) return false;

        customerNetId = customer.NetworkObject.NetworkObjectId;
        return true;
    }

    [ServerRpc]
    private void RequestDeliverServerRpc(ulong customerNetId)
    {

        if (heldItem == null)
            TryResolveHeldItem();

        if (heldItem == null || !heldItem.TryGetComponent(out ServingTray servingTray))
        {
            Debug.LogWarning("[Server] Player is not holding a tray.");
            return;
        }

        if (!servingTray.HasFood)
        {
            Debug.LogWarning("[Server] The serving tray has no assembled food.");
            return;
        }

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(customerNetId, out NetworkObject netObj))
        {
            Debug.LogWarning("[Server] Customer NetworkObject not found.");
            return;
        }

        CustomerAI customer = netObj.GetComponent<CustomerAI>();
        if (customer == null) return;
        if (customer.State != CustomerAI.CustomerState.WaitingForFood) return;

        float distance = Vector3.Distance(transform.position, customer.transform.position);
        if (distance > pickupRange * 2f + 1f)
        {
            Debug.LogWarning($"[Server] Player is too far from customer to deliver. Distance: {distance:F2}");
            return;
        }

        // Stop holding and place in front of customer
        Item tray = heldItem;
        heldItem = null;
        heldItemNetId.Value = NoItem;

        customer.ReceiveFood(tray);
    }
}
