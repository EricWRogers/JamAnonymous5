using System.Collections.Generic;
using UnityEngine;

public enum FoodKind
{
    Hotdog,
    Burger
}

[CreateAssetMenu(fileName = "New Food Item", menuName = "Long Dong Silvers/Food/Food Item")]
public class FoodItemDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string foodName = "Food";
    [SerializeField] private FoodKind foodKind = FoodKind.Hotdog;

    [Header("Wrapping")]
    [Tooltip("Prefab spawned when this food is wrapped. Add WrappedFoodItem and NetworkObject to it for multiplayer use.")]
    [SerializeField] private GameObject wrappedFoodPrefab;

    [Header("Future Order Checks")]
    [Tooltip("Leave empty for now. Customer order matching can compare against these definitions later.")]
    [SerializeField] private List<FoodIngredientDefinition> allowedIngredients = new();

    public string FoodName => foodName;
    public FoodKind FoodKind => foodKind;
    public GameObject WrappedFoodPrefab => wrappedFoodPrefab;
    public IReadOnlyList<FoodIngredientDefinition> AllowedIngredients => allowedIngredients;

    public bool AllowsIngredient(FoodIngredientDefinition ingredient)
    {
        if (ingredient == null) return false;
        if (allowedIngredients == null || allowedIngredients.Count == 0) return true;

        return allowedIngredients.Contains(ingredient);
    }
}
