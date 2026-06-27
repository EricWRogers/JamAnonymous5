using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class SpawnProp : MonoBehaviour
{
    [Header("Item Purchase")]
    [SerializeField] private GameObject itemPrefab;
    [SerializeField] private Transform itemSpawnPoint;
    [SerializeField] private int itemCost = 10;
    [SerializeField] private float restockDelay = 0.25f;
    [SerializeField] private bool logDebugMessages = true;

    private Item spawnedItem;
    private float nextRestockTime;
    private bool isRestockingBlocked;
    private string pendingChargeItemName;

    private bool HasItemPurchaseSetup => itemPrefab != null && itemSpawnPoint != null;

    private void Awake()
    {
        Collider triggerCollider = GetComponent<Collider>();

        if (triggerCollider != null)
        {
            triggerCollider.isTrigger = true;
        }
    }

    private void Start()
    {
        if (!IsServerActive()) return;

        TryRestock();
    }

    private void Update()
    {
        if (!IsServerActive()) return;
        if (Time.time < nextRestockTime) return;

        if (isRestockingBlocked)
        {
            TryPayPendingCharge();
            return;
        }

        if (spawnedItem == null)
        {
            TryRestock();
            return;
        }

        if (spawnedItem.IsHeld)
        {
            ChargeForRemovedItem("picked up");
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServerActive()) return;
        if (spawnedItem == null) return;

        Item item = other.GetComponentInParent<Item>();

        if (item != spawnedItem) return;

        ChargeForRemovedItem("left the spawner trigger");
    }

    private bool TryRestock()
    {
        if (!HasItemPurchaseSetup)
        {
            Log("Item purchase setup is missing. Assign Item Prefab and Item Spawn Point.");
            return false;
        }

        if (spawnedItem != null)
        {
            return false;
        }

        if (isRestockingBlocked)
        {
            return false;
        }

        if (itemPrefab.GetComponent<NetworkObject>() == null)
        {
            Debug.LogWarning($"[Server] Spawned item prefab {itemPrefab.name} is missing a NetworkObject.");
            return false;
        }

        if (itemPrefab.GetComponent<Item>() == null)
        {
            Debug.LogWarning($"[Server] Spawned item prefab {itemPrefab.name} is missing an Item component.");
            return false;
        }

        GameObject item = Instantiate(itemPrefab, itemSpawnPoint.position, itemSpawnPoint.rotation);
        NetworkObject itemNetworkObject = item.GetComponent<NetworkObject>();
        itemNetworkObject.Spawn();

        spawnedItem = item.GetComponent<Item>();

        Log($"Restocked {itemPrefab.name} at {itemSpawnPoint.position}.");
        return true;
    }

    private RestaurantMoney GetRestaurantMoney()
    {
        if (RestaurantMoney.Instance != null)
        {
            return RestaurantMoney.Instance;
        }

        return FindFirstObjectByType<RestaurantMoney>();
    }

    private void ChargeForRemovedItem(string reason)
    {
        Item removedItem = spawnedItem;
        spawnedItem = null;
        nextRestockTime = Time.time + restockDelay;

        if (itemCost <= 0)
        {
            Log($"{removedItem.itemName} {reason}. No charge because item cost is 0.");
            return;
        }

        if (!TryChargeRestaurantMoney(removedItem.itemName))
        {
            isRestockingBlocked = true;
            pendingChargeItemName = removedItem.itemName;
            return;
        }

        Log($"{removedItem.itemName} {reason}. Charged restaurant ${itemCost}.");
    }

    private bool TryPayPendingCharge()
    {
        if (itemCost <= 0)
        {
            isRestockingBlocked = false;
            pendingChargeItemName = null;
            return true;
        }

        if (!TryChargeRestaurantMoney(pendingChargeItemName))
        {
            return false;
        }

        Log($"Paid pending charge for {pendingChargeItemName}. Restocking resumed.");
        isRestockingBlocked = false;
        pendingChargeItemName = null;
        nextRestockTime = Time.time + restockDelay;
        return true;
    }

    private bool TryChargeRestaurantMoney(string itemName)
    {
        RestaurantMoney restaurantMoney = GetRestaurantMoney();

        if (restaurantMoney == null)
        {
            Debug.LogWarning("[Server] RestaurantMoney is missing, so the removed item cannot be charged.");
            return false;
        }

        if (!restaurantMoney.ServerTrySpend(itemCost))
        {
            Debug.Log($"[Server] Restaurant cannot afford {itemName}. Restocking is paused.");
            return false;
        }

        return true;
    }

    private void Log(string message)
    {
        if (!logDebugMessages) return;

        Debug.Log($"[SpawnProp] {message}", this);
    }

    private bool IsServerActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    }
}
