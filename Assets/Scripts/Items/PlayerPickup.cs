using Unity.Netcode;
using UnityEngine;


public class PlayerPickup : NetworkBehaviour
{
    [Header("Pickup Settings")]
    public float pickupRange = 2.5f;

    [Header("References")]
    public Transform holdPoint;     
    public Camera playerCamera;

    [Header("Layer Mask")]
    public LayerMask pickableLayer; 

    private Item heldItem = null;
    private NetworkVariable<ulong> heldItemNetId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private InputSystem_Actions inputs;


    private bool IsHoldingItemLocally => heldItemNetId.Value != ulong.MaxValue;


    void Awake()
    {
        inputs = new InputSystem_Actions();
    }

    public override void OnNetworkSpawn()
    {

        if (!IsOwner && playerCamera != null)
            playerCamera.gameObject.SetActive(false);
    }

    void Update()
    {

        if (IsServer && heldItem != null)
            heldItem.FollowHoldPoint(holdPoint);


        if (!IsOwner) return;

        if (inputs.Player.PickUp.WasPressedThisFrame())
        {
            if (!IsHoldingItemLocally)
            {

                if (TryGetItemInSight(out ulong targetId))
                    RequestPickUpServerRpc(targetId);
            }
            else
            {
                if (TryGetItemInSight(out ulong targetId))
                    RequestSwapServerRpc(targetId);
            }
        }

        if (inputs.Player.Drop.WasPressedThisFrame() && IsHoldingItemLocally)
            RequestDropServerRpc();
    }


    [ServerRpc]
    void RequestPickUpServerRpc(ulong networkObjectId)
    {
        if (!TryResolveItem(networkObjectId, out Item target)) return;
        PerformPickUp(target);
    }

    [ServerRpc]
    void RequestSwapServerRpc(ulong networkObjectId)
    {
        if (!TryResolveItem(networkObjectId, out Item target)) return;
        PerformDrop();
        PerformPickUp(target);
    }

    [ServerRpc]
    void RequestDropServerRpc()
    {
        PerformDrop();
    }

    bool TryResolveItem(ulong networkObjectId, out Item item)
    {
        item = null;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects
                .TryGetValue(networkObjectId, out var netObj))
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

        // Distance validation — small tolerance for network latency.
        float dist = Vector3.Distance(transform.position, item.transform.position);
        if (dist > pickupRange + 1f)
        {
            Debug.LogWarning($"[Server] Player too far from item (dist={dist:F2}). Rejecting pickup.");
            return false;
        }

        return true;
    }

    void PerformPickUp(Item item)
    {
        heldItem = item;
        heldItem.PickUp();

        heldItemNetId.Value = heldItem.NetworkObject.NetworkObjectId;

        Debug.Log($"[Server] {gameObject.name} picked up: {heldItem.itemName}");
    }

    void PerformDrop()
    {
        if (heldItem == null) return;

        Vector3 dropPos = transform.position + transform.forward * 1f;
        heldItem.Drop(dropPos);
        Debug.Log($"[Server] {gameObject.name} dropped: {heldItem.itemName}");

        heldItem = null;

        // Clear the NetworkVariable so the owner client knows the hand is empty.
        heldItemNetId.Value = ulong.MaxValue;
    }


    bool TryGetItemInSight(out ulong networkObjectId)
    {
        networkObjectId = default;

        Ray ray = playerCamera.ScreenPointToRay(
            new Vector3(Screen.width / 2f, Screen.height / 2f));

        if (!Physics.Raycast(ray, out RaycastHit hit, pickupRange, pickableLayer))
            return false;

        var netObj = hit.collider.GetComponent<NetworkObject>();
        if (netObj == null) return false;

        if (hit.collider.GetComponent<Item>() == null) return false;

        networkObjectId = netObj.NetworkObjectId;
        return true;
    }


    public Item GetHeldItem() => heldItem;


    public bool IsHoldingItem() => IsHoldingItemLocally;
    void OnEnable() => inputs.Player.Enable();
    void OnDisable() => inputs.Player.Disable();
}