using UnityEngine;

public enum FoodCookState
{
    None,
    Raw,
    Cooked,
    Burnt
}

public enum FoodPrepState
{
    None,
    Whole,
    Chopped,
    Sliced
}

[DisallowMultipleComponent]
[RequireComponent(typeof(Item))]
public class FoodIngredient : MonoBehaviour
{
    [SerializeField] private FoodIngredientDefinition definition;
    [SerializeField] private bool useDefinitionDefaultStates = true;
    [SerializeField] private FoodCookState cookState = FoodCookState.None;
    [SerializeField] private FoodPrepState prepState = FoodPrepState.None;
    [Range(0f, 1f)]
    [SerializeField] private float cookProgress;

    public FoodIngredientDefinition Definition => definition;
    public bool HasDefinition => definition != null;
    public bool IsTrash => definition != null && definition.IsTrash;
    public int Cost => definition != null ? definition.Cost : 0;
    public bool CanBeCooked => definition != null && definition.CanBeCooked;
    public bool CanBePrepared => definition != null && definition.CanBePrepared;
    public FoodCookState CookState => cookState;
    public FoodPrepState PrepState => prepState;
    public float CookProgress => cookProgress;

    private void Awake()
    {
        if (useDefinitionDefaultStates)
        {
            ResetStateFromDefinition();
        }
    }

    public void ResetStateFromDefinition()
    {
        cookState = definition != null ? definition.DefaultCookState : FoodCookState.None;
        prepState = definition != null ? definition.DefaultPrepState : FoodPrepState.None;
        cookProgress = GetDefaultCookProgress(cookState);
    }

    public void SetCookState(FoodCookState newCookState)
    {
        cookState = newCookState;
    }

    public void SetPrepState(FoodPrepState newPrepState)
    {
        prepState = newPrepState;
    }

    public void SetCookProgress(float newCookProgress)
    {
        cookProgress = Mathf.Clamp01(newCookProgress);
    }

    private void Reset()
    {
        Item item = GetComponent<Item>();

        if (item != null)
        {
            item.itemType = Item.ItemType.Ingredient;
        }
    }

    private float GetDefaultCookProgress(FoodCookState state)
    {
        switch (state)
        {
            case FoodCookState.Cooked:
                return 0.5f;
            case FoodCookState.Burnt:
                return 1f;
            default:
                return 0f;
        }
    }
}
