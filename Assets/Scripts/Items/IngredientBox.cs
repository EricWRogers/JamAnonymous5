using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>A purchased batch of ingredients. Only the host creates items and changes stock.</summary>
[RequireComponent(typeof(Item))]
[RequireComponent(typeof(Unity.Netcode.Components.NetworkTransform))]
public sealed class IngredientBox : NetworkBehaviour, IInteractable
{
    [Header("Box Contents")]
    [SerializeField, Tooltip("Networked food prefab dispensed by this box.")] private GameObject ingredientPrefab;
    [SerializeField, InspectorName("Ingredients Per Box"), Min(1), Tooltip("Number of ingredients in a newly purchased box.")] private int configuredCapacity = 20;
    [SerializeField, InspectorName("Box Price"), Min(0), Tooltip("Price for the whole box.")] private int configuredPrice;
    [Header("Carrying")]
    [SerializeField, Tooltip("Offset from the player's hold point: negative Y lowers the box; positive Z moves it forward.")]
    private Vector3 carryOffset = new Vector3(0f, -0.3f, 0.4f);
    [SerializeField, Min(0f), Tooltip("Additional vertical travel when looking up or down. Looking down lowers the box to clear the placement preview.")]
    private float lookHeightTravel = 0.65f;

    public Vector3 GetCarryOffset(PlayerPickup holder)
    {
        Vector3 offset = carryOffset;
        if (holder.playerCamera != null && holder.holdPoint != null)
        {
            float lookHeight = Vector3.Dot(holder.playerCamera.transform.forward, holder.transform.up);
            // Convert a player-up displacement into hold-point space, including
            // when the hold point itself follows the camera's pitch.
            offset += holder.holdPoint.InverseTransformVector(
                holder.transform.up * (lookHeight * lookHeightTravel));
        }
        return offset;
    }

    [Header("Box Label")]
    [SerializeField, Min(0f), Tooltip("World-space gap between the top of the box and its label.")]
    private float labelClearance = 0.08f;
    public bool HasValidContents => ingredientPrefab != null &&
        ingredientPrefab.GetComponent<FoodIngredient>() != null &&
        ingredientPrefab.GetComponent<Item>() != null &&
        ingredientPrefab.GetComponent<NetworkObject>() != null;
    private readonly NetworkVariable<int> remaining = new();
    private readonly NetworkVariable<int> capacity = new(20);
    private readonly NetworkVariable<int> price = new();
    private readonly NetworkVariable<bool> purchased = new();
    private readonly NetworkVariable<FixedString128Bytes> ingredientName = new();
    private TextMeshPro label;
    private string displayedText;
    private Renderer[] boxRenderers;
    private Collider[] boxColliders;
    public int Remaining => remaining.Value;
    public bool IsPurchased => purchased.Value;

    public void Configure(GameObject prefab, int count, int cost)
    {
        ingredientPrefab = prefab;
        configuredCapacity = Mathf.Clamp(count, 1, 999);
        configuredPrice = Mathf.Max(0, cost);
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            capacity.Value = Mathf.Clamp(configuredCapacity, 1, 999);
            price.Value = Mathf.Max(0, configuredPrice);
            if (!HasValidContents) Debug.LogError("Assign a networked FoodIngredient prefab to this ingredient box.", this);
            string name = HasValidContents ? ingredientPrefab.GetComponent<Item>().itemName : "Unconfigured box";
            if (string.IsNullOrEmpty(name)) name = ingredientPrefab.name;
            ingredientName.Value = new FixedString128Bytes(name.Length > 30 ? name.Substring(0, 30) : name);
        }
        var display = new GameObject("Box label");
        // Keep label meshes out of Item placement bounds and holograms.
        boxRenderers = GetComponentsInChildren<Renderer>();
        boxColliders = GetComponentsInChildren<Collider>();
        label = display.AddComponent<TextMeshPro>();
        label.fontSize = 1.1f;
        label.alignment = TextAlignmentOptions.Center;
        label.rectTransform.sizeDelta = new Vector2(1.2f, 0.45f);
        label.color = Color.white;
        purchased.OnValueChanged += OnPurchasedChanged;
        RefreshPurchasePhysics();
        UpdateLabelPose();
    }

    private void OnPurchasedChanged(bool previous, bool current) => RefreshPurchasePhysics();
    private void RefreshPurchasePhysics()
    {
        GetComponent<Rigidbody>().constraints = purchased.Value ? RigidbodyConstraints.None : RigidbodyConstraints.FreezeAll;
    }

    private void LateUpdate()
    {
        if (!IsSpawned || label == null) return;
        string text = !purchased.Value
            ? $"{ingredientName.Value}\n[F] Buy {capacity.Value} for ${price.Value}"
            : $"{ingredientName.Value} {remaining.Value}/{capacity.Value}\n[F] Take / Return | [Shift+F] Carry";
        if (text != displayedText) { label.text = text; displayedText = text; }
        UpdateLabelPose();
    }

    private void UpdateLabelPose()
    {
        Camera camera = Camera.main;
        if (camera != null) label.transform.rotation = camera.transform.rotation;

        Bounds bounds = new Bounds(transform.position, Vector3.zero);
        bool hasBounds = false;
        foreach (Renderer renderer in boxRenderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        foreach (Collider collider in boxColliders)
        {
            if (collider == null || !collider.enabled || collider.isTrigger || !collider.gameObject.activeInHierarchy) continue;
            if (!hasBounds) { bounds = collider.bounds; hasBounds = true; }
            else bounds.Encapsulate(collider.bounds);
        }

        // Include the label's vertical extent after billboarding, so its lower
        // edge stays clear even when the camera pitches or the box rotates.
        Vector2 size = label.rectTransform.rect.size;
        float halfHeight = (Mathf.Abs(label.transform.right.y) * size.x +
                            Mathf.Abs(label.transform.up.y) * size.y) * 0.5f;
        label.transform.position = new Vector3(bounds.center.x,
            bounds.max.y + Mathf.Max(0f, labelClearance) + halfHeight, bounds.center.z);
    }

    public override void OnNetworkDespawn()
    {
        purchased.OnValueChanged -= OnPurchasedChanged;
        if (label != null) Destroy(label.gameObject);
        label = null;
        displayedText = null;
    }

    public void Interact(PlayerInteraction player)
    {
        if (IsSpawned && player.IsOwner && player.TryGetComponent(out PlayerPickup pickup))
            RequestUseRpc(pickup.IsCarryModifierPressed);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    private void RequestUseRpc(bool carry, RpcParams rpc = default)
    {
        if (!enabled || !HasValidContents || GetComponent<Item>().IsHeld ||
            !NetworkManager.ConnectedClients.TryGetValue(rpc.Receive.SenderClientId, out NetworkClient client) ||
            client.PlayerObject == null) return;
        PlayerPickup pickup = client.PlayerObject.GetComponentInChildren<PlayerPickup>();
        if (pickup == null || !pickup.enabled ||
            Vector3.Distance(pickup.transform.position, transform.position) > 3.5f) return;
        if (pickup.TryGetComponent(out PlayerVehicleDriver driver) && driver.IsSeated) return;
        Vector3 eye = pickup.playerCamera != null ? pickup.playerCamera.transform.position : pickup.transform.position + Vector3.up;
        Vector3 target = transform.position + Vector3.up * 0.12f;
        foreach (RaycastHit hit in Physics.RaycastAll(eye, target - eye, Vector3.Distance(eye, target),
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform) && !hit.transform.IsChildOf(pickup.transform)) return;

        if (pickup.IsHoldingItem())
        {
            if (!carry) TryReturnIngredient(pickup);
            return;
        }
        if (carry)
        {
            if (purchased.Value) pickup.ServerTryPickUpItem(GetComponent<Item>());
            return;
        }
        if (!purchased.Value)
        {
            if (pickup.holdPoint == null) return;
            RestaurantMoney money = RestaurantMoney.Instance;
            if (price.Value > 0 && (money == null || !money.ServerTrySpend(price.Value))) return;
            remaining.Value = capacity.Value;
            purchased.Value = true;
            // Complete purchase and pickup in the same server request, before
            // physics can drop the newly unlocked box off its shelf.
            if (!pickup.ServerTryPickUpItem(GetComponent<Item>()))
            {
                purchased.Value = false;
                remaining.Value = 0;
                if (price.Value > 0) money.ServerAddMoney(price.Value);
            }
            return;
        }
        if (remaining.Value <= 0) return;
        // Requests execute sequentially on the host. Failed pickup consumes no stock.
        GameObject instance = Instantiate(ingredientPrefab,
            pickup.holdPoint != null ? pickup.holdPoint.position : target, Quaternion.identity);
        NetworkObject obj = instance.GetComponent<NetworkObject>();
        obj.Spawn(destroyWithScene: true);
        if (!pickup.ServerTryPickUpItem(instance.GetComponent<Item>())) { obj.Despawn(true); return; }
        remaining.Value--;
    }

    private void TryReturnIngredient(PlayerPickup pickup)
    {
        if (!IsServer || !purchased.Value || remaining.Value >= capacity.Value ||
            !pickup.ServerTryGetHeldItem(out Item item) || !item.IsSpawned || !item.IsHeld ||
            !item.TryGetComponent(out FoodIngredient food) || food.IsInFoodAssembly || food.IsOnGrill ||
            !food.MatchesBoxStock(ingredientPrefab.GetComponent<FoodIngredient>())) return;
        if (item.TryGetComponent(out FoodAssemblyBase assembly) && assembly.SnappedIngredients.Count > 0) return;
        // Never discard nested ingredients or other networked contents.
        if (item.GetComponentsInChildren<NetworkObject>(true).Length != 1) return;
        if (!pickup.ServerTryReleaseHeldItemPreserveWorldPose(item)) return;
        item.NetworkObject.Despawn(true);
        remaining.Value++;
    }
}
