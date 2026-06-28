using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(Collider))]
public class TrashIngredientSpawner : MonoBehaviour
{
    [Header("Trash Pool")]
    [SerializeField] private List<FoodIngredientDefinition> trashIngredients = new();

    [Header("Spawn")]
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private bool spawnOnStart = true;
    [SerializeField] private bool restockWhenTaken = true;
    [SerializeField] private float restockDelay = 0.25f;
    [SerializeField] private bool logDebugMessages = true;

    private Item spawnedItem;
    private float nextRestockTime;

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
        if (!spawnOnStart) return;
        if (!IsServerActive()) return;

        TrySpawnRandomTrashIngredient();
    }

    private void Update()
    {
        if (!restockWhenTaken) return;
        if (!IsServerActive()) return;
        if (Time.time < nextRestockTime) return;

        if (spawnedItem == null)
        {
            TrySpawnRandomTrashIngredient();
            return;
        }

        if (spawnedItem.IsHeld)
        {
            MarkCurrentItemTaken();
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!restockWhenTaken) return;
        if (!IsServerActive()) return;
        if (spawnedItem == null) return;

        Item item = other.GetComponentInParent<Item>();

        if (item != spawnedItem) return;

        MarkCurrentItemTaken();
    }

    public bool TrySpawnRandomTrashIngredient()
    {
        if (!IsServerActive()) return false;

        if (spawnPoint == null)
        {
            Log("Spawn Point is missing.");
            return false;
        }

        if (spawnedItem != null)
        {
            return false;
        }

        FoodIngredientDefinition ingredient = GetRandomTrashIngredient();

        if (ingredient == null)
        {
            Log("No valid trash ingredients are assigned.");
            return false;
        }

        GameObject prefab = ingredient.IngredientPrefab;

        if (prefab == null)
        {
            Log($"{ingredient.IngredientName} has no Ingredient Prefab assigned.");
            return false;
        }

        if (prefab.GetComponent<NetworkObject>() == null)
        {
            Debug.LogWarning($"[TrashIngredientSpawner] {prefab.name} is missing a NetworkObject.", this);
            return false;
        }

        if (prefab.GetComponent<Item>() == null)
        {
            Debug.LogWarning($"[TrashIngredientSpawner] {prefab.name} is missing an Item component.", this);
            return false;
        }

        GameObject spawnedObject = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);
        NetworkObject networkObject = spawnedObject.GetComponent<NetworkObject>();
        networkObject.Spawn();

        FoodIngredient foodIngredient = spawnedObject.GetComponent<FoodIngredient>();

        if (foodIngredient != null)
        {
            foodIngredient.SetDefinition(ingredient);
        }

        spawnedItem = spawnedObject.GetComponent<Item>();
        Log($"Spawned {ingredient.IngredientName}.");
        return true;
    }

    private FoodIngredientDefinition GetRandomTrashIngredient()
    {
        if (trashIngredients == null || trashIngredients.Count == 0) return null;

        List<FoodIngredientDefinition> validIngredients = new();

        for (int i = 0; i < trashIngredients.Count; i++)
        {
            FoodIngredientDefinition ingredient = trashIngredients[i];

            if (ingredient == null) continue;

            if (!ingredient.IsTrash)
            {
                Debug.LogWarning($"[TrashIngredientSpawner] {ingredient.IngredientName} is not marked as Trash.", this);
                continue;
            }

            if (ingredient.IngredientPrefab == null)
            {
                Debug.LogWarning($"[TrashIngredientSpawner] {ingredient.IngredientName} has no Ingredient Prefab assigned.", this);
                continue;
            }

            validIngredients.Add(ingredient);
        }

        if (validIngredients.Count == 0) return null;

        return validIngredients[Random.Range(0, validIngredients.Count)];
    }

    private void MarkCurrentItemTaken()
    {
        spawnedItem = null;
        nextRestockTime = Time.time + restockDelay;
    }

    private void Log(string message)
    {
        if (!logDebugMessages) return;

        Debug.Log($"[TrashIngredientSpawner] {message}", this);
    }

    private bool IsServerActive()
    {
        return NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    }
}
