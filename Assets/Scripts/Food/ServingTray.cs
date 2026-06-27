using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Item))]
[RequireComponent(typeof(NetworkObject))]
public class ServingTray : NetworkBehaviour, IInteractable
{
    private const ulong NoFood = ulong.MaxValue;

    [Header("Food Snap")]
    [SerializeField] private Transform foodSnapRoot;
    [SerializeField] private Vector3 foodLocalPosition = new Vector3(0f, 0.05f, 0f);
    [SerializeField] private Vector3 foodLocalEulerAngles;

    private readonly List<FoodIngredient> emptyIngredients = new();
    private FoodAssemblyBase carriedFood;
    private ulong pendingFoodNetworkObjectId = NoFood;

    private NetworkVariable<ulong> carriedFoodNetworkObjectId = new(
        NoFood,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public FoodAssemblyBase CarriedFood => carriedFood;
    public FoodItemDefinition CarriedFoodDefinition => carriedFood != null ? carriedFood.FoodDefinition : null;
    public bool HasFood => carriedFoodNetworkObjectId.Value != NoFood;

    private void Reset()
    {
        Item item = GetComponent<Item>();

        if (item != null)
        {
            item.itemName = "Serving Tray";
            item.itemType = Item.ItemType.Utensil;
        }
    }

    public void Interact(PlayerInteraction interactor)
    {
        if (!IsSpawned)
        {
            return;
        }

        RequestLoadHeldFoodServerRpc();
    }

    public override void OnNetworkSpawn()
    {
        carriedFoodNetworkObjectId.OnValueChanged += OnCarriedFoodChanged;

        if (carriedFoodNetworkObjectId.Value != NoFood)
        {
            TryApplyCarriedFood(carriedFoodNetworkObjectId.Value);
        }
    }

    public override void OnNetworkDespawn()
    {
        carriedFoodNetworkObjectId.OnValueChanged -= OnCarriedFoodChanged;
        carriedFood = null;
        pendingFoodNetworkObjectId = NoFood;
    }

    private void Update()
    {
        if (pendingFoodNetworkObjectId == NoFood) return;

        TryApplyCarriedFood(pendingFoodNetworkObjectId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestLoadHeldFoodServerRpc(ServerRpcParams rpcParams = default)
    {
        if (!TryGetSenderPickup(rpcParams.Receive.SenderClientId, out PlayerPickup playerPickup))
        {
            return;
        }

        ServerTryLoadHeldFood(playerPickup);
    }

    public bool ServerTryLoadHeldFood(PlayerPickup playerPickup)
    {
        if (!IsServer) return false;
        if (playerPickup == null) return false;
        if (HasFood) return false;

        if (!playerPickup.ServerTryGetHeldItem(out Item heldItem))
        {
            return false;
        }

        FoodAssemblyBase food = GetFoodAssemblyBase(heldItem);

        if (food == null)
        {
            return false;
        }

        if (!CanLoadFood(food))
        {
            return false;
        }

        if (!playerPickup.ServerTryReleaseHeldItemPreserveWorldPose(heldItem))
        {
            return false;
        }

        return ServerTryLoadFood(food);
    }

    public bool ServerTryLoadFood(FoodAssemblyBase food)
    {
        if (!IsServer) return false;
        if (food == null) return false;
        if (!CanLoadFood(food)) return false;

        carriedFoodNetworkObjectId.Value = food.NetworkObject.NetworkObjectId;
        TryApplyCarriedFood(food.NetworkObject.NetworkObjectId);

        return true;
    }

    public bool ServerTryUnloadFood(FoodAssemblyBase food)
    {
        if (!IsServer) return false;
        if (food == null) return false;

        if (!TryResolveCarriedFood(out FoodAssemblyBase resolvedFood))
        {
            return false;
        }

        if (resolvedFood != food)
        {
            return false;
        }

        food.SetCurrentServingTray(null);
        carriedFood = null;
        pendingFoodNetworkObjectId = NoFood;
        carriedFoodNetworkObjectId.Value = NoFood;

        return true;
    }

    public IReadOnlyList<FoodIngredient> GetCarriedIngredients()
    {
        if (carriedFood != null)
        {
            return carriedFood.GetAssembledIngredients();
        }

        emptyIngredients.Clear();
        return emptyIngredients;
    }

    private void OnCarriedFoodChanged(ulong previousFoodNetworkObjectId, ulong newFoodNetworkObjectId)
    {
        if (carriedFood != null && carriedFood.NetworkObject != null &&
            carriedFood.NetworkObject.NetworkObjectId == previousFoodNetworkObjectId)
        {
            carriedFood.SetCurrentServingTray(null);
        }

        if (newFoodNetworkObjectId == NoFood)
        {
            carriedFood = null;
            pendingFoodNetworkObjectId = NoFood;
            return;
        }

        TryApplyCarriedFood(newFoodNetworkObjectId);
    }

    private bool TryApplyCarriedFood(ulong foodNetworkObjectId)
    {
        if (foodNetworkObjectId == NoFood)
        {
            carriedFood = null;
            pendingFoodNetworkObjectId = NoFood;
            return true;
        }

        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                foodNetworkObjectId,
                out NetworkObject foodNetworkObject))
        {
            pendingFoodNetworkObjectId = foodNetworkObjectId;
            return false;
        }

        FoodAssemblyBase food = foodNetworkObject.GetComponent<FoodAssemblyBase>();

        if (food == null)
        {
            pendingFoodNetworkObjectId = NoFood;
            return false;
        }

        carriedFood = food;
        carriedFood.SetCurrentServingTray(this);
        ApplyFoodPose(carriedFood);
        pendingFoodNetworkObjectId = NoFood;

        return true;
    }

    private bool TryResolveCarriedFood(out FoodAssemblyBase food)
    {
        food = carriedFood;

        if (food != null &&
            food.NetworkObject != null &&
            food.NetworkObject.IsSpawned &&
            food.NetworkObject.NetworkObjectId == carriedFoodNetworkObjectId.Value)
        {
            return true;
        }

        food = null;

        if (carriedFoodNetworkObjectId.Value == NoFood)
        {
            return false;
        }

        if (NetworkManager.Singleton == null ||
            !NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(
                carriedFoodNetworkObjectId.Value,
                out NetworkObject foodNetworkObject))
        {
            return false;
        }

        food = foodNetworkObject.GetComponent<FoodAssemblyBase>();
        carriedFood = food;

        return food != null;
    }

    private bool CanLoadFood(FoodAssemblyBase food)
    {
        if (food == null) return false;
        if (HasFood) return false;
        if (food.IsOnServingTray) return false;
        if (!food.HasAnyIngredientData()) return false;

        return food.NetworkObject != null && food.NetworkObject.IsSpawned;
    }

    private void ApplyFoodPose(FoodAssemblyBase food)
    {
        if (food == null) return;

        Transform parent = foodSnapRoot != null ? foodSnapRoot : transform;
        Quaternion localRotation = Quaternion.Euler(foodLocalEulerAngles);
        Item foodItem = food.GetComponent<Item>();

        if (foodItem != null)
        {
            foodItem.LockLocalParentPreserveWorldScale(parent, foodLocalPosition, localRotation);
        }
        else
        {
            Vector3 worldScale = food.transform.lossyScale;
            food.transform.SetParent(parent, worldPositionStays: false);
            food.transform.localPosition = foodLocalPosition;
            food.transform.localRotation = localRotation;
            SetLocalScaleForWorldScale(food.transform, worldScale, parent);
        }

        Rigidbody rb = food.GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        NetworkTransform networkTransform = food.GetComponent<NetworkTransform>();

        if (networkTransform != null)
        {
            networkTransform.enabled = false;
        }

        food.RefreshSnappedIngredientLayout();
        SetFoodCollidersForTray(food);
    }

    private void SetFoodCollidersForTray(FoodAssemblyBase food)
    {
        if (food == null) return;

        Collider[] colliders = food.GetComponentsInChildren<Collider>();

        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = colliders[i].transform == food.transform;
        }
    }

    private FoodAssemblyBase GetFoodAssemblyBase(Item item)
    {
        if (item == null) return null;

        FoodAssemblyBase food = item.GetComponent<FoodAssemblyBase>();

        if (food != null)
        {
            return food;
        }

        food = item.GetComponentInChildren<FoodAssemblyBase>();

        if (food != null)
        {
            return food;
        }

        return item.GetComponentInParent<FoodAssemblyBase>();
    }

    private void SetLocalScaleForWorldScale(Transform target, Vector3 worldScale, Transform parent)
    {
        if (target == null) return;

        if (parent == null)
        {
            target.localScale = worldScale;
            return;
        }

        Vector3 parentScale = parent.lossyScale;

        target.localScale = new Vector3(
            DivideScale(worldScale.x, parentScale.x),
            DivideScale(worldScale.y, parentScale.y),
            DivideScale(worldScale.z, parentScale.z)
        );
    }

    private float DivideScale(float value, float divisor)
    {
        return Mathf.Abs(divisor) > 0.0001f ? value / divisor : value;
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
}
