using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Item pickup behavior.
/// 
/// While held, each client locally parents the item to its visible copy of the holder's holdPoint.
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
    private PlayerPickup cachedHolder;
    private RigidbodyInterpolation defaultInterpolation;
    private bool defaultAutoObjectParentSync;
    private bool localParentLocked;

    private const ulong NoHolder = ulong.MaxValue;

    private NetworkVariable<ulong> holderClientId = new NetworkVariable<ulong>(
        NoHolder,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsHeld => holderClientId.Value != NoHolder;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<Collider>();
        networkTransform = GetComponent<NetworkTransform>();

        if (rb != null)
        {
            defaultInterpolation = rb.interpolation;
        }

        if (NetworkObject != null)
        {
            defaultAutoObjectParentSync = NetworkObject.AutoObjectParentSync;
        }
    }

    public override void OnNetworkSpawn()
    {
        holderClientId.OnValueChanged += OnHolderChanged;
        ApplyHeldState(IsHeld);

        if (IsHeld)
        {
            TryAttachToHolder();
        }
    }

    public override void OnNetworkDespawn()
    {
        holderClientId.OnValueChanged -= OnHolderChanged;
        localParentLocked = false;
        DetachFromHolder(worldPositionStays: true);
    }

    private void LateUpdate()
    {
        if (localParentLocked) return;
        if (!IsHeld) return;
        if (transform.parent != null) return;

        TryAttachToHolder();
    }

    private void OnHolderChanged(ulong oldValue, ulong newValue)
    {
        cachedHolder = null;

        if (newValue == NoHolder)
        {
            if (localParentLocked)
            {
                ApplyHeldState(true);
                return;
            }

            ApplyHeldState(false);
            DetachFromHolder(worldPositionStays: true);
        }
        else
        {
            UnlockLocalParent();
            ApplyHeldState(true);
            TryAttachToHolder();
        }
    }

    public bool ServerStartHolding(PlayerPickup holder)
    {
        if (!IsServer) return false;
        if (holder == null) return false;
        if (holder.NetworkObject == null) return false;
        if (!holder.NetworkObject.IsSpawned) return false;
        if (IsHeld) return false;

        UnlockLocalParent();
        cachedHolder = holder;
        holderClientId.Value = holder.OwnerClientId;
        ApplyHeldState(true);
        AttachToHolder(holder);

        return true;
    }

    public void ServerStopHolding(Vector3 dropPosition, Quaternion dropRotation)
    {
        if (!IsServer) return;

        holderClientId.Value = NoHolder;
        cachedHolder = null;
        DetachFromHolder(worldPositionStays: true);

        SetWorldPose(dropPosition, dropRotation);

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        ApplyHeldState(false);
    }

    public void LockLocalParent(Transform parent, Vector3 localPosition, Quaternion localRotation)
    {
        if (parent == null) return;

        localParentLocked = true;

        if (NetworkObject != null)
        {
            NetworkObject.AutoObjectParentSync = false;
        }

        transform.SetParent(parent, worldPositionStays: false);
        transform.localPosition = localPosition;
        transform.localRotation = localRotation;

        ApplyHeldState(true);
    }

    public void UnlockLocalParent()
    {
        if (!localParentLocked) return;

        localParentLocked = false;

        if (NetworkObject != null)
        {
            NetworkObject.AutoObjectParentSync = defaultAutoObjectParentSync;
        }
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

    private bool TryAttachToHolder()
    {
        if (!TryGetHolder(out PlayerPickup holder))
        {
            return false;
        }

        AttachToHolder(holder);
        return true;
    }

    private void AttachToHolder(PlayerPickup holder)
    {
        if (holder == null) return;
        if (holder.holdPoint == null) return;

        if (NetworkObject != null)
        {
            NetworkObject.AutoObjectParentSync = false;
        }

        transform.SetParent(holder.holdPoint, worldPositionStays: false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
    }

    private void DetachFromHolder(bool worldPositionStays)
    {
        transform.SetParent(null, worldPositionStays);

        if (NetworkObject != null)
        {
            NetworkObject.AutoObjectParentSync = defaultAutoObjectParentSync;
        }
    }

    private bool TryGetHolder(out PlayerPickup holder)
    {
        holder = cachedHolder;

        if (holder != null &&
            holder.NetworkObject != null &&
            holder.NetworkObject.IsSpawned &&
            holder.OwnerClientId == holderClientId.Value)
        {
            return true;
        }

        holder = null;

        if (holderClientId.Value == NoHolder)
        {
            return false;
        }

        PlayerPickup[] pickups = FindObjectsByType<PlayerPickup>(FindObjectsSortMode.None);

        for (int i = 0; i < pickups.Length; i++)
        {
            PlayerPickup candidate = pickups[i];

            if (candidate.NetworkObject != null &&
                candidate.NetworkObject.IsSpawned &&
                candidate.OwnerClientId == holderClientId.Value)
            {
                cachedHolder = candidate;
                holder = candidate;
                return true;
            }
        }

        return false;
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
