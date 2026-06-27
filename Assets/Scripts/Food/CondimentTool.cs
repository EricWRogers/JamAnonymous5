using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Item))]
[RequireComponent(typeof(NetworkObject))]
public class CondimentTool : NetworkBehaviour
{
    [Header("Condiment")]
    [SerializeField] private FoodIngredientDefinition condimentIngredient;
    [Tooltip("Optional override. If empty, the prefab from the condiment ingredient definition is used.")]
    [SerializeField] private GameObject condimentPrefabOverride;
    [SerializeField] private GameObject burgerCondimentPrefab;
    [SerializeField] private GameObject hotdogCondimentPrefab;

    public FoodIngredientDefinition CondimentIngredient => condimentIngredient;

    private void Reset()
    {
        Item item = GetComponent<Item>();

        if (item != null)
        {
            item.itemType = Item.ItemType.Utensil;
        }
    }

    public bool ServerTryApplyTo(FoodAssemblyBase assemblyBase)
    {
        if (!IsServer) return false;
        if (assemblyBase == null) return false;
        if (assemblyBase.IsOnServingTray) return false;

        GameObject condimentPrefab = GetCondimentPrefab(assemblyBase);

        if (condimentPrefab == null)
        {
            Debug.LogWarning("[CondimentTool] Missing condiment prefab.", this);
            return false;
        }

        GameObject condimentObject = Instantiate(
            condimentPrefab,
            assemblyBase.transform.position,
            assemblyBase.transform.rotation
        );

        FoodIngredient condiment = condimentObject.GetComponent<FoodIngredient>();

        if (condiment == null)
        {
            Debug.LogWarning($"[CondimentTool] {condimentPrefab.name} is missing FoodIngredient.", this);
            Destroy(condimentObject);
            return false;
        }

        if (condimentIngredient != null)
        {
            condiment.SetDefinition(condimentIngredient);
        }

        NetworkObject condimentNetworkObject = condimentObject.GetComponent<NetworkObject>();

        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            if (condimentNetworkObject == null)
            {
                Debug.LogWarning($"[CondimentTool] {condimentPrefab.name} is missing NetworkObject.", this);
                Destroy(condimentObject);
                return false;
            }

            condimentNetworkObject.Spawn();
        }

        if (assemblyBase.ServerTrySnapIngredient(condiment))
        {
            return true;
        }

        DestroyCondimentObject(condimentObject, condimentNetworkObject);
        return false;
    }

    private GameObject GetCondimentPrefab(FoodAssemblyBase assemblyBase)
    {
        FoodKind? foodKind = assemblyBase != null && assemblyBase.FoodDefinition != null
            ? assemblyBase.FoodDefinition.FoodKind
            : null;

        if (foodKind == FoodKind.Burger && burgerCondimentPrefab != null)
        {
            return burgerCondimentPrefab;
        }

        if (foodKind == FoodKind.Hotdog && hotdogCondimentPrefab != null)
        {
            return hotdogCondimentPrefab;
        }

        if (condimentPrefabOverride != null)
        {
            return condimentPrefabOverride;
        }

        return condimentIngredient != null ? condimentIngredient.IngredientPrefab : null;
    }

    private void DestroyCondimentObject(GameObject condimentObject, NetworkObject condimentNetworkObject)
    {
        if (condimentNetworkObject != null && condimentNetworkObject.IsSpawned)
        {
            condimentNetworkObject.Despawn(destroy: true);
            return;
        }

        Destroy(condimentObject);
    }
}
