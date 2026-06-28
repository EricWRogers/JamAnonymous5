using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

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
    [SerializeField] private Vector3 firstIngredientLocalOffset = new Vector3(0f, 0.08f, 0f);
    [SerializeField] private Vector3 stackDirection = Vector3.up;
    [SerializeField] private float ingredientSpacing = 0.08f;
    [SerializeField] private Vector3 snappedLocalEulerAngles;

    private readonly List<FoodIngredient> snappedIngredients = new();
    private readonly List<Vector3> snappedIngredientLocalPositions = new();
    private readonly List<Quaternion> snappedIngredientLocalRotations = new();
    private readonly List<FoodIngredient> assembledIngredients = new();
    private FoodIngredient baseIngredient;
    private ServingTray currentTray;

    public FoodItemDefinition FoodDefinition => foodDefinition;
    public IReadOnlyList<FoodIngredient> SnappedIngredients => snappedIngredients;
    public ServingTray CurrentServingTray => currentTray;
    public bool IsOnServingTray => currentTray != null;

    private void Awake()
    {
        baseIngredient = GetComponent<FoodIngredient>();
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

    [ServerRpc(RequireOwnership = false)]
    private void RequestUseHeldItemServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!TryGetSenderPickup(rpcParams.Receive.SenderClientId, out PlayerPickup playerPickup))
        {
            Log($"Could not find PlayerPickup for client {rpcParams.Receive.SenderClientId}.");
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
            return false;
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
        if (IsOnServingTray)
        {
            return false;
        }

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
        if (IsOnServingTray) return false;

        return condimentTool.ServerTryApplyTo(this);
    }

    public bool ServerTrySnapIngredient(FoodIngredient ingredient)
    {
        return ServerTrySnapIngredient(ingredient, Quaternion.Euler(snappedLocalEulerAngles), spacingMultiplier: 1f);
    }

    public bool ServerTrySnapIngredientWithLocalYRotationOffset(
        FoodIngredient ingredient,
        float localYRotationOffset,
        float spacingMultiplier)
    {
        Vector3 localEulerAngles = snappedLocalEulerAngles;
        localEulerAngles.y += localYRotationOffset;

        return ServerTrySnapIngredient(ingredient, Quaternion.Euler(localEulerAngles), spacingMultiplier);
    }

    public bool ServerTrySnapIngredientWithLocalOffsets(
        FoodIngredient ingredient,
        float localYRotationOffset,
        float spacingMultiplier,
        Vector3 localPositionOffset)
    {
        Vector3 localEulerAngles = snappedLocalEulerAngles;
        localEulerAngles.y += localYRotationOffset;

        return ServerTrySnapIngredient(ingredient, Quaternion.Euler(localEulerAngles), spacingMultiplier, localPositionOffset);
    }

    private bool ServerTrySnapIngredient(FoodIngredient ingredient, Quaternion localRotation, float spacingMultiplier)
    {
        return ServerTrySnapIngredient(ingredient, localRotation, spacingMultiplier, Vector3.zero);
    }

    private bool ServerTrySnapIngredient(
        FoodIngredient ingredient,
        Quaternion localRotation,
        float spacingMultiplier,
        Vector3 localPositionOffset)
    {
        if (!IsServerActive()) return false;
        if (!CanSnapIngredient(ingredient, allowHeld: false, out _))
        {
            return false;
        }

        RemoveMissingSnappedIngredients();
        EnsureSnappedIngredientLocalPositions();

        Vector3 localPosition = GetLocalSnapPosition(snappedIngredients.Count, spacingMultiplier) + localPositionOffset;

        snappedIngredients.Add(ingredient);
        snappedIngredientLocalPositions.Add(localPosition);
        snappedIngredientLocalRotations.Add(localRotation);
        ApplySnappedPose(ingredient, localPosition, localRotation);

        NetworkObject ingredientNetworkObject = ingredient.GetComponent<NetworkObject>();

        if (NetworkObject != null &&
            NetworkObject.IsSpawned &&
            ingredientNetworkObject != null &&
            ingredientNetworkObject.IsSpawned)
        {
            SnapIngredientClientRpc(ingredientNetworkObject.NetworkObjectId, localPosition, localRotation);
        }

        return true;
    }

    public void RefreshSnappedIngredientLayout()
    {
        TrackSnappedIngredientsFromChildren();
        RemoveMissingSnappedIngredients();
        EnsureSnappedIngredientLocalPositions();
        EnsureSnappedIngredientLocalRotations();

        for (int i = 0; i < snappedIngredients.Count; i++)
        {
            ApplySnappedPose(snappedIngredients[i], snappedIngredientLocalPositions[i], snappedIngredientLocalRotations[i]);
        }
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

        if (IsOnServingTray)
        {
            reason = "food is already on a serving tray.";
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
        return firstIngredientLocalOffset + direction * ingredientSpacing * snappedIngredientIndex;
    }

    private Vector3 GetLocalSnapPosition(int snappedIngredientIndex, float spacingMultiplier)
    {
        if (snappedIngredientIndex == 0)
        {
            return GetLocalSnapPosition(snappedIngredientIndex);
        }

        EnsureSnappedIngredientLocalPositions();

        if (snappedIngredientLocalPositions.Count == 0)
        {
            return GetLocalSnapPosition(snappedIngredientIndex);
        }

        Vector3 direction = stackDirection.sqrMagnitude > 0f ? stackDirection.normalized : Vector3.up;
        Vector3 previousPosition = snappedIngredientLocalPositions[snappedIngredientLocalPositions.Count - 1];
        float previousStackDistance = Vector3.Dot(previousPosition - firstIngredientLocalOffset, direction);

        return firstIngredientLocalOffset + direction * (previousStackDistance + ingredientSpacing * Mathf.Max(0f, spacingMultiplier));
    }

    private void ApplySnappedPose(FoodIngredient ingredient, Vector3 localPosition, Quaternion localRotation)
    {
        if (ingredient == null) return;

        Transform parent = snapRoot != null ? snapRoot : transform;
        Item item = ingredient.GetComponent<Item>();

        if (item != null)
        {
            item.LockLocalParent(parent, localPosition, localRotation);
        }
        else
        {
            ingredient.transform.SetParent(parent, worldPositionStays: false);
            ingredient.transform.localPosition = localPosition;
            ingredient.transform.localRotation = localRotation;
        }

        Rigidbody rb = ingredient.GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
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

    [ClientRpc]
    private void SnapIngredientClientRpc(ulong ingredientNetworkObjectId, Vector3 localPosition, Quaternion localRotation)
    {
        if (NetworkManager.Singleton == null) return;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
            ingredientNetworkObjectId,
            out NetworkObject ingredientNetworkObject))
        {
            StartCoroutine(ApplySnappedPoseWhenSpawned(ingredientNetworkObjectId, localPosition, localRotation));
            return;
        }

        FoodIngredient ingredient = ingredientNetworkObject.GetComponent<FoodIngredient>();
        TrackSnappedIngredient(ingredient, localPosition, localRotation);
        ApplySnappedPose(ingredient, localPosition, localRotation);
    }

    private IEnumerator ApplySnappedPoseWhenSpawned(
        ulong ingredientNetworkObjectId,
        Vector3 localPosition,
        Quaternion localRotation)
    {
        const float timeoutSeconds = 2f;
        float elapsedSeconds = 0f;

        while (elapsedSeconds < timeoutSeconds)
        {
            yield return null;
            elapsedSeconds += Time.deltaTime;

            if (NetworkManager.Singleton == null)
            {
                yield break;
            }

            if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                ingredientNetworkObjectId,
                out NetworkObject ingredientNetworkObject))
            {
                continue;
            }

            FoodIngredient ingredient = ingredientNetworkObject.GetComponent<FoodIngredient>();
            TrackSnappedIngredient(ingredient, localPosition, localRotation);
            ApplySnappedPose(ingredient, localPosition, localRotation);
            yield break;
        }
    }

    private bool IsServerActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
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
