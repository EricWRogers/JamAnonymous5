using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public class WrappedFoodIngredientRecord
{
    public const ulong NoSourceNetworkObject = ulong.MaxValue;

    public FoodIngredientDefinition ingredient;
    public IngredientQuality quality;
    public FoodCookState cookState;
    public FoodPrepState prepState;
    public float cookProgress;
    public ulong sourceNetworkObjectId = NoSourceNetworkObject;

    public string IngredientName => ingredient != null ? ingredient.IngredientName : "Unknown Ingredient";
    public bool IsTrash => quality == IngredientQuality.Trash;

    public WrappedFoodIngredientRecord(FoodIngredient source)
    {
        ingredient = source != null ? source.Definition : null;
        quality = ingredient != null ? ingredient.Quality : IngredientQuality.Good;
        cookState = source != null ? source.CookState : FoodCookState.None;
        prepState = source != null ? source.PrepState : FoodPrepState.None;
        cookProgress = source != null ? source.CookProgress : 0f;

        Item item = source != null ? source.GetComponent<Item>() : null;
        sourceNetworkObjectId = item != null && item.NetworkObject != null
            ? item.NetworkObject.NetworkObjectId
            : NoSourceNetworkObject;
    }
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Item))]
public class WrappedFoodItem : NetworkBehaviour
{
    [SerializeField] private FoodItemDefinition foodDefinition;
    [SerializeField] private List<WrappedFoodIngredientRecord> ingredients = new();

    public FoodItemDefinition FoodDefinition => foodDefinition;
    public IReadOnlyList<WrappedFoodIngredientRecord> Ingredients => ingredients;
    public bool HasTrashIngredients => ingredients.Exists(ingredient => ingredient != null && ingredient.IsTrash);

    private void Reset()
    {
        Item item = GetComponent<Item>();

        if (item != null)
        {
            item.itemType = Item.ItemType.Food;
        }
    }

    public void ServerInitialize(
        FoodItemDefinition definition,
        IReadOnlyList<FoodIngredient> sourceIngredients,
        bool despawnSourceIngredients = true)
    {
        if (!IsServer) return;

        foodDefinition = definition;
        ingredients.Clear();

        if (sourceIngredients != null)
        {
            for (int i = 0; i < sourceIngredients.Count; i++)
            {
                FoodIngredient source = sourceIngredients[i];

                if (source == null || !source.HasDefinition)
                {
                    continue;
                }

                ingredients.Add(new WrappedFoodIngredientRecord(source));
            }
        }

        if (despawnSourceIngredients)
        {
            ServerDespawnSourceIngredients(sourceIngredients);
        }
    }

    public int CountIngredient(FoodIngredientDefinition ingredient)
    {
        if (ingredient == null) return 0;

        int count = 0;

        for (int i = 0; i < ingredients.Count; i++)
        {
            if (ingredients[i] != null && ingredients[i].ingredient == ingredient)
            {
                count++;
            }
        }

        return count;
    }

    private void ServerDespawnSourceIngredients(IReadOnlyList<FoodIngredient> sourceIngredients)
    {
        if (!IsServer || sourceIngredients == null) return;

        for (int i = 0; i < sourceIngredients.Count; i++)
        {
            FoodIngredient source = sourceIngredients[i];
            if (source == null) continue;

            NetworkObject networkObject = source.GetComponent<NetworkObject>();

            if (networkObject != null && networkObject.IsSpawned)
            {
                networkObject.Despawn(destroy: true);
            }
            else
            {
                Destroy(source.gameObject);
            }
        }
    }
}
