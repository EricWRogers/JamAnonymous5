using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public class Grill : NetworkBehaviour
{
    [Header("Cooking")]
    [Tooltip("Normalized progress per second. 0.05 reaches cooked in 10 seconds and burnt in 20 seconds.")]
    [SerializeField, Min(0f)] private float cookSpeed = 0.05f;
    [Header("Cooking Surface")]
    [Tooltip("Trigger above the cooking surface. Ingredients must also rest directly on a solid grill collider.")]
    [SerializeField] private BoxCollider cookingArea;
    [SerializeField, Min(0.005f)] private float surfaceTolerance = 0.05f;

    private readonly NetworkList<ulong> cookingItemIds = new(null,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly HashSet<FoodIngredient> cookingIngredients = new();
    private readonly HashSet<FoodIngredient> detected = new();
    private readonly List<FoodIngredient> removed = new();
    private Collider[] overlaps = new Collider[32];
    private float elapsed;
    private bool pendingObjects;
    public bool HasCookingItem => cookingItemIds.Count > 0;

    private void Awake()
    {
        if (cookingArea != null) return;
        foreach (BoxCollider box in GetComponentsInChildren<BoxCollider>())
            if (box.isTrigger) { cookingArea = box; break; }
    }

    public override void OnNetworkSpawn()
    {
        cookingItemIds.OnListChanged += OnCookingChanged;
        if (cookingArea == null) Debug.LogError("Grill requires a cooking-area BoxCollider trigger.", this);
        ReconcileIngredients();
    }

    public override void OnNetworkDespawn()
    {
        cookingItemIds.OnListChanged -= OnCookingChanged;
        foreach (FoodIngredient ingredient in cookingIngredients)
            if (ingredient != null && ingredient.CurrentGrill == this) ingredient.SetCurrentGrill(null);
        cookingIngredients.Clear();
        detected.Clear();
        pendingObjects = false;
        elapsed = 0f;
        base.OnNetworkDespawn();
    }

    private void Update()
    {
        if (!IsSpawned) return;
        if (pendingObjects) ReconcileIngredients();
        if (!IsServer) return;
        elapsed += Time.deltaTime;
        if (elapsed < 0.1f) return;
        float delta = elapsed;
        elapsed = 0f;
        DetectIngredients();
        // Remove old membership first, including picked-up or despawned food.
        for (int i = cookingItemIds.Count - 1; i >= 0; i--)
            if (!TryResolve(cookingItemIds[i], out FoodIngredient ingredient) || !detected.Contains(ingredient))
                cookingItemIds.RemoveAt(i);
        foreach (FoodIngredient ingredient in detected)
        {
            ulong id = ingredient.NetworkObjectId;
            // Don't advance a new arrival for time spent off the grill before this scan.
            if (!cookingItemIds.Contains(id)) cookingItemIds.Add(id);
            else ingredient.ServerAdvanceCooking(Mathf.Max(0f, cookSpeed) * delta);
        }
    }

    private void DetectIngredients()
    {
        detected.Clear();
        if (cookingArea == null || !cookingArea.enabled || !cookingArea.gameObject.activeInHierarchy) return;
        Vector3 scale = cookingArea.transform.lossyScale;
        Vector3 half = Vector3.Scale(cookingArea.size * 0.5f,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        int count;
        do
        {
            count = Physics.OverlapBoxNonAlloc(cookingArea.transform.TransformPoint(cookingArea.center), half,
                overlaps, cookingArea.transform.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (count < overlaps.Length) break;
            System.Array.Resize(ref overlaps, overlaps.Length * 2);
        } while (true);
        for (int i = 0; i < count; i++)
        {
            Collider collider = overlaps[i];
            FoodIngredient ingredient = collider.GetComponentInParent<FoodIngredient>();
            if (ingredient == null || detected.Contains(ingredient) || !ingredient.IsSpawned ||
                !ingredient.CanBeCooked || ingredient.IsInFoodAssembly ||
                (ingredient.CurrentGrill != null && ingredient.CurrentGrill != this)) continue;
            Item item = ingredient.GetComponent<Item>();
            if (item == null || item.IsHeld || !IsSupportedByGrill(ingredient, collider)) continue;
            detected.Add(ingredient);
        }
    }

    private bool IsSupportedByGrill(FoodIngredient ingredient, Collider collider)
    {
        Bounds bounds = collider.bounds;
        RaycastHit nearest = default;
        float distance = float.PositiveInfinity;
        // Use world up: imported grill meshes can have a rotated local coordinate system.
        foreach (RaycastHit hit in Physics.RaycastAll(bounds.center, Vector3.down,
            bounds.extents.y + surfaceTolerance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform.IsChildOf(ingredient.transform)) continue;
            if (hit.distance < distance) { nearest = hit; distance = hit.distance; }
        }
        return nearest.collider != null && nearest.normal.y >= 0.8f &&
            nearest.collider.GetComponentInParent<Grill>() == this;
    }

    public bool ServerTryRemoveItem(FoodIngredient ingredient)
    {
        if (!IsServer || ingredient == null || ingredient.CurrentGrill != this) return false;
        cookingItemIds.Remove(ingredient.NetworkObjectId);
        if (ingredient.CurrentGrill == this) ingredient.SetCurrentGrill(null);
        return true;
    }

    private void OnCookingChanged(NetworkListEvent<ulong> change) => ReconcileIngredients();

    private void ReconcileIngredients()
    {
        removed.Clear();
        foreach (FoodIngredient ingredient in cookingIngredients)
            if (ingredient == null || !ingredient.IsSpawned || !cookingItemIds.Contains(ingredient.NetworkObjectId))
                removed.Add(ingredient);
        foreach (FoodIngredient ingredient in removed)
        {
            if (ingredient != null && ingredient.CurrentGrill == this) ingredient.SetCurrentGrill(null);
            cookingIngredients.Remove(ingredient);
        }
        pendingObjects = false;
        foreach (ulong id in cookingItemIds)
        {
            if (!TryResolve(id, out FoodIngredient ingredient)) { pendingObjects = true; continue; }
            cookingIngredients.Add(ingredient);
            ingredient.SetCurrentGrill(this);
        }
    }

    private bool TryResolve(ulong id, out FoodIngredient ingredient)
    {
        ingredient = null;
        return NetworkManager != null && NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(id, out NetworkObject obj)
            && obj.TryGetComponent(out ingredient);
    }
}
