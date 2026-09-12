using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class IngredientBoxSpawner : MonoBehaviour
{
    [SerializeField, Tooltip("Configured TomatoBox, OnionBox, or other networked box prefab.")]
    private IngredientBox boxPrefab;
    [SerializeField, Tooltip("Defaults to this object's position and rotation when unassigned.")]
    private Transform spawnPoint;
    [SerializeField, Min(0f)] private float restockDelay = 0.5f;
    [SerializeField, Min(0.05f), Tooltip("How far a purchased box must move before the shelf restocks.")]
    private float removalDistance = 0.4f;

    private IngredientBox stockedBox;
    private Item stockedItem;
    private Vector3 stockedPosition;
    private float nextSpawnTime;
    private string reportedSetupError;

    private void Update()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsListening || !manager.IsServer) return;

        if (stockedBox != null)
        {
            bool removed = !stockedBox.IsSpawned ||
                (stockedBox.IsPurchased && (stockedItem.IsHeld ||
                 Vector3.Distance(stockedBox.transform.position, stockedPosition) >= removalDistance));
            if (!removed) return;

            // The purchased box remains in the world with its current contents.
            stockedBox = null;
            stockedItem = null;
            nextSpawnTime = Time.time + Mathf.Max(0f, restockDelay);
        }

        if (Time.time < nextSpawnTime) return;
        nextSpawnTime = Time.time + 1f;
        string setupError = GetSetupError(manager);
        if (setupError != null)
        {
            if (reportedSetupError != setupError) Debug.LogError(setupError, this);
            reportedSetupError = setupError;
            return;
        }

        reportedSetupError = null;
        Transform marker = spawnPoint != null ? spawnPoint : transform;
        stockedPosition = marker.position;
        stockedBox = Instantiate(boxPrefab, marker.position, marker.rotation);
        stockedItem = stockedBox.GetComponent<Item>();
        stockedBox.NetworkObject.Spawn(destroyWithScene: true);
    }

    private string GetSetupError(NetworkManager manager)
    {
        if (boxPrefab == null) return "Assign a Box Prefab to this IngredientBoxSpawner.";
        if (boxPrefab.GetComponent<NetworkObject>() == null || boxPrefab.GetComponent<Item>() == null)
            return $"Box prefab '{boxPrefab.name}' requires NetworkObject and Item components on its root.";
        if (!boxPrefab.HasValidContents)
            return $"Box prefab '{boxPrefab.name}' has invalid contents. Assign an Ingredient Prefab with FoodIngredient, Item, and NetworkObject on its root; a mesh-only prefab cannot be dispensed.";
        if (!manager.NetworkConfig.Prefabs.Contains(boxPrefab.gameObject))
            return $"Box prefab '{boxPrefab.name}' is missing from the active NetworkManager's network prefab lists.";
        return null;
    }

    private void OnDrawGizmosSelected()
    {
        Transform marker = spawnPoint != null ? spawnPoint : transform;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(marker.position, Mathf.Max(0.05f, removalDistance));
        Gizmos.DrawRay(marker.position, marker.forward * 0.5f);
    }
}
