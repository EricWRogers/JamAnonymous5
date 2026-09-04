using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.Serialization;

public struct FoodStackEntry : INetworkSerializable, IEquatable<FoodStackEntry>
{
    public ulong IngredientNetworkObjectId;
    public Quaternion LocalRotation;
    public Vector3 LocalPositionOffset;
    public bool IsRemovable;

    public FoodStackEntry(
        ulong ingredientNetworkObjectId,
        Quaternion localRotation,
        Vector3 localPositionOffset,
        bool isRemovable)
    {
        IngredientNetworkObjectId = ingredientNetworkObjectId;
        LocalRotation = localRotation;
        LocalPositionOffset = localPositionOffset;
        IsRemovable = isRemovable;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref IngredientNetworkObjectId);
        serializer.SerializeValue(ref LocalRotation);
        serializer.SerializeValue(ref LocalPositionOffset);
        serializer.SerializeValue(ref IsRemovable);
    }

    public bool Equals(FoodStackEntry other)
    {
        return IngredientNetworkObjectId == other.IngredientNetworkObjectId &&
               LocalRotation.Equals(other.LocalRotation) &&
               LocalPositionOffset.Equals(other.LocalPositionOffset) &&
               IsRemovable == other.IsRemovable;
    }
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Item))]
[RequireComponent(typeof(FoodIngredient))]
[RequireComponent(typeof(NetworkObject))]
public class FoodAssemblyBase : NetworkBehaviour, IInteractable
{
    [Header("Food")]
    [SerializeField] private FoodItemDefinition foodDefinition;

    [Header("Snapping")]
    [SerializeField] private Transform snapRoot;
    [FormerlySerializedAs("firstIngredientLocalOffset")]
    [Tooltip("Optional sideways offset for the whole stack. Its component along Stack Direction is ignored because height is calculated automatically.")]
    [SerializeField] private Vector3 stackOriginLocalOffset;
    [SerializeField] private Vector3 stackDirection = Vector3.up;
    [Min(0f)]
    [Tooltip("Small gap between solid ingredient surfaces. Ingredient thickness is calculated automatically from visible bounds.")]
    [SerializeField] private float surfaceGap = 0.002f;
    [Min(0f)]
    [Tooltip("Prevents surface overlays such as condiments from z-fighting with the ingredient beneath them.")]
    [SerializeField] private float overlayGap = 0.001f;
    [SerializeField] private Vector3 snappedLocalEulerAngles;
    [SerializeField] private float serverInteractRange = 4f;

    private readonly List<FoodIngredient> snappedIngredients = new();
    private readonly List<Vector3> snappedIngredientLocalPositions = new();
    private readonly List<Quaternion> snappedIngredientLocalRotations = new();
    private readonly List<FoodIngredient> assembledIngredients = new();
    private NetworkList<FoodStackEntry> stackEntries;
    private FoodIngredient baseIngredient;
    private ServingTray currentTray;
    private bool isWaitingForStackObjects;

    public FoodItemDefinition FoodDefinition => foodDefinition;
    public IReadOnlyList<FoodIngredient> SnappedIngredients => snappedIngredients;
    public ServingTray CurrentServingTray => currentTray;
    public bool IsOnServingTray => currentTray != null;

    private void Awake()
    {
        baseIngredient = GetComponent<FoodIngredient>();
        stackEntries = new NetworkList<FoodStackEntry>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server
        );
    }

    public override void OnNetworkSpawn()
    {
        stackEntries.OnListChanged += OnStackEntriesChanged;
        RebuildStackFromNetworkState();
    }

    public override void OnNetworkDespawn()
    {
        stackEntries.OnListChanged -= OnStackEntriesChanged;
        isWaitingForStackObjects = false;
        base.OnNetworkDespawn();
    }

    public void Interact(PlayerInteraction interactor)
    {
        if (!IsSpawned)
        {
            Log("Cannot place ingredient because this assembly base is not network spawned.");
            return;
        }

        RequestUseHeldItemServerRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestUseHeldItemServerRpc(RpcParams rpcParams = default)
    {
        if (!TryGetSenderPickup(rpcParams.Receive.SenderClientId, out PlayerPickup playerPickup))
        {
            Log($"Could not find PlayerPickup for client {rpcParams.Receive.SenderClientId}.");
            return;
        }

        if (!IsPlayerCloseEnough(playerPickup))
        {
            Log($"Client {rpcParams.Receive.SenderClientId} is too far away to use this food assembly.");
            return;
        }

        ServerTryUseHeldItem(playerPickup);
    }

    public bool ServerTryUseHeldItem(PlayerPickup playerPickup)
    {
        if (!IsServerActive()) return false;
        if (playerPickup == null)
        {
            return false;
        }

        if (!playerPickup.ServerTryGetHeldItem(out Item heldItem))
        {
            return ServerTryTakeTopIngredient(playerPickup);
        }

        ServingTray servingTray = GetServingTray(heldItem);

        if (servingTray != null)
        {
            return ServerTryPlaceOnTray(servingTray);
        }

        CondimentTool condimentTool = GetCondimentTool(heldItem);

        if (condimentTool != null)
        {
            return ServerTryApplyCondiment(condimentTool);
        }

        return ServerTryPlaceHeldIngredient(playerPickup, heldItem);
    }

    public bool ServerTryPlaceHeldIngredient(PlayerPickup playerPickup)
    {
        if (!IsServerActive()) return false;
        if (playerPickup == null)
        {
            return false;
        }

        if (!playerPickup.ServerTryGetHeldItem(out Item heldItem))
        {
            return false;
        }

        return ServerTryPlaceHeldIngredient(playerPickup, heldItem);
    }

    private bool ServerTryPlaceHeldIngredient(PlayerPickup playerPickup, Item heldItem)
    {
        FoodIngredient ingredient = GetFoodIngredient(heldItem);

        if (!CanSnapIngredient(ingredient, allowHeld: true, out _))
        {
            return false;
        }

        if (!playerPickup.ServerTryReleaseHeldItem(heldItem, transform.position, transform.rotation))
        {
            return false;
        }

        return ServerTrySnapIngredient(ingredient);
    }

    public bool ServerTryPlaceOnTray(ServingTray servingTray)
    {
        if (!IsServerActive()) return false;
        if (servingTray == null) return false;
        if (IsOnServingTray) return false;
        if (!HasAnyIngredientData()) return false;

        return servingTray.ServerTryLoadFood(this);
    }

    public bool ServerTryApplyCondiment(CondimentTool condimentTool)
    {
        if (!IsServerActive()) return false;
        if (condimentTool == null) return false;

        return condimentTool.ServerTryApplyTo(this);
    }

    public bool ServerTrySnapIngredient(FoodIngredient ingredient)
    {
        return ServerTrySnapIngredient(
            ingredient,
            Quaternion.Euler(snappedLocalEulerAngles),
            Vector3.zero,
            isRemovable: true
        );
    }

    public bool ServerTrySnapIngredientWithLocalYRotationOffset(
        FoodIngredient ingredient,
        float localYRotationOffset,
        bool isRemovable = true)
    {
        Vector3 localEulerAngles = snappedLocalEulerAngles;
        localEulerAngles.y += localYRotationOffset;

        return ServerTrySnapIngredient(
            ingredient,
            Quaternion.Euler(localEulerAngles),
            Vector3.zero,
            isRemovable
        );
    }

    public bool ServerTrySnapIngredientWithLocalOffsets(
        FoodIngredient ingredient,
        float localYRotationOffset,
        Vector3 localPositionOffset,
        bool isRemovable = true)
    {
        Vector3 localEulerAngles = snappedLocalEulerAngles;
        localEulerAngles.y += localYRotationOffset;

        return ServerTrySnapIngredient(
            ingredient,
            Quaternion.Euler(localEulerAngles),
            localPositionOffset,
            isRemovable
        );
    }

    private bool ServerTrySnapIngredient(
        FoodIngredient ingredient,
        Quaternion localRotation,
        Vector3 localPositionOffset,
        bool isRemovable)
    {
        if (!IsServerActive()) return false;
        if (!CanSnapIngredient(ingredient, allowHeld: false, out _))
        {
            return false;
        }

        NetworkObject ingredientNetworkObject = ingredient.GetComponent<NetworkObject>();

        if (ingredientNetworkObject == null || !ingredientNetworkObject.IsSpawned)
        {
            return false;
        }

        stackEntries.Add(new FoodStackEntry(
            ingredientNetworkObject.NetworkObjectId,
            localRotation,
            localPositionOffset,
            isRemovable
        ));

        return true;
    }

    public bool ServerTryTakeTopIngredient(PlayerPickup playerPickup)
    {
        if (!IsServerActive() || playerPickup == null) return false;
        if (playerPickup.ServerTryGetHeldItem(out _)) return false;

        Item assemblyItem = GetComponent<Item>();
        if (assemblyItem != null && assemblyItem.IsHeld) return false;

        for (int i = stackEntries.Count - 1; i >= 0; i--)
        {
            FoodStackEntry entry = stackEntries[i];
            if (!entry.IsRemovable) continue;
            if (!TryResolveIngredient(entry.IngredientNetworkObjectId, out FoodIngredient ingredient)) continue;

            Item ingredientItem = ingredient.GetComponent<Item>();
            if (ingredientItem == null) continue;

            stackEntries.RemoveAt(i);
            ingredientItem.UnlockLocalParent();

            if (playerPickup.ServerTryPickUpItem(ingredientItem))
            {
                return true;
            }

            stackEntries.Insert(i, entry);
            return false;
        }

        return false;
    }

    public void RefreshSnappedIngredientLayout()
    {
        if (IsSpawned)
        {
            RebuildStackFromNetworkState();
            return;
        }

        TrackSnappedIngredientsFromChildren();
        ApplyTrackedIngredientPoses();
    }

    public void SetCurrentServingTray(ServingTray servingTray)
    {
        currentTray = servingTray;
    }

    public bool ServerTryRemoveFromServingTray()
    {
        if (!IsServerActive()) return false;
        if (currentTray == null) return true;

        return currentTray.ServerTryUnloadFood(this);
    }

    private bool CanSnapIngredient(FoodIngredient ingredient, bool allowHeld, out string reason)
    {
        reason = null;

        if (ingredient == null)
        {
            reason = "held item has no FoodIngredient component.";
            return false;
        }

        if (ingredient == baseIngredient)
        {
            reason = "cannot place the base ingredient onto itself.";
            return false;
        }

        if (!ingredient.HasDefinition)
        {
            reason = $"{ingredient.name} has no FoodIngredientDefinition assigned.";
            return false;
        }

        if (ingredient.Definition.StackBehavior == FoodStackBehavior.Unstackable)
        {
            reason = $"{ingredient.Definition.IngredientName} cannot be added to a food stack.";
            return false;
        }

        if (snappedIngredients.Contains(ingredient))
        {
            reason = $"{ingredient.name} is already placed on this food.";
            return false;
        }

        if (ingredient.TryGetComponent(out FoodAssemblyBase otherAssemblyBase) && otherAssemblyBase != this)
        {
            reason = $"{ingredient.name} is another food assembly base.";
            return false;
        }

        if (foodDefinition != null && !foodDefinition.AllowsIngredient(ingredient.Definition))
        {
            reason = $"{ingredient.Definition.IngredientName} is not allowed by {foodDefinition.FoodName}.";
            return false;
        }

        Item item = ingredient.GetComponent<Item>();

        if (!allowHeld && item != null && item.IsHeld)
        {
            reason = $"{ingredient.name} is still being held.";
            return false;
        }

        return true;
    }

    public IReadOnlyList<FoodIngredient> GetAssembledIngredients()
    {
        assembledIngredients.Clear();

        if (baseIngredient != null && baseIngredient.HasDefinition)
        {
            assembledIngredients.Add(baseIngredient);
        }

        RemoveMissingSnappedIngredients();

        for (int i = 0; i < snappedIngredients.Count; i++)
        {
            FoodIngredient ingredient = snappedIngredients[i];

            if (ingredient != null && ingredient.HasDefinition)
            {
                assembledIngredients.Add(ingredient);
            }
        }

        return assembledIngredients;
    }

    public bool HasAnyIngredientData()
    {
        if (baseIngredient != null && baseIngredient.HasDefinition)
        {
            return true;
        }

        RemoveMissingSnappedIngredients();

        for (int i = 0; i < snappedIngredients.Count; i++)
        {
            FoodIngredient ingredient = snappedIngredients[i];

            if (ingredient != null && ingredient.HasDefinition)
            {
                return true;
            }
        }

        return false;
    }

    private Vector3 GetLocalSnapPosition(int snappedIngredientIndex)
    {
        Vector3 direction = stackDirection.sqrMagnitude > 0f ? stackDirection.normalized : Vector3.up;
        return GetStackOriginOffset(direction);
    }

    private void ApplySnappedPose(FoodIngredient ingredient, Vector3 localPosition, Quaternion localRotation)
    {
        if (ingredient == null) return;

        ingredient.SetCurrentAssembly(this);
        Transform parent = snapRoot != null ? snapRoot : transform;
        Item item = ingredient.GetComponent<Item>();

        if (item != null)
        {
            item.LockLocalParentPreserveWorldScale(parent, localPosition, localRotation);
        }
        else
        {
            ingredient.transform.SetParent(parent, worldPositionStays: false);
            ingredient.transform.localPosition = localPosition;
            ingredient.transform.localRotation = localRotation;
        }

        Rigidbody rb = ingredient.GetComponent<Rigidbody>();

        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        NetworkTransform networkTransform = ingredient.GetComponent<NetworkTransform>();

        if (networkTransform != null)
        {
            networkTransform.enabled = false;
        }

        Collider[] colliders = ingredient.GetComponentsInChildren<Collider>();

        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
        }
    }

    private void OnStackEntriesChanged(NetworkListEvent<FoodStackEntry> changeEvent)
    {
        RebuildStackFromNetworkState();
    }

    private void RebuildStackFromNetworkState()
    {
        List<FoodIngredient> previouslySnappedIngredients = new(snappedIngredients);
        snappedIngredients.Clear();
        snappedIngredientLocalPositions.Clear();
        snappedIngredientLocalRotations.Clear();

        Transform parent = snapRoot != null ? snapRoot : transform;
        Vector3 direction = stackDirection.sqrMagnitude > 0f ? stackDirection.normalized : Vector3.up;
        Vector3 stackOrigin = GetStackOriginOffset(direction);
        float currentStackTop = GetInitialStackTop(parent, direction, stackOrigin);
        bool hasMissingObject = false;

        for (int i = 0; i < stackEntries.Count; i++)
        {
            FoodStackEntry entry = stackEntries[i];

            if (!TryResolveIngredient(entry.IngredientNetworkObjectId, out FoodIngredient ingredient))
            {
                hasMissingObject = true;
                continue;
            }

            Vector3 localPosition = GetAutomaticStackPosition(
                ingredient,
                entry,
                parent,
                direction,
                stackOrigin,
                ref currentStackTop
            );

            TrackSnappedIngredient(ingredient, localPosition, entry.LocalRotation);
        }

        for (int i = 0; i < previouslySnappedIngredients.Count; i++)
        {
            FoodIngredient previousIngredient = previouslySnappedIngredients[i];

            if (previousIngredient != null &&
                !snappedIngredients.Contains(previousIngredient) &&
                previousIngredient.CurrentAssembly == this)
            {
                previousIngredient.SetCurrentAssembly(null);
            }
        }

        if (hasMissingObject && !isWaitingForStackObjects && isActiveAndEnabled)
        {
            StartCoroutine(RebuildWhenStackObjectsAreSpawned());
        }
    }

    private IEnumerator RebuildWhenStackObjectsAreSpawned()
    {
        isWaitingForStackObjects = true;
        const float timeoutSeconds = 5f;
        float elapsedSeconds = 0f;

        while (elapsedSeconds < timeoutSeconds)
        {
            yield return null;
            elapsedSeconds += Time.deltaTime;

            bool allResolved = true;
            for (int i = 0; i < stackEntries.Count; i++)
            {
                if (!TryResolveIngredient(stackEntries[i].IngredientNetworkObjectId, out _))
                {
                    allResolved = false;
                    break;
                }
            }

            if (!allResolved) continue;

            isWaitingForStackObjects = false;
            RebuildStackFromNetworkState();
            yield break;
        }

        isWaitingForStackObjects = false;
    }

    private Vector3 GetAutomaticStackPosition(
        FoodIngredient ingredient,
        FoodStackEntry entry,
        Transform parent,
        Vector3 direction,
        Vector3 stackOrigin,
        ref float currentStackTop)
    {
        Vector3 lateralOffset = entry.LocalPositionOffset -
                                direction * Vector3.Dot(entry.LocalPositionOffset, direction);
        Vector3 candidatePosition = stackOrigin + lateralOffset;

        // Measure in the exact rotation and preserved scale that will be used in the stack.
        ApplySnappedPose(ingredient, candidatePosition, entry.LocalRotation);

        if (!ingredient.TryGetStackProjection(parent, direction, out float bottom, out float top))
        {
            Vector3 fallbackPosition = candidatePosition + direction * currentStackTop;
            ingredient.transform.localPosition = fallbackPosition;
            return fallbackPosition;
        }

        FoodStackBehavior behavior = ingredient.Definition.StackBehavior;
        float gap = behavior == FoodStackBehavior.SurfaceOverlay ? overlayGap : surfaceGap;
        float correction = currentStackTop + gap - bottom;
        Vector3 finalPosition = candidatePosition + direction * correction;

        if (behavior == FoodStackBehavior.SolidLayer)
        {
            currentStackTop = top + correction;
        }

        ingredient.transform.localPosition = finalPosition;

        return finalPosition;
    }

    private float GetInitialStackTop(Transform parent, Vector3 direction, Vector3 stackOrigin)
    {
        if (baseIngredient != null &&
            baseIngredient.TryGetStackProjection(parent, direction, out _, out float baseTop))
        {
            return baseTop;
        }

        return Vector3.Dot(stackOrigin, direction);
    }

    private Vector3 GetStackOriginOffset(Vector3 direction)
    {
        return stackOriginLocalOffset - direction * Vector3.Dot(stackOriginLocalOffset, direction);
    }

    private bool TryResolveIngredient(ulong networkObjectId, out FoodIngredient ingredient)
    {
        ingredient = null;
        if (NetworkManager.Singleton == null) return false;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject)) return false;

        ingredient = networkObject.GetComponent<FoodIngredient>();
        return ingredient != null;
    }

    private void ApplyTrackedIngredientPoses()
    {
        RemoveMissingSnappedIngredients();
        EnsureSnappedIngredientLocalPositions();
        EnsureSnappedIngredientLocalRotations();

        for (int i = 0; i < snappedIngredients.Count; i++)
        {
            ApplySnappedPose(snappedIngredients[i], snappedIngredientLocalPositions[i], snappedIngredientLocalRotations[i]);
        }
    }

    private bool IsServerActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    }

    private bool IsPlayerCloseEnough(PlayerPickup playerPickup)
    {
        if (playerPickup == null) return false;

        float allowedRange = Mathf.Max(0f, serverInteractRange);
        return Vector3.Distance(playerPickup.transform.position, transform.position) <= allowedRange;
    }

    private bool TryGetSenderPickup(ulong senderClientId, out PlayerPickup playerPickup)
    {
        playerPickup = null;

        if (NetworkManager.Singleton == null) return false;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client))
        {
            return false;
        }

        if (client.PlayerObject == null)
        {
            return false;
        }

        playerPickup = client.PlayerObject.GetComponent<PlayerPickup>();

        if (playerPickup != null)
        {
            return true;
        }

        playerPickup = client.PlayerObject.GetComponentInChildren<PlayerPickup>();

        if (playerPickup != null)
        {
            return true;
        }

        playerPickup = client.PlayerObject.GetComponentInParent<PlayerPickup>();
        return playerPickup != null;
    }

    private FoodIngredient GetFoodIngredient(Item item)
    {
        if (item == null) return null;

        FoodIngredient ingredient = item.GetComponent<FoodIngredient>();

        if (ingredient != null)
        {
            return ingredient;
        }

        ingredient = item.GetComponentInChildren<FoodIngredient>();

        if (ingredient != null)
        {
            return ingredient;
        }

        return item.GetComponentInParent<FoodIngredient>();
    }

    private ServingTray GetServingTray(Item item)
    {
        if (item == null) return null;

        ServingTray servingTray = item.GetComponent<ServingTray>();

        if (servingTray != null)
        {
            return servingTray;
        }

        servingTray = item.GetComponentInChildren<ServingTray>();

        if (servingTray != null)
        {
            return servingTray;
        }

        return item.GetComponentInParent<ServingTray>();
    }

    private CondimentTool GetCondimentTool(Item item)
    {
        if (item == null) return null;

        CondimentTool condimentTool = item.GetComponent<CondimentTool>();

        if (condimentTool != null)
        {
            return condimentTool;
        }

        condimentTool = item.GetComponentInChildren<CondimentTool>();

        if (condimentTool != null)
        {
            return condimentTool;
        }

        return item.GetComponentInParent<CondimentTool>();
    }

    private void TrackSnappedIngredient(FoodIngredient ingredient)
    {
        if (ingredient == null) return;

        TrackSnappedIngredient(ingredient, ingredient.transform.localPosition, ingredient.transform.localRotation);
    }

    private void TrackSnappedIngredient(FoodIngredient ingredient, Vector3 localPosition, Quaternion localRotation)
    {
        if (ingredient == null) return;
        if (ingredient == baseIngredient) return;

        int existingIndex = snappedIngredients.IndexOf(ingredient);

        if (existingIndex >= 0)
        {
            EnsureSnappedIngredientLocalPositions();
            EnsureSnappedIngredientLocalRotations();
            snappedIngredientLocalPositions[existingIndex] = localPosition;
            snappedIngredientLocalRotations[existingIndex] = localRotation;
            return;
        }

        snappedIngredients.Add(ingredient);
        snappedIngredientLocalPositions.Add(localPosition);
        snappedIngredientLocalRotations.Add(localRotation);
    }

    private void TrackSnappedIngredientsFromChildren()
    {
        Transform parent = snapRoot != null ? snapRoot : transform;
        FoodIngredient[] childIngredients = parent.GetComponentsInChildren<FoodIngredient>(includeInactive: true);

        for (int i = 0; i < childIngredients.Length; i++)
        {
            TrackSnappedIngredient(childIngredients[i]);
        }
    }

    private void RemoveMissingSnappedIngredients()
    {
        for (int i = snappedIngredients.Count - 1; i >= 0; i--)
        {
            if (snappedIngredients[i] != null)
            {
                continue;
            }

            snappedIngredients.RemoveAt(i);

            if (i < snappedIngredientLocalPositions.Count)
            {
                snappedIngredientLocalPositions.RemoveAt(i);
            }

            if (i < snappedIngredientLocalRotations.Count)
            {
                snappedIngredientLocalRotations.RemoveAt(i);
            }
        }

        while (snappedIngredientLocalPositions.Count > snappedIngredients.Count)
        {
            snappedIngredientLocalPositions.RemoveAt(snappedIngredientLocalPositions.Count - 1);
        }

        while (snappedIngredientLocalRotations.Count > snappedIngredients.Count)
        {
            snappedIngredientLocalRotations.RemoveAt(snappedIngredientLocalRotations.Count - 1);
        }
    }

    private void EnsureSnappedIngredientLocalPositions()
    {
        while (snappedIngredientLocalPositions.Count < snappedIngredients.Count)
        {
            int index = snappedIngredientLocalPositions.Count;
            FoodIngredient ingredient = snappedIngredients[index];

            Vector3 localPosition = ingredient != null
                ? ingredient.transform.localPosition
                : GetLocalSnapPosition(index);

            snappedIngredientLocalPositions.Add(localPosition);
        }
    }

    private void EnsureSnappedIngredientLocalRotations()
    {
        while (snappedIngredientLocalRotations.Count < snappedIngredients.Count)
        {
            int index = snappedIngredientLocalRotations.Count;
            FoodIngredient ingredient = snappedIngredients[index];

            Quaternion localRotation = ingredient != null
                ? ingredient.transform.localRotation
                : Quaternion.Euler(snappedLocalEulerAngles);

            snappedIngredientLocalRotations.Add(localRotation);
        }
    }

    private void Log(string message)
    {
        Debug.LogWarning($"[FoodAssemblyBase] {message}", this);
    }
}
