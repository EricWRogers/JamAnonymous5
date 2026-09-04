using System.Collections.Generic;
using Unity.Netcode;
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
public class FoodIngredient : NetworkBehaviour
{
    [SerializeField] private FoodIngredientDefinition definition;
    [SerializeField] private bool useDefinitionDefaultStates = true;
    [SerializeField] private FoodCookState cookState = FoodCookState.None;
    [SerializeField] private FoodPrepState prepState = FoodPrepState.None;
    [Range(0f, 1f)]
    [SerializeField] private float cookProgress;

    [Header("Cook Visuals")]
    [SerializeField] private Color cookedColor = new Color(0.4f, 0.2f, 0.05f);
    [SerializeField] private Color burntColor = new Color(0.1f, 0.05f, 0f);

    private readonly NetworkVariable<FoodCookState> networkCookState = new(
        FoodCookState.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<FoodPrepState> networkPrepState = new(
        FoodPrepState.None,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<float> networkCookProgress = new(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly Dictionary<Renderer, Color> originalRendererColors = new();
    private Grill currentGrill;
    private FoodAssemblyBase currentAssembly;

    public FoodIngredientDefinition Definition => definition;
    public bool HasDefinition => definition != null;
    public bool IsTrash => definition != null && definition.IsTrash;
    public int Cost => definition != null ? definition.Cost : 0;
    public bool CanBeCooked => definition != null && definition.CanBeCooked;
    public bool CanBePrepared => definition != null && definition.CanBePrepared;
    public FoodCookState CookState => IsSpawned ? networkCookState.Value : cookState;
    public FoodPrepState PrepState => IsSpawned ? networkPrepState.Value : prepState;
    public float CookProgress => IsSpawned ? networkCookProgress.Value : cookProgress;
    public Grill CurrentGrill => currentGrill;
    public bool IsOnGrill => currentGrill != null;
    public FoodAssemblyBase CurrentAssembly => currentAssembly;
    public bool IsInFoodAssembly => currentAssembly != null;

    /// <summary>
    /// Returns this ingredient's visible bounds projected onto an assembly's local
    /// stack direction. Bounds are measured from the active renderers, so differently
    /// sized ingredients and preparation-state visuals require no spacing values.
    /// </summary>
    public bool TryGetStackProjection(
        Transform assemblyRoot,
        Vector3 localStackDirection,
        out float minimum,
        out float maximum)
    {
        minimum = float.PositiveInfinity;
        maximum = float.NegativeInfinity;

        if (assemblyRoot == null)
        {
            return false;
        }

        Vector3 direction = localStackDirection.sqrMagnitude > Mathf.Epsilon
            ? localStackDirection.normalized
            : Vector3.up;
        Renderer[] renderers = GetComponentsInChildren<Renderer>(includeInactive: false);
        bool foundBounds = false;

        for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
        {
            Renderer currentRenderer = renderers[rendererIndex];

            if (currentRenderer == null || !currentRenderer.enabled)
            {
                continue;
            }

            // An assembly base contains the renderers of all snapped ingredients.
            // Only measure renderers owned by this FoodIngredient.
            FoodIngredient rendererOwner = currentRenderer.GetComponentInParent<FoodIngredient>();

            if (rendererOwner != this)
            {
                continue;
            }

            Bounds localBounds = currentRenderer.localBounds;
            Vector3 center = localBounds.center;
            Vector3 extents = localBounds.extents;

            for (int cornerIndex = 0; cornerIndex < 8; cornerIndex++)
            {
                Vector3 corner = center + new Vector3(
                    (cornerIndex & 1) == 0 ? -extents.x : extents.x,
                    (cornerIndex & 2) == 0 ? -extents.y : extents.y,
                    (cornerIndex & 4) == 0 ? -extents.z : extents.z
                );
                Vector3 worldCorner = currentRenderer.transform.TransformPoint(corner);
                Vector3 assemblyLocalCorner = assemblyRoot.InverseTransformPoint(worldCorner);
                float projection = Vector3.Dot(assemblyLocalCorner, direction);

                minimum = Mathf.Min(minimum, projection);
                maximum = Mathf.Max(maximum, projection);
                foundBounds = true;
            }
        }

        return foundBounds;
    }

    private void Awake()
    {
        if (useDefinitionDefaultStates)
        {
            ResetStateFromDefinition();
        }
    }

    public override void OnNetworkSpawn()
    {
        networkCookState.OnValueChanged += OnCookStateChanged;
        networkPrepState.OnValueChanged += OnPrepStateChanged;
        networkCookProgress.OnValueChanged += OnCookProgressChanged;

        if (IsServer)
        {
            networkCookState.Value = cookState;
            networkPrepState.Value = prepState;
            networkCookProgress.Value = cookProgress;
        }

        SyncLocalStateFromNetwork();
        ApplyCookVisuals();
    }

    public override void OnNetworkDespawn()
    {
        networkCookState.OnValueChanged -= OnCookStateChanged;
        networkPrepState.OnValueChanged -= OnPrepStateChanged;
        networkCookProgress.OnValueChanged -= OnCookProgressChanged;
        currentGrill = null;
        currentAssembly = null;
        base.OnNetworkDespawn();
    }

    public void ResetStateFromDefinition()
    {
        cookState = definition != null ? definition.DefaultCookState : FoodCookState.None;
        prepState = definition != null ? definition.DefaultPrepState : FoodPrepState.None;
        cookProgress = GetDefaultCookProgress(cookState);
    }

    public void SetDefinition(FoodIngredientDefinition newDefinition, bool resetState = true)
    {
        definition = newDefinition;

        if (resetState)
        {
            ResetStateFromDefinition();
        }
    }

    public void SetCookState(FoodCookState newCookState)
    {
        cookState = newCookState;

        if (IsSpawned && IsServer)
        {
            networkCookState.Value = newCookState;
        }

        ApplyCookVisuals();
    }

    public void SetPrepState(FoodPrepState newPrepState)
    {
        prepState = newPrepState;

        if (IsSpawned && IsServer)
        {
            networkPrepState.Value = newPrepState;
        }
    }

    public void SetCookProgress(float newCookProgress)
    {
        cookProgress = Mathf.Clamp01(newCookProgress);

        if (IsSpawned && IsServer)
        {
            networkCookProgress.Value = cookProgress;
        }

        ApplyCookVisuals();
    }

    public bool ServerAdvanceCooking(float normalizedDelta)
    {
        if (!IsServer || !CanBeCooked || normalizedDelta <= 0f) return false;

        float nextProgress = Mathf.Clamp01(networkCookProgress.Value + normalizedDelta);
        FoodCookState nextState = nextProgress >= 1f
            ? FoodCookState.Burnt
            : nextProgress >= 0.5f
                ? FoodCookState.Cooked
                : FoodCookState.Raw;

        cookProgress = nextProgress;
        cookState = nextState;
        networkCookProgress.Value = nextProgress;
        networkCookState.Value = nextState;
        ApplyCookVisuals();
        return true;
    }

    public void SetCurrentGrill(Grill grill)
    {
        currentGrill = grill;
    }

    public void SetCurrentAssembly(FoodAssemblyBase assembly)
    {
        currentAssembly = assembly;
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

    private void OnCookStateChanged(FoodCookState previousValue, FoodCookState newValue)
    {
        cookState = newValue;
        ApplyCookVisuals();
    }

    private void OnPrepStateChanged(FoodPrepState previousValue, FoodPrepState newValue)
    {
        prepState = newValue;
    }

    private void OnCookProgressChanged(float previousValue, float newValue)
    {
        cookProgress = newValue;
        ApplyCookVisuals();
    }

    private void SyncLocalStateFromNetwork()
    {
        cookState = networkCookState.Value;
        prepState = networkPrepState.Value;
        cookProgress = networkCookProgress.Value;
    }

    private void ApplyCookVisuals()
    {
        if (!CanBeCooked) return;

        Renderer[] renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        float progress = CookProgress;

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer currentRenderer = renderers[i];
            if (currentRenderer == null) continue;

            if (!originalRendererColors.TryGetValue(currentRenderer, out Color originalColor))
            {
                originalColor = currentRenderer.material.color;
                originalRendererColors.Add(currentRenderer, originalColor);
            }

            currentRenderer.material.color = progress < 0.5f
                ? Color.Lerp(originalColor, cookedColor, progress * 2f)
                : Color.Lerp(cookedColor, burntColor, (progress - 0.5f) * 2f);
        }
    }
}
