using UnityEngine;

public enum IngredientQuality
{
    Good,
    Trash
}

public enum FoodStackBehavior
{
    SolidLayer,
    SurfaceOverlay,
    Unstackable
}

[CreateAssetMenu(fileName = "New Food Ingredient", menuName = "Long Dong Silvers/Food/Ingredient")]
public class FoodIngredientDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string ingredientName = "Ingredient";
    [SerializeField] private IngredientQuality quality = IngredientQuality.Good;

    [Header("Economy")]
    [Min(0)]
    [SerializeField] private int goodIngredientCost = 1;

    [Header("Preparation")]
    [SerializeField] private bool canBeCooked;
    [SerializeField] private bool canBePrepared;
    [SerializeField] private FoodCookState defaultCookState = FoodCookState.None;
    [SerializeField] private FoodPrepState defaultPrepState = FoodPrepState.None;

    [Header("Assembly")]
    [Tooltip("Solid layers increase the food height. Surface overlays, such as sauces, sit on the current surface without increasing it.")]
    [SerializeField] private FoodStackBehavior stackBehavior = FoodStackBehavior.SolidLayer;

    [Header("Prefab References")]
    [SerializeField] private GameObject ingredientPrefab;
    [SerializeField] private Sprite icon;

    [Header("Substitution")] //This is for the garbage ingredients
    [SerializeField] private FoodIngredientDefinition trashEquivalent;
    public FoodIngredientDefinition TrashEquivalent => trashEquivalent;

    public string IngredientName => ingredientName;
    public IngredientQuality Quality => quality;
    public bool IsTrash => quality == IngredientQuality.Trash;
    public int Cost => IsTrash ? 0 : goodIngredientCost;
    public bool CanBeCooked => canBeCooked;
    public bool CanBePrepared => canBePrepared;
    public FoodCookState DefaultCookState => defaultCookState;
    public FoodPrepState DefaultPrepState => defaultPrepState;
    public FoodStackBehavior StackBehavior => stackBehavior;
    public GameObject IngredientPrefab => ingredientPrefab;
    public Sprite Icon => icon;
}
