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

    [Header("Placement")]
    [SerializeField] private Vector2 randomLocalYRotationRange = new Vector2(0f, 360f);
    [SerializeField] private Vector2 hotdogRandomLocalXPositionRange = new Vector2(0f, -0.02f);
    [Min(0f)]
    [SerializeField] private float spacingMultiplier = 0.15f;

    [Header("Audio")]
    [SerializeField] private AudioClip squirtSound;
    [SerializeField, Range(0f, 1f)] private float squirtSoundVolume = 1f;

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

        float localYRotation = ShouldRandomizeLocalYRotation(assemblyBase)
            ? GetRandomLocalYRotation()
            : 0f;
        Vector3 localPositionOffset = GetLocalPositionOffset(assemblyBase);

        if (assemblyBase.ServerTrySnapIngredientWithLocalOffsets(condiment, localYRotation, spacingMultiplier, localPositionOffset))
        {
            PlaySquirtSoundClientRpc(assemblyBase.transform.position);
            return true;
        }

        DestroyCondimentObject(condimentObject, condimentNetworkObject);
        return false;
    }

    [ClientRpc]
    private void PlaySquirtSoundClientRpc(Vector3 position)
    {
        if (squirtSound == null) return;

        AudioSource.PlayClipAtPoint(squirtSound, position, squirtSoundVolume);
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

    private bool ShouldRandomizeLocalYRotation(FoodAssemblyBase assemblyBase)
    {
        return IsFoodKind(assemblyBase, FoodKind.Burger);
    }

    private Vector3 GetLocalPositionOffset(FoodAssemblyBase assemblyBase)
    {
        if (!IsFoodKind(assemblyBase, FoodKind.Hotdog))
        {
            return Vector3.zero;
        }

        return new Vector3(GetRandomHotdogLocalXPosition(), 0f, 0f);
    }

    private bool IsFoodKind(FoodAssemblyBase assemblyBase, FoodKind foodKind)
    {
        return assemblyBase != null &&
               assemblyBase.FoodDefinition != null &&
               assemblyBase.FoodDefinition.FoodKind == foodKind;
    }

    private float GetRandomLocalYRotation()
    {
        return GetRandomRangeValue(randomLocalYRotationRange);
    }

    private float GetRandomHotdogLocalXPosition()
    {
        return GetRandomRangeValue(hotdogRandomLocalXPositionRange);
    }

    private float GetRandomRangeValue(Vector2 range)
    {
        float min = range.x;
        float max = range.y;

        if (min > max)
        {
            (min, max) = (max, min);
        }

        if (Mathf.Approximately(min, max))
        {
            return min;
        }

        return Random.Range(min, max);
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
