using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked player pickup controller.
///
/// Responsibility split:
///   Owner (client)  — reads input, performs the local raycast, sends ServerRpcs.
///   Server / Host   — validates the request, mutates item state, ticks FollowHoldPoint.
///   Other clients   — do nothing; item position is replicated via NetworkTransform.
///
/// Required setup:
///   • PlayerPickup sits on a NetworkObject (the player prefab).
///   • Each Item prefab has: NetworkObject, NetworkTransform, Rigidbody, Collider, Item script.
///   • Items must be spawned by the server (NetworkObject.Spawn) before they can be picked up.
///   • The "Pickable" LayerMask and the holdPoint Transform are assigned in the Inspector.
/// </summary>
public class PlayerPickup : NetworkBehaviour
{
    [Header("Pickup Settings")]
    public float pickupRange = 2.5f;

    [Header("References")]
    public Transform holdPoint;     // Empty child GameObject in front of camera/player
    public Camera playerCamera;

    [Header("Layer Mask")]
    public LayerMask pickableLayer; // Set to your "Pickable" layer in Inspector

    // Server-only: the actual Item reference used for FollowHoldPoint each tick.
    private Item heldItem = null;

    // Replicated to all clients so the owning client knows whether it is holding
    // something, allowing it to correctly gate drop input and swap checks.
    // ulong.MaxValue == "no item held".
    private NetworkVariable<ulong> heldItemNetId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private InputSystem_Actions inputs;

    // Convenience: readable on the owning client AND the server.
    private bool IsHoldingItemLocally => heldItemNetId.Value != ulong.MaxValue;

    // -------------------------------------------------------------------------
    // Lifecycle
    // -------------------------------------------------------------------------

    void Awake()
    {
        inputs = new InputSystem_Actions();
    }

    public override void OnNetworkSpawn()
    {
        // Disable the camera for every player except the local owner so there
        // is only one active camera per client.
        if (!IsOwner && playerCamera != null)
            playerCamera.gameObject.SetActive(false);
    }

    void Update()
    {
        // --- Server tick: keep the held item welded to this player's hold point ---
        // Runs on the host for every player object.
        // NetworkTransform on the item replicates the position to all clients.
        if (IsServer && heldItem != null)
            heldItem.FollowHoldPoint(holdPoint);

        // --- Input: only the owning client should read input ---
        if (!IsOwner) return;

        if (inputs.Player.PickUp.WasPressedThisFrame())
        {
            if (!IsHoldingItemLocally)
            {
                // Not holding anything — try to pick up whatever is in sight.
                if (TryGetItemInSight(out ulong targetId))
                    RequestPickUpServerRpc(targetId);
            }
            else
            {
                // Already holding — try to swap with whatever is in sight.
                if (TryGetItemInSight(out ulong targetId))
                    RequestSwapServerRpc(targetId);
            }
        }

        // IsHoldingItemLocally reads the NetworkVariable, so this works on the
        // owning client even though heldItem itself only exists on the server.
        if (inputs.Player.Drop.WasPressedThisFrame() && IsHoldingItemLocally)
            RequestDropServerRpc();
    }

    // -------------------------------------------------------------------------
    // Server RPCs — called by the owning client, executed on the server/host
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // Server-only helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resolves a NetworkObjectId to an Item with server-side distance validation.
    /// </summary>
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

        // Write to the NetworkVariable so all clients (especially the owner)
        // learn that this player is now holding an item.
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

    // -------------------------------------------------------------------------
    // Owner-side raycast
    // -------------------------------------------------------------------------

    /// <summary>
    /// Casts a ray from the centre of the owner's screen and returns the
    /// NetworkObjectId of the first Item in range. Only call from the owner.
    /// </summary>
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

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the currently held Item. Non-null on the server only.
    /// Use IsHoldingItem() from other clients/systems instead.
    /// </summary>
    public Item GetHeldItem() => heldItem;

    /// <summary>
    /// True if this player is holding an item. Safe to call from any client
    /// because it reads the replicated NetworkVariable.
    /// </summary>
    public bool IsHoldingItem() => IsHoldingItemLocally;

    // -------------------------------------------------------------------------
    // Input enable / disable
    // -------------------------------------------------------------------------

    void OnEnable() => inputs.Player.Enable();
    void OnDisable() => inputs.Player.Disable();
}