using TMPro;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

public class CookProgressInspector : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private TMP_Text cookProgressText;
    [SerializeField] private GameObject inspectorRoot;

    [Header("Layout")]
    [SerializeField] private Vector2 inspectorPadding = new(24f, 16f);
    [SerializeField, Min(0f)] private float maximumWidth = 420f;
    [SerializeField, Min(0f)] private float maximumHeight = 0f;

    [Header("Detection")]
    [SerializeField, Min(0.1f)] private float inspectRange = 4f;
    [SerializeField] private LayerMask inspectLayers = ~0;

    private void Awake()
    {
        if (playerCamera == null && TryGetComponent(out PlayerPickup pickup))
        {
            playerCamera = pickup.playerCamera;
        }

        SetVisible(false);
    }

    private void Update()
    {
        if (!IsLocalPlayer())
        {
            SetVisible(false);
            return;
        }

        FoodIngredient ingredientInSight = FindIngredientInSight();
        if (TryShowGrillIngredientInfo(ingredientInSight))
        {
            return;
        }

        if (Keyboard.current == null || !Keyboard.current.tabKey.isPressed)
        {
            SetVisible(false);
            return;
        }

        ServingTray tray = FindTrayInSight();
        bool shouldShow = tray != null && TryShowTrayInfo(tray);

        if (!shouldShow)
        {
            FoodAssemblyBase assembly = FindAssemblyInSight();
            shouldShow = assembly != null && TryShowAssemblyInfo(assembly);
        }

        if (!shouldShow)
        {
            shouldShow = TryShowIngredientInfo(ingredientInSight);
        }

        if (!shouldShow)
        {
            SetVisible(false);
            return;
        }
    }

    private bool TryShowGrillIngredientInfo(FoodIngredient ingredient)
    {
        if (ingredient == null || !ingredient.CanBeCooked || !ingredient.IsOnGrill)
        {
            return false;
        }

        SetVisible(true);
        cookProgressText.text = $"{ingredient.Definition.IngredientName}: " +
            $"{Mathf.RoundToInt(ingredient.CookProgress * 100f)}%";
        RefreshLayout();
        return true;
    }

    private FoodIngredient FindIngredientInSight()
    {
        if (playerCamera == null) return null;

        Ray ray = playerCamera.ScreenPointToRay(
            new Vector3(Screen.width / 2f, Screen.height / 2f, 0f)
        );

        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                inspectRange,
                inspectLayers,
                QueryTriggerInteraction.Ignore))
        {
            return null;
        }

        return hit.collider.GetComponentInParent<FoodIngredient>();
    }

    private ServingTray FindTrayInSight()
    {
        if (playerCamera == null) return null;

        Ray ray = playerCamera.ScreenPointToRay(
            new Vector3(Screen.width / 2f, Screen.height / 2f, 0f)
        );

        if (!Physics.Raycast(ray, out RaycastHit hit, inspectRange, inspectLayers, QueryTriggerInteraction.Ignore))
        {
            return null;
        }

        return hit.collider.GetComponentInParent<ServingTray>();
    }

    private FoodAssemblyBase FindAssemblyInSight()
    {
        if (playerCamera == null) return null;

        Ray ray = playerCamera.ScreenPointToRay(
            new Vector3(Screen.width / 2f, Screen.height / 2f, 0f)
        );

        if (!Physics.Raycast(ray, out RaycastHit hit, inspectRange, inspectLayers, QueryTriggerInteraction.Ignore))
        {
            return null;
        }

        return hit.collider.GetComponentInParent<FoodAssemblyBase>();
    }

    private bool TryShowIngredientInfo(FoodIngredient ingredient)
    {
        if (!CanInspect(ingredient)) return false;

        SetVisible(true);
        cookProgressText.text = $"Cooked: {Mathf.RoundToInt(ingredient.CookProgress * 100f)}%";
        RefreshLayout();
        return true;
    }

    private bool TryShowTrayInfo(ServingTray tray)
    {
        Item trayItem = tray.GetComponent<Item>();
        if (trayItem == null || trayItem.IsHeld) return false;

        return TryShowStackInfo(tray.GetCarriedIngredients());
    }

    private bool TryShowAssemblyInfo(FoodAssemblyBase assembly)
    {
        Item assemblyItem = assembly.GetComponent<Item>();
        if (assemblyItem == null || assemblyItem.IsHeld || assembly.IsOnServingTray) return false;

        return TryShowStackInfo(assembly.GetAssembledIngredients());
    }

    private bool TryShowStackInfo(IReadOnlyList<FoodIngredient> ingredients)
    {
        StringBuilder info = new();

        for (int i = ingredients.Count - 1; i >= 0; i--)
        {
            FoodIngredient ingredient = ingredients[i];
            if (ingredient == null || ingredient.Definition == null)
            {
                continue;
            }

            if (info.Length > 0) info.AppendLine();
            info.Append(ingredient.Definition.IngredientName);
            if (ingredient.CanBeCooked)
            {
                info.Append(": ");
                info.Append(Mathf.RoundToInt(ingredient.CookProgress * 100f));
                info.Append('%');
            }
        }

        if (info.Length == 0) return false;

        SetVisible(true);
        cookProgressText.text = info.ToString();
        RefreshLayout();
        return true;
    }

    private void RefreshLayout()
    {
        if (cookProgressText == null) return;

        RectTransform textRect = cookProgressText.rectTransform;
        RectTransform rootRect = inspectorRoot != null
            ? inspectorRoot.GetComponent<RectTransform>()
            : textRect.parent as RectTransform;

        if (rootRect == null) return;

        cookProgressText.textWrappingMode = maximumWidth > 0f
            ? TextWrappingModes.Normal
            : TextWrappingModes.NoWrap;
        cookProgressText.ForceMeshUpdate();

        float textWidth = cookProgressText.preferredWidth;
        if (maximumWidth > 0f)
        {
            textWidth = Mathf.Min(textWidth, maximumWidth - inspectorPadding.x);
        }

        textWidth = Mathf.Max(1f, textWidth);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(inspectorPadding.x * 0.5f, inspectorPadding.y * 0.5f);
        textRect.offsetMax = new Vector2(-inspectorPadding.x * 0.5f, -inspectorPadding.y * 0.5f);

        float textHeight = cookProgressText.GetPreferredValues(textWidth, 0f).y;
        float rootWidth = textWidth + inspectorPadding.x;
        float rootHeight = textHeight + inspectorPadding.y;

        if (maximumHeight > 0f)
        {
            rootHeight = Mathf.Min(rootHeight, maximumHeight);
            cookProgressText.overflowMode = TextOverflowModes.Truncate;
        }

        rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rootWidth);
        rootRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rootHeight);
        LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
    }

    private bool CanInspect(FoodIngredient ingredient)
    {
        if (ingredient == null) return false;

        Item item = ingredient.GetComponent<Item>();

        return ingredient.CanBeCooked &&
            item != null &&
            !item.IsHeld &&
            !ingredient.IsInFoodAssembly &&
            !ingredient.IsOnGrill;
    }

    private bool IsLocalPlayer()
    {
        PlayerInteraction interaction = GetComponent<PlayerInteraction>();
        return interaction == null || interaction.IsOwner;
    }

    private void SetVisible(bool visible)
    {
        if (inspectorRoot != null)
        {
            inspectorRoot.SetActive(visible);
        }
        else if (cookProgressText != null)
        {
            cookProgressText.gameObject.SetActive(visible);
        }
    }
}
