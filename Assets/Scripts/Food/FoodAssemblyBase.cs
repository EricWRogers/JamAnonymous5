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
    [SerializeField] private FoodItemDefinition foodToWrap;
    [SerializeField] private Transform wrappedFoodSpawnPoint;
    [SerializeField] private bool despawnIngredientsWhenWrapped = true;

    [Header("Snapping")]
    [SerializeField] private Transform snapRoot;
    [SerializeField] private Vector3 firstIngredientLocalOffset = new Vector3(0f, 0.08f, 0f);
    [SerializeField] private Vector3 stackDirection = Vector3.up;
    [SerializeField] private float ingredientSpacing = 0.08f;
    [SerializeField] private Vector3 snappedLocalEulerAngles;

    private readonly List<FoodIngredient> snappedIngredients = new();
    private readonly List<FoodIngredient> wrapIngredients = new();
    private FoodIngredient baseIngredient;

    public FoodItemDefinition FoodToWrap => foodToWrap;
    public IReadOnlyList<FoodIngredient> SnappedIngredients => snappedIngredients;

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

        RequestPlaceHeldIngredientServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestPlaceHeldIngredientServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!TryGetSenderPickup(rpcParams.Receive.SenderClientId, out PlayerPickup playerPickup))
        {
            Log($"Could not find PlayerPickup for client {rpcParams.Receive.SenderClientId}.");
            return;
        }

        ServerTryPlaceHeldIngredient(playerPickup);
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

    public bool ServerTrySnapIngredient(FoodIngredient ingredient)
    {
        if (!IsServerActive()) return false;
        if (!CanSnapIngredient(ingredient, allowHeld: false, out _))
        {
            return false;
        }

        snappedIngredients.RemoveAll(snappedIngredient => snappedIngredient == null);

        Vector3 localPosition = GetLocalSnapPosition(snappedIngredients.Count);
        Quaternion localRotation = Quaternion.Euler(snappedLocalEulerAngles);

        snappedIngredients.Add(ingredient);
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

    public WrappedFoodItem ServerWrapFood()
    {
        if (!IsServerActive()) return null;
        if (foodToWrap == null)
        {
            Debug.LogWarning("[FoodAssemblyBase] Food To Wrap is missing.", this);
            return null;
        }

        if (foodToWrap.WrappedFoodPrefab == null)
        {
            Debug.LogWarning($"[FoodAssemblyBase] {foodToWrap.FoodName} has no wrapped food prefab.", this);
            return null;
        }

        Vector3 spawnPosition = wrappedFoodSpawnPoint != null ? wrappedFoodSpawnPoint.position : transform.position;
        Quaternion spawnRotation = wrappedFoodSpawnPoint != null ? wrappedFoodSpawnPoint.rotation : transform.rotation;

        GameObject wrappedObject = Instantiate(foodToWrap.WrappedFoodPrefab, spawnPosition, spawnRotation);
        WrappedFoodItem wrappedFood = wrappedObject.GetComponent<WrappedFoodItem>();

        if (wrappedFood == null)
        {
            Debug.LogWarning($"[FoodAssemblyBase] {foodToWrap.WrappedFoodPrefab.name} is missing WrappedFoodItem.", this);
            Destroy(wrappedObject);
            return null;
        }

        NetworkObject wrappedNetworkObject = wrappedObject.GetComponent<NetworkObject>();

        if (wrappedNetworkObject != null)
        {
            wrappedNetworkObject.Spawn();
        }

        wrappedFood.ServerInitialize(foodToWrap, GetIngredientsForWrap(), despawnIngredientsWhenWrapped);
        return wrappedFood;
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

        if (foodToWrap != null && !foodToWrap.AllowsIngredient(ingredient.Definition))
        {
            reason = $"{ingredient.Definition.IngredientName} is not allowed by {foodToWrap.FoodName}.";
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

    private IReadOnlyList<FoodIngredient> GetIngredientsForWrap()
    {
        wrapIngredients.Clear();

        if (baseIngredient != null && baseIngredient.HasDefinition)
        {
            wrapIngredients.Add(baseIngredient);
        }

        snappedIngredients.RemoveAll(ingredient => ingredient == null);

        for (int i = 0; i < snappedIngredients.Count; i++)
        {
            FoodIngredient ingredient = snappedIngredients[i];

            if (ingredient != null && ingredient.HasDefinition)
            {
                wrapIngredients.Add(ingredient);
            }
        }

        return wrapIngredients;
    }

    private Vector3 GetLocalSnapPosition(int snappedIngredientIndex)
    {
        Vector3 direction = stackDirection.sqrMagnitude > 0f ? stackDirection.normalized : Vector3.up;
        return firstIngredientLocalOffset + direction * ingredientSpacing * snappedIngredientIndex;
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
            return;
        }

        FoodIngredient ingredient = ingredientNetworkObject.GetComponent<FoodIngredient>();
        ApplySnappedPose(ingredient, localPosition, localRotation);
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

    private void Log(string message)
    {
        Debug.LogWarning($"[FoodAssemblyBase] {message}", this);
    }
}
