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
    private bool reportedInvalidSetup;

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
        if (boxPrefab == null || !boxPrefab.HasValidContents ||
            boxPrefab.GetComponent<NetworkObject>() == null || boxPrefab.GetComponent<Item>() == null ||
            !manager.NetworkConfig.Prefabs.Contains(boxPrefab.gameObject))
        {
            if (!reportedInvalidSetup)
                Debug.LogError("Assign a configured IngredientBox prefab and register it in the NetworkManager's prefab list.", this);
            reportedInvalidSetup = true;
            return;
        }

        reportedInvalidSetup = false;
        Transform marker = spawnPoint != null ? spawnPoint : transform;
        stockedPosition = marker.position;
        stockedBox = Instantiate(boxPrefab, marker.position, marker.rotation);
        stockedItem = stockedBox.GetComponent<Item>();
        stockedBox.NetworkObject.Spawn(destroyWithScene: true);
    }

    private void OnDrawGizmosSelected()
    {
        Transform marker = spawnPoint != null ? spawnPoint : transform;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(marker.position, Mathf.Max(0.05f, removalDistance));
        Gizmos.DrawRay(marker.position, marker.forward * 0.5f);
    }
}
