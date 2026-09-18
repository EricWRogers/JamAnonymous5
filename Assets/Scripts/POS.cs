using System.Collections.Generic;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class POS : NetworkBehaviour
{
    public GameObject startShift;
    public GameObject ingredientSelect;

    public TextMeshProUGUI text;

    public string orderText;

    private readonly List<string> serverIngredientNames = new();
    private readonly List<float> serverCookPercentages = new();
    private readonly NetworkVariable<FixedString4096Bytes> networkOrderText = new(
        new FixedString4096Bytes(),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        networkOrderText.OnValueChanged += OnOrderTextChanged;
        if (IsServer)
        {
            networkOrderText.Value = new FixedString4096Bytes();
        }

        ApplyOrderText(networkOrderText.Value.ToString());
        if (IsServer) UpdatePanelClientRpc(true, false);
    }

    public override void OnNetworkDespawn()
    {
        networkOrderText.OnValueChanged -= OnOrderTextChanged;
        base.OnNetworkDespawn();
    }

    public void StartShift()
    {
        var register = GetComponent<RegisterTest>();
        if (register != null && register.RequiresOpenShop)
        {
            if (register.Shop != null) register.Shop.RequestOpen(true);
            return;
        }
        StartShiftServerRpc();
    }

    public void SubmitOrder()
    {
        SubmitOrderServerRpc();
    }

    public void AddIngredient(FoodIngredientButtonDefinition ingredient)
    {
        if (ingredient == null || ingredient.ingredient == null) return;
        AddIngredientServerRpc(ingredient.ingredient.IngredientName);
    }

    public void RemoveLastIngredient()
    {
        RemoveLastIngredientServerRpc();
    }

    public void SetLastCookPercentage(CookPreferenceButtonDefinition button)
    {
        if (button == null) return;
        int percentage = Mathf.Clamp(button.percentage, 0, 100);
        SetLastCookPercentageServerRpc(percentage);
    }

    [ServerRpc(RequireOwnership = false)]
    private void AddIngredientServerRpc(string ingredientName)
    {
        if (!CanTakeOrders || string.IsNullOrWhiteSpace(ingredientName)) return;

        serverIngredientNames.Add(ingredientName);
        serverCookPercentages.Add(-1f);
        RefreshServerOrderText();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RemoveLastIngredientServerRpc()
    {
        if (!CanTakeOrders || serverIngredientNames.Count == 0) return;

        serverIngredientNames.RemoveAt(serverIngredientNames.Count - 1);
        serverCookPercentages.RemoveAt(serverCookPercentages.Count - 1);
        RefreshServerOrderText();
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetLastCookPercentageServerRpc(int percentage)
    {
        if (!CanTakeOrders || serverCookPercentages.Count == 0) return;
        serverCookPercentages[serverCookPercentages.Count - 1] = Mathf.Clamp(percentage, 0, 100);
        RefreshServerOrderText();
    }

    private void RefreshServerOrderText()
    {
        var lines = new List<string>();

        for (int i = 0; i < serverIngredientNames.Count; i++)
        {
            float percentage = i < serverCookPercentages.Count
                ? serverCookPercentages[i]
                : -1f;

            lines.Add(percentage >= 0f
                ? $"{serverIngredientNames[i]} ({percentage:0}%)"
                : serverIngredientNames[i]);
        }

        orderText = string.Join("\n", lines);
        networkOrderText.Value = new FixedString4096Bytes(orderText);
    }

    private void OnOrderTextChanged(FixedString4096Bytes previousValue, FixedString4096Bytes newValue)
    {
        ApplyOrderText(newValue.ToString());
    }

    private void ApplyOrderText(string newText)
    {
        orderText = newText;
        if (text != null) text.text = newText;
    }

    [ServerRpc(RequireOwnership = false)]
    void StartShiftServerRpc()
    {
        if (TryGetComponent<RegisterTest>(out var register) && register.RequiresOpenShop) return;
        GameManager.Instance.StartShiftServerRpc();
        UpdatePanelClientRpc(false, true);
    }

    [ServerRpc(RequireOwnership = false)]
    void SubmitOrderServerRpc()
    {
        if (!CanTakeOrders || serverIngredientNames.Count == 0) return;

        var names = new List<string>();
        for (int i = 0; i < serverIngredientNames.Count; i++)
        {
            float percentage = serverCookPercentages[i];
            names.Add(percentage >= 0f
                ? $"{serverIngredientNames[i]} ({percentage:0}%)"
                : serverIngredientNames[i]);
        }

        GetComponent<RegisterTest>().NotifyOrderSubmittedServerRpc(string.Join(",", names));
        serverIngredientNames.Clear();
        serverCookPercentages.Clear();
        RefreshServerOrderText();
        UpdatePanelClientRpc(false, true);
    }

    [ClientRpc]
    void UpdatePanelClientRpc(bool showStart, bool showIngredient)
    {
        startShift.SetActive(showStart);
        ingredientSelect.SetActive(showIngredient);
    }

    public void ResetToStartShift()
    {
        if (!IsServer) return;
        serverIngredientNames.Clear();
        serverCookPercentages.Clear();
        RefreshServerOrderText();
        if (IsServer) UpdatePanelClientRpc(true, false);
    }

    public void EndShift()
    {
        var register = GetComponent<RegisterTest>();
        if (register != null && register.RequiresOpenShop)
        {
            if (register.Shop != null) register.Shop.RequestOpen(false);
            return;
        }
        if (!NetworkManager.Singleton.IsHost) return;
        GameManager.Instance.EndShiftServerRpc();
    }

    private bool CanTakeOrders => GetComponent<RegisterTest>() == null || GetComponent<RegisterTest>().CanAcceptCustomers;

    private void Update()
    {
        var register = GetComponent<RegisterTest>();
        if (register == null || !register.RequiresOpenShop) return;
        bool open = register.Shop != null && register.Shop.IsOpen;
        if (startShift != null)
        {
            startShift.SetActive(!open);
            var label = startShift.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.text = register.Shop == null || register.Shop.State == FoodTruckShop.ShopState.Closed
                ? "Open Truck" : "Shutters moving...";
        }
        if (ingredientSelect != null) ingredientSelect.SetActive(open);
    }

    public void CustomerEntered(Collider other)
    {
        if (!IsServer) return;

        CustomerAI customer = other.GetComponentInParent<CustomerAI>();
        if (customer != null)
            customer.TryEnterRestaurant();
    }
}