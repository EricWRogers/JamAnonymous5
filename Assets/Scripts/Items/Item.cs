using System.Collections;
using System.Collections.Generic;
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
[DefaultExecutionOrder(-120)]
public class Item : NetworkBehaviour, IInteractable
{
    public void Interact(PlayerInteraction interactor)
    {
        if (interactor.TryGetComponent(out PlayerPickup pickup)) pickup.RequestPickUpItem(this);
    }
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
    private NetworkTransform networkTransform;
    private PlayerPickup cachedHolder;
    private RigidbodyInterpolation defaultInterpolation;
    private bool defaultAutoObjectParentSync;
    private bool localParentLocked;
    private readonly Dictionary<Collider, bool> colliderStatesBeforeHold = new();

    private const ulong NoHolder = ulong.MaxValue;
    private readonly NetworkVariable<TruckRelativePose> surfacePose = new(TruckRelativePose.Detached);
    private TruckTransformSuspension surfaceSync;
    private bool resolvedSurface;
    private readonly List<(Collider item, Collider truck)> surfaceCollisionPairs = new();
    public bool IsSurfaceAttached => surfacePose.Value.Support != TruckRelativePose.None;
    public VehicleKitchen AttachedKitchen
    {
        get
        {
            Item current = this;
            for (int i = 0; i < 16; i++)
            {
                if (!current.TryGetSurface(out NetworkObject support))
                {
                    current = current.transform.parent != null ? current.transform.parent.GetComponentInParent<Item>() : null;
                    if (current == null || current == this) return null;
                    continue;
                }
                VehicleKitchen kitchen = support.GetComponentInParent<VehicleKitchen>();
                if (kitchen != null) return kitchen;
                current = support.GetComponent<Item>();
                if (current == null || current == this) return null;
            }
            return null;
        }
    }
    private bool TryGetSurface(out NetworkObject support)
    {
        support = null;
        return IsSurfaceAttached && NetworkManager != null &&
            NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(surfacePose.Value.Support, out support);
    }
    public bool ServerAttachToSurface(NetworkObject support, Vector3 position, Quaternion rotation)
    {
        if (!IsServer || IsHeld || support == null || !support.IsSpawned || support == NetworkObject) return false;
        surfacePose.Value = new TruckRelativePose { Support = support.NetworkObjectId,
            Position = support.transform.InverseTransformPoint(position),
            Rotation = Quaternion.Inverse(support.transform.rotation) * rotation };
        FollowSurface();
        return true;
    }
    private void OnSurfaceChanged(TruckRelativePose previous, TruckRelativePose current)
    {
        if (current.Support == TruckRelativePose.None)
        {
            resolvedSurface = false;
            surfaceSync?.Restore();
            surfaceSync = null;
            ApplyHeldState(IsHeld || localParentLocked);
        }
        else FollowSurface();
    }
    private void FollowSurface(int depth = 0)
    {
        if (IsHeld || localParentLocked || depth >= 16) return;
        // Keep pending late-join attachments stationary until their support arrives.
        if (networkTransform != null && surfaceSync == null) surfaceSync = new TruckTransformSuspension(networkTransform);
        surfaceSync?.Suspend();
        if (rb != null) rb.isKinematic = true;
        RestoreSuppressedColliders();
        if (!TryGetSurface(out NetworkObject support))
        {
            if (IsServer && resolvedSurface) surfacePose.Value = TruckRelativePose.Detached;
            return;
        }
        resolvedSurface = true;
        if (support.TryGetComponent(out Item supportingItem) && supportingItem.IsSurfaceAttached)
            supportingItem.FollowSurface(depth + 1);
        TruckRelativePose pose = surfacePose.Value;
        SetWorldPose(support.transform.TransformPoint(pose.Position), support.transform.rotation * pose.Rotation);
        SuppressSurfaceCollisions();
    }

    private void SuppressSurfaceCollisions()
    {
        VehicleKitchen kitchen = AttachedKitchen;
        if (kitchen == null) return;
        Rigidbody truckBody = kitchen.GetComponent<Rigidbody>();
        foreach (Collider own in GetComponentsInChildren<Collider>(true))
        foreach (Collider other in kitchen.GetComponentsInChildren<Collider>(true))
        {
            if (own == other || own.isTrigger || other.isTrigger || other.attachedRigidbody != truckBody) continue;
            var pair = (own, other);
            if (!surfaceCollisionPairs.Contains(pair) && !Physics.GetIgnoreCollision(own, other))
                surfaceCollisionPairs.Add(pair);
        }
        UpdateSurfaceCollisions(false);
    }

    private void UpdateSurfaceCollisions(bool release)
    {
        for (int i = surfaceCollisionPairs.Count - 1; i >= 0; i--)
        {
            var pair = surfaceCollisionPairs[i];
            if (pair.item == null || pair.truck == null) { surfaceCollisionPairs.RemoveAt(i); continue; }
            // Disabling held colliders can reset IgnoreCollision. Reapply until
            // release has actually cleared the truck, preventing a separation kick.
            bool separated = pair.item.enabled && pair.truck.enabled &&
                !pair.item.bounds.Intersects(pair.truck.bounds);
            if (release && separated)
            {
                Physics.IgnoreCollision(pair.item, pair.truck, false);
                surfaceCollisionPairs.RemoveAt(i);
            }
            else Physics.IgnoreCollision(pair.item, pair.truck, true);
        }
    }

    private void FixedUpdate()
    {
        if (!IsSpawned) return;
        if (IsSurfaceAttached && !IsHeld && !localParentLocked) FollowSurface();
        // Assembly children may have a local parent instead of their own surface state.
        else if (AttachedKitchen != null) SuppressSurfaceCollisions();
        else UpdateSurfaceCollisions(!IsHeld);
    }

    private NetworkVariable<ulong> holderClientId = new NetworkVariable<ulong>(
        NoHolder,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsHeld => holderClientId.Value != NoHolder;

    public bool IsColliderEnabledAfterRelease(Collider collider)
    {
        if (collider == null || !collider.gameObject.activeInHierarchy) return false;
        if (colliderStatesBeforeHold.TryGetValue(collider, out bool wasEnabled)) return wasEnabled;
        return collider.enabled;
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
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
        surfacePose.OnValueChanged += OnSurfaceChanged;
        ApplyHeldState(IsHeld);

        if (IsHeld)
        {
            TryAttachToHolder();
        }
    }

    public override void OnNetworkDespawn()
    {
        foreach (var pair in surfaceCollisionPairs)
            if (pair.item != null && pair.truck != null) Physics.IgnoreCollision(pair.item, pair.truck, false);
        surfaceCollisionPairs.Clear();
        holderClientId.OnValueChanged -= OnHolderChanged;
        surfacePose.OnValueChanged -= OnSurfaceChanged;
        surfaceSync?.Restore();
        surfaceSync = null;
        localParentLocked = false;
        DetachFromHolder(worldPositionStays: true);
    }

    private void LateUpdate()
    {
        if (IsSurfaceAttached && !IsHeld && !localParentLocked) { FollowSurface(); return; }
        if (IsHeld)
        {
            SuppressCollidersWhileHeld();
        }

        if (localParentLocked) return;
        if (!IsHeld) return;
        if (TryGetComponent(out IngredientBox box) && TryGetHolder(out PlayerPickup holder) &&
            holder.holdPoint != null && transform.parent == holder.holdPoint)
        {
            transform.localPosition = box.GetCarryOffset(holder);
        }
        if (transform.parent != null) return;

        TryAttachToHolder();
    }

    private void Update()
    {
        if (IsSpawned && IsSurfaceAttached && !IsHeld && !localParentLocked) FollowSurface();
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

        surfacePose.Value = TruckRelativePose.Detached;
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
        if (networkTransform != null && networkTransform.IsSpawned &&
            (networkTransform.IsServerAuthoritative() || networkTransform.IsOwner))
            networkTransform.Teleport(dropPosition, dropRotation, transform.localScale);
    }

    public void ServerStopHoldingPreserveWorldPose()
    {
        if (!IsServer) return;

        Vector3 position = transform.position;
        Quaternion rotation = transform.rotation;

        holderClientId.Value = NoHolder;
        cachedHolder = null;
        DetachFromHolder(worldPositionStays: true);

        SetWorldPose(position, rotation);

        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        ApplyHeldState(false);
    }

    public void LockLocalParent(Transform parent, Vector3 localPosition, Quaternion localRotation)
    {
        LockLocalParent(parent, localPosition, localRotation, preserveWorldScale: false);
    }

    public void LockLocalParentPreserveWorldScale(Transform parent, Vector3 localPosition, Quaternion localRotation)
    {
        LockLocalParent(parent, localPosition, localRotation, preserveWorldScale: true);
    }

    public void ServerApplyThrow(Vector3 velocity, Transform thrower)
    {
        if (!IsServer || IsHeld || rb == null || rb.isKinematic || !ItemPlacement.IsFinite(velocity)) return;
        rb.linearVelocity = velocity;
        rb.WakeUp();
        if (thrower != null) StartCoroutine(IgnoreThrowerUntilClear(thrower));
    }

    private IEnumerator IgnoreThrowerUntilClear(Transform thrower)
    {
        var pairs = new List<(Collider item, Collider player)>();
        Collider[] playerColliders = thrower.GetComponentsInChildren<Collider>();
        foreach (Collider itemCollider in GetComponentsInChildren<Collider>())
        foreach (Collider playerCollider in playerColliders)
        {
            if (itemCollider == playerCollider || Physics.GetIgnoreCollision(itemCollider, playerCollider)) continue;
            Physics.IgnoreCollision(itemCollider, playerCollider, true);
            pairs.Add((itemCollider, playerCollider));
        }
        var step = new WaitForFixedUpdate();
        float earliestRestore = Time.time + 0.15f;
        bool overlapping;
        do
        {
            yield return step;
            overlapping = false;
            foreach (var pair in pairs)
                if (pair.item != null && pair.player != null && pair.item.enabled && pair.player.enabled &&
                    pair.item.bounds.Intersects(pair.player.bounds)) { overlapping = true; break; }
        } while (!IsHeld && (Time.time < earliestRestore || overlapping));
        foreach (var pair in pairs)
            if (pair.item != null && pair.player != null) Physics.IgnoreCollision(pair.item, pair.player, false);
    }

    /// <summary>
    /// Locks an item at a world-space pose without inheriting scale from a station.
    /// Use this for static placement surfaces whose transforms may be non-uniformly scaled.
    /// </summary>
    public void LockWorldPose(Vector3 worldPosition, Quaternion worldRotation)
    {
        Vector3 worldScale = transform.lossyScale;
        localParentLocked = true;

        if (NetworkObject != null)
        {
            NetworkObject.AutoObjectParentSync = false;
        }

        transform.SetParent(null, worldPositionStays: true);
        transform.localScale = worldScale;
        SetWorldPose(worldPosition, worldRotation);
        ApplyHeldState(true);
    }

    private void LockLocalParent(
        Transform parent,
        Vector3 localPosition,
        Quaternion localRotation,
        bool preserveWorldScale)
    {
        if (parent == null) return;

        Vector3 worldScale = transform.lossyScale;
        localParentLocked = true;

        if (NetworkObject != null)
        {
            NetworkObject.AutoObjectParentSync = false;
        }

        transform.SetParent(parent, worldPositionStays: false);
        transform.localPosition = localPosition;
        transform.localRotation = localRotation;

        if (preserveWorldScale)
        {
            SetLocalScaleForWorldScale(worldScale, parent);
        }

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
            if (held)
            {
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.None;
            }
            else
            {
                rb.isKinematic = IsSurfaceAttached || (IsSpawned && networkTransform != null &&
                    (networkTransform.IsServerAuthoritative() ? !IsServer : !IsOwner));
                rb.interpolation = defaultInterpolation;
            }
        }

        if (held)
        {
            SuppressCollidersWhileHeld();
        }
        else
        {
            RestoreSuppressedColliders();
        }

        if (networkTransform != null)
        {
            networkTransform.enabled = !held;
        }
    }

    private void SuppressCollidersWhileHeld()
    {
        Collider[] colliders = GetComponentsInChildren<Collider>(includeInactive: true);

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider currentCollider = colliders[i];

            if (currentCollider == null)
            {
                continue;
            }

            if (!colliderStatesBeforeHold.ContainsKey(currentCollider))
            {
                colliderStatesBeforeHold.Add(currentCollider, currentCollider.enabled);
            }

            currentCollider.enabled = false;
        }
    }

    private void RestoreSuppressedColliders()
    {
        foreach (KeyValuePair<Collider, bool> colliderState in colliderStatesBeforeHold)
        {
            if (colliderState.Key != null)
            {
                colliderState.Key.enabled = colliderState.Value;
            }
        }

        colliderStatesBeforeHold.Clear();
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

        Vector3 worldScale = transform.lossyScale;

        if (NetworkObject != null)
        {
            NetworkObject.AutoObjectParentSync = false;
        }

        transform.SetParent(holder.holdPoint, worldPositionStays: false);
        transform.localPosition = TryGetComponent(out IngredientBox box) ? box.GetCarryOffset(holder) : Vector3.zero;
        transform.localRotation = Quaternion.identity;
        SetLocalScaleForWorldScale(worldScale, holder.holdPoint);
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

    private void SetLocalScaleForWorldScale(Vector3 worldScale, Transform parent)
    {
        if (parent == null)
        {
            transform.localScale = worldScale;
            return;
        }

        Vector3 parentScale = parent.lossyScale;

        transform.localScale = new Vector3(
            DivideScale(worldScale.x, parentScale.x),
            DivideScale(worldScale.y, parentScale.y),
            DivideScale(worldScale.z, parentScale.z)
        );
    }

    private float DivideScale(float value, float divisor)
    {
        return Mathf.Abs(divisor) > 0.0001f ? value / divisor : value;
    }
}
