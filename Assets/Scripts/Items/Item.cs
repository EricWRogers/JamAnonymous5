using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Item pickup behavior using NetworkObject parenting.
/// 
/// Held items are parented to the holder's NetworkObject root and snap to the
/// holder's child holdPoint while parented.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class Item : NetworkBehaviour
{
    [Header("Item Info")]
    public string itemName = "Item";
    public ItemType itemType = ItemType.Food;

    public enum ItemType
    {
        Food,
        Utensil,
        Ingredient
    }

    private Rigidbody rb;
    private Collider col;
    private NetworkTransform networkTransform;
    private PlayerPickup parentHolder;
    private RigidbodyInterpolation defaultInterpolation;

    private NetworkVariable<bool> isHeld = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsHeld => isHeld.Value;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        networkTransform = GetComponent<NetworkTransform>();

        if (rb != null)
        {
            defaultInterpolation = rb.interpolation;
        }
    }

    public override void OnNetworkSpawn()
    {
        isHeld.OnValueChanged += OnHeldChanged;
        ApplyHeldState(isHeld.Value);
    }

    public override void OnNetworkDespawn()
    {
        isHeld.OnValueChanged -= OnHeldChanged;
    }

    public override void OnNetworkObjectParentChanged(NetworkObject parentNetworkObject)
    {
        parentHolder = null;

        if (parentNetworkObject == null)
        {
            return;
        }

        parentHolder = parentNetworkObject.GetComponentInChildren<PlayerPickup>();

        if (parentHolder != null)
        {
            SetWorldHoldPose(parentHolder);
        }
    }

    private void LateUpdate()
    {
        if (!IsHeld) return;
        if (parentHolder == null) return;

        SetWorldHoldPose(parentHolder);
    }

    private void OnHeldChanged(bool oldValue, bool newValue)
    {
        ApplyHeldState(newValue);
    }

    public bool ServerStartHolding(PlayerPickup holder)
    {
        if (!IsServer) return false;
        if (holder == null) return false;
        if (holder.NetworkObject == null) return false;
        if (!holder.NetworkObject.IsSpawned) return false;
        if (IsHeld) return false;

        isHeld.Value = true;
        ApplyHeldState(true);

        if (!NetworkObject.TrySetParent(holder.NetworkObject, worldPositionStays: false))
        {
            isHeld.Value = false;
            ApplyHeldState(false);
            Debug.LogWarning($"[Server] Failed to parent {itemName} to {holder.gameObject.name}.");
            return false;
        }

        parentHolder = holder;
        SetWorldHoldPose(holder);

        return true;
    }

    public void ServerStopHolding(Vector3 dropPosition, Quaternion dropRotation)
    {
        if (!IsServer) return;

        parentHolder = null;
        NetworkObject.TryRemoveParent(worldPositionStays: true);

        SetWorldPose(dropPosition, dropRotation);

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        isHeld.Value = false;
        ApplyHeldState(false);
    }

    private void ApplyHeldState(bool held)
    {
        if (rb != null)
        {
            rb.isKinematic = held;
            rb.interpolation = held ? RigidbodyInterpolation.None : defaultInterpolation;

            if (held)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        if (col != null)
        {
            col.enabled = !held;
        }

        if (networkTransform != null)
        {
            networkTransform.enabled = !held;
        }
    }

    private void SetWorldHoldPose(PlayerPickup holder)
    {
        if (holder == null) return;
        if (holder.holdPoint == null) return;

        transform.SetPositionAndRotation(holder.holdPoint.position, holder.holdPoint.rotation);
    }

    private void SetWorldPose(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);

        if (rb != null)
        {
            rb.position = position;
            rb.rotation = rotation;
        }
    }
}
