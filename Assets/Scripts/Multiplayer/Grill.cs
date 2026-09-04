using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class Grill : NetworkBehaviour, IInteractable
{
    private const ulong NoItem = ulong.MaxValue;

    [Header("Cooking")]
    [Tooltip("Normalized cooking progress added per second. 0.05 reaches cooked in 10 seconds and burnt in 20 seconds.")]
    [SerializeField] private float cookSpeed = 0.05f;

    [Header("Placement")]
    [Tooltip("Each transform is one usable grill slot. The number of valid transforms is the grill capacity.")]
    [SerializeField] private List<Transform> cookingPoints = new();
    [FormerlySerializedAs("itemWorldEulerAngles")]
    [Tooltip("Rotation applied relative to each cooking point.")]
    [SerializeField] private Vector3 itemLocalEulerAngles;
    [SerializeField] private float serverInteractRange = 4f;

    private readonly NetworkList<ulong> cookingItemIds = new(
        null,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );
    private readonly List<FoodIngredient> cookingIngredients = new();
    private bool hasPendingSlotObjects;
    private bool isInitializingSlots;

    public int SlotCount => cookingPoints != null ? cookingPoints.Count : 0;
    public bool HasCookingItem => GetOccupiedSlotCount() > 0;
    public bool HasAvailableSlot => FindFirstAvailableSlot() >= 0;

    public override void OnNetworkSpawn()
    {
        cookingItemIds.OnListChanged += OnCookingSlotsChanged;

        if (IsServer)
        {
            InitializeNetworkSlots();
        }

        ReconcileCookingSlots();
    }

    public override void OnNetworkDespawn()
    {
        cookingItemIds.OnListChanged -= OnCookingSlotsChanged;

        for (int i = 0; i < cookingIngredients.Count; i++)
        {
            FoodIngredient ingredient = cookingIngredients[i];

            if (ingredient != null && ingredient.CurrentGrill == this)
            {
                ingredient.SetCurrentGrill(null);
            }
        }

        cookingIngredients.Clear();
        hasPendingSlotObjects = false;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (hasPendingSlotObjects)
        {
            ReconcileCookingSlots();
        }

        if (!IsServer) return;

        int slotCount = Mathf.Min(SlotCount, cookingItemIds.Count);

        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            ulong itemId = cookingItemIds[slotIndex];

            if (itemId == NoItem) continue;

            if (!TryResolveIngredient(itemId, out FoodIngredient ingredient))
            {
                hasPendingSlotObjects = true;
                continue;
            }

            Item item = ingredient.GetComponent<Item>();

            if (item == null || item.IsHeld)
            {
                ClearSlot(slotIndex);
                continue;
            }

            ingredient.ServerAdvanceCooking(Mathf.Max(0f, cookSpeed) * Time.deltaTime);
        }
    }

    public void Interact(PlayerInteraction interactor)
    {
        if (!IsSpawned) return;
        RequestUseGrillRpc();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestUseGrillRpc(RpcParams rpcParams = default)
    {
        if (!TryGetSenderPickup(rpcParams.Receive.SenderClientId, out PlayerPickup playerPickup)) return;
        if (!IsPlayerCloseEnough(playerPickup)) return;

        ServerTryUseWithPlayer(playerPickup);
    }

    public bool ServerTryUseWithPlayer(PlayerPickup playerPickup)
    {
        if (!IsServer || playerPickup == null) return false;

        if (playerPickup.ServerTryGetHeldItem(out Item heldItem))
        {
            return ServerTryPlaceHeldItem(playerPickup, heldItem);
        }

        return ServerTryTakeCookingItem(playerPickup);
    }

    public bool ServerTryRemoveItem(FoodIngredient ingredient)
    {
        if (!IsServer || ingredient == null || ingredient.NetworkObject == null) return false;

        int slotIndex = FindSlotContaining(ingredient.NetworkObject.NetworkObjectId);
        if (slotIndex < 0) return false;

        ClearSlot(slotIndex);
        return true;
    }

    private bool ServerTryPlaceHeldItem(PlayerPickup playerPickup, Item heldItem)
    {
        if (heldItem == null) return false;

        FoodIngredient ingredient = heldItem.GetComponent<FoodIngredient>();

        if (ingredient == null ||
            ingredient.NetworkObject == null ||
            !ingredient.CanBeCooked ||
            ingredient.IsOnGrill)
        {
            return false;
        }

        int slotIndex = FindFirstAvailableSlot();
        if (slotIndex < 0) return false;

        if (!playerPickup.ServerTryReleaseHeldItemPreserveWorldPose(heldItem)) return false;

        cookingItemIds[slotIndex] = ingredient.NetworkObject.NetworkObjectId;
        return true;
    }

    private bool ServerTryTakeCookingItem(PlayerPickup playerPickup)
    {
        int slotIndex = FindClosestOccupiedSlot(playerPickup.transform.position);
        if (slotIndex < 0) return false;

        ulong previousItemId = cookingItemIds[slotIndex];

        if (!TryResolveIngredient(previousItemId, out FoodIngredient ingredient)) return false;

        Item item = ingredient.GetComponent<Item>();
        if (item == null) return false;

        ClearSlot(slotIndex);

        if (playerPickup.ServerTryPickUpItem(item))
        {
            return true;
        }

        cookingItemIds[slotIndex] = previousItemId;
        return false;
    }

    private void InitializeNetworkSlots()
    {
        isInitializingSlots = true;

        while (cookingItemIds.Count < SlotCount)
        {
            cookingItemIds.Add(NoItem);
        }

        while (cookingItemIds.Count > SlotCount)
        {
            cookingItemIds.RemoveAt(cookingItemIds.Count - 1);
        }

        isInitializingSlots = false;
    }

    private void OnCookingSlotsChanged(NetworkListEvent<ulong> changeEvent)
    {
        if (isInitializingSlots) return;
        ReconcileCookingSlots();
    }

    private void ReconcileCookingSlots()
    {
        EnsureLocalSlotCount();
        hasPendingSlotObjects = false;

        for (int slotIndex = 0; slotIndex < SlotCount; slotIndex++)
        {
            ulong expectedItemId = slotIndex < cookingItemIds.Count
                ? cookingItemIds[slotIndex]
                : NoItem;
            FoodIngredient previousIngredient = cookingIngredients[slotIndex];

            if (previousIngredient != null && !IngredientMatchesId(previousIngredient, expectedItemId))
            {
                if (previousIngredient.CurrentGrill == this)
                {
                    previousIngredient.SetCurrentGrill(null);
                }

                previousIngredient.GetComponent<Item>()?.UnlockLocalParent();
                cookingIngredients[slotIndex] = null;
            }

            if (expectedItemId == NoItem)
            {
                continue;
            }

            if (!TryApplyCookingSlot(slotIndex, expectedItemId))
            {
                hasPendingSlotObjects = true;
            }
        }
    }

    private bool TryApplyCookingSlot(int slotIndex, ulong itemId)
    {
        if (!IsValidSlot(slotIndex)) return false;
        if (!TryResolveIngredient(itemId, out FoodIngredient ingredient)) return false;

        Item item = ingredient.GetComponent<Item>();
        if (item == null) return false;

        FoodIngredient previousIngredient = cookingIngredients[slotIndex];

        if (previousIngredient != null && previousIngredient != ingredient)
        {
            if (previousIngredient.CurrentGrill == this)
            {
                previousIngredient.SetCurrentGrill(null);
            }

            previousIngredient.GetComponent<Item>()?.UnlockLocalParent();
        }

        cookingIngredients[slotIndex] = ingredient;
        ingredient.SetCurrentGrill(this);
        item.LockLocalParentPreserveWorldScale(
            cookingPoints[slotIndex],
            Vector3.zero,
            Quaternion.Euler(itemLocalEulerAngles)
        );

        return true;
    }

    private void ClearSlot(int slotIndex)
    {
        if (!IsServer || slotIndex < 0 || slotIndex >= cookingItemIds.Count) return;
        cookingItemIds[slotIndex] = NoItem;
    }

    private int FindFirstAvailableSlot()
    {
        int slotCount = Mathf.Min(SlotCount, cookingItemIds.Count);

        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            if (IsValidSlot(slotIndex) && cookingItemIds[slotIndex] == NoItem)
            {
                return slotIndex;
            }
        }

        return -1;
    }

    private int FindClosestOccupiedSlot(Vector3 worldPosition)
    {
        int closestSlot = -1;
        float closestSqrDistance = float.MaxValue;
        int slotCount = Mathf.Min(SlotCount, cookingItemIds.Count);

        for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
        {
            if (!IsValidSlot(slotIndex) || cookingItemIds[slotIndex] == NoItem) continue;

            float sqrDistance = (cookingPoints[slotIndex].position - worldPosition).sqrMagnitude;

            if (sqrDistance >= closestSqrDistance) continue;

            closestSqrDistance = sqrDistance;
            closestSlot = slotIndex;
        }

        return closestSlot;
    }

    private int FindSlotContaining(ulong itemId)
    {
        for (int slotIndex = 0; slotIndex < cookingItemIds.Count; slotIndex++)
        {
            if (cookingItemIds[slotIndex] == itemId)
            {
                return slotIndex;
            }
        }

        return -1;
    }

    private int GetOccupiedSlotCount()
    {
        int occupiedCount = 0;

        for (int slotIndex = 0; slotIndex < cookingItemIds.Count; slotIndex++)
        {
            if (cookingItemIds[slotIndex] != NoItem)
            {
                occupiedCount++;
            }
        }

        return occupiedCount;
    }

    private bool IsValidSlot(int slotIndex)
    {
        return cookingPoints != null &&
               slotIndex >= 0 &&
               slotIndex < cookingPoints.Count &&
               cookingPoints[slotIndex] != null;
    }

    private void EnsureLocalSlotCount()
    {
        while (cookingIngredients.Count < SlotCount)
        {
            cookingIngredients.Add(null);
        }

        while (cookingIngredients.Count > SlotCount)
        {
            int lastIndex = cookingIngredients.Count - 1;
            FoodIngredient ingredient = cookingIngredients[lastIndex];

            if (ingredient != null && ingredient.CurrentGrill == this)
            {
                ingredient.SetCurrentGrill(null);
                ingredient.GetComponent<Item>()?.UnlockLocalParent();
            }

            cookingIngredients.RemoveAt(lastIndex);
        }
    }

    private bool IngredientMatchesId(FoodIngredient ingredient, ulong itemId)
    {
        return itemId != NoItem &&
               ingredient != null &&
               ingredient.NetworkObject != null &&
               ingredient.NetworkObject.IsSpawned &&
               ingredient.NetworkObject.NetworkObjectId == itemId;
    }

    private bool TryResolveIngredient(ulong networkObjectId, out FoodIngredient ingredient)
    {
        ingredient = null;
        if (networkObjectId == NoItem || NetworkManager.Singleton == null) return false;
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject networkObject)) return false;

        ingredient = networkObject.GetComponent<FoodIngredient>();
        return ingredient != null;
    }

    private bool IsPlayerCloseEnough(PlayerPickup playerPickup)
    {
        float allowedRange = Mathf.Max(0f, serverInteractRange);
        return playerPickup != null && Vector3.Distance(playerPickup.transform.position, transform.position) <= allowedRange;
    }

    private bool TryGetSenderPickup(ulong senderClientId, out PlayerPickup playerPickup)
    {
        playerPickup = null;
        if (NetworkManager.Singleton == null) return false;
        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client)) return false;
        if (client.PlayerObject == null) return false;

        playerPickup = client.PlayerObject.GetComponent<PlayerPickup>();
        if (playerPickup != null) return true;

        playerPickup = client.PlayerObject.GetComponentInChildren<PlayerPickup>();
        if (playerPickup != null) return true;

        playerPickup = client.PlayerObject.GetComponentInParent<PlayerPickup>();
        return playerPickup != null;
    }
}
