using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Player pickup controller.
/// Held items locally attach to this player's holdPoint while held.
/// </summary>
public class PlayerPickup : NetworkBehaviour
{
    private const ulong NoItem = ulong.MaxValue;

    [Header("Pickup Settings")]
    public float pickupRange = 2.5f;

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

    private void Awake()
    {
        inputs = new InputSystem_Actions();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner && playerCamera != null)
        {
            playerCamera.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (!IsOwner) return;

        if (inputs.Player.PickUp.WasPressedThisFrame())
        {
            if (!IsHoldingItemLocally)
            {
                if (TryGetItemInSight(out ulong targetId))
                {
                    RequestPickUpServerRpc(targetId);
                }
            }
            else
            {
                if (TryGetItemInSight(out ulong targetId))
                {
                    RequestSwapServerRpc(targetId);
                }
            }
        }

        if (inputs.Player.Drop.WasPressedThisFrame() && IsHoldingItemLocally)
        {
            RequestDropServerRpc();
        }
    }

    [ServerRpc]
    private void RequestPickUpServerRpc(ulong networkObjectId)
    {
        if (heldItem != null) return;

        if (!TryResolveItem(networkObjectId, out Item target)) return;

        PerformPickUp(target);
    }

    [ServerRpc]
    private void RequestSwapServerRpc(ulong networkObjectId)
    {
        if (!TryResolveItem(networkObjectId, out Item target)) return;

        if (heldItem == target)
        {
            return;
        }

        PerformDrop();
        PerformPickUp(target);
    }

    [ServerRpc]
    private void RequestDropServerRpc()
    {
        PerformDrop();
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

    private void PerformPickUp(Item item)
    {
        if (!IsServer) return;
        if (item == null) return;
        if (heldItem != null) return;

        if (!PrepareItemForPickUp(item))
        {
            return;
        }

        bool success = item.ServerStartHolding(this);

        if (!success)
        {
            Debug.LogWarning("[Server] Failed to start holding item.");
            return;
        }

        heldItem = item;
        heldItemNetId.Value = item.NetworkObject.NetworkObjectId;

        Debug.Log($"[Server] {gameObject.name} picked up {item.itemName}");
    }

    private bool PrepareItemForPickUp(Item item)
    {
        if (item == null) return false;

        FoodAssemblyBase foodAssembly = item.GetComponent<FoodAssemblyBase>();

        if (foodAssembly != null && foodAssembly.IsOnServingTray)
        {
            return foodAssembly.ServerTryRemoveFromServingTray();
        }

        return true;
    }

    private void PerformDrop()
    {
        if (!IsServer) return;

        if (heldItem == null)
        {
            TryResolveHeldItem();
        }

        if (heldItem == null) return;

        Vector3 dropPosition = GetDropPosition();
        Quaternion dropRotation = Quaternion.LookRotation(transform.forward, Vector3.up);

        heldItem.ServerStopHolding(dropPosition, dropRotation);

        Debug.Log($"[Server] {gameObject.name} dropped {heldItem.itemName}");

        heldItem = null;
        heldItemNetId.Value = NoItem;
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

    private Vector3 GetDropPosition()
    {
        Vector3 basePosition;

        if (holdPoint != null)
        {
            basePosition = holdPoint.position;
        }
        else
        {
            basePosition = transform.position + Vector3.up;
        }

        return basePosition + transform.forward * 1f;
    }

    private bool TryGetItemInSight(out ulong networkObjectId)
    {
        networkObjectId = default;

        if (playerCamera == null)
        {
            Debug.LogWarning("[Client] Player camera is missing.");
            return false;
        }

        Ray ray = playerCamera.ScreenPointToRay(
            new Vector3(Screen.width / 2f, Screen.height / 2f, 0f)
        );

        RaycastHit[] hits = Physics.RaycastAll(
            ray,
            pickupRange,
            pickableLayer,
            QueryTriggerInteraction.Ignore
        );

        if (hits.Length == 0)
        {
            return false;
        }

        float closestDistance = float.MaxValue;
        NetworkObject closestNetObj = null;

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].distance >= closestDistance)
            {
                continue;
            }

            NetworkObject netObj = hits[i].collider.GetComponentInParent<NetworkObject>();

            if (netObj == null)
            {
                continue;
            }

            Item item = netObj.GetComponent<Item>();

            if (item == null || item.IsHeld)
            {
                continue;
            }

            closestNetObj = netObj;
            closestDistance = hits[i].distance;
        }

        if (closestNetObj == null)
        {
            return false;
        }

        networkObjectId = closestNetObj.NetworkObjectId;
        return true;
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

    private void OnDisable()
    {
        if (inputs != null)
        {
            inputs.Player.Disable();
        }
    }
}
