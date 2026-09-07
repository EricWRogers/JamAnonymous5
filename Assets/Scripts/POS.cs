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
        UpdatePanelClientRpc(true, false);
    }

    public override void OnNetworkDespawn()
    {
        networkOrderText.OnValueChanged -= OnOrderTextChanged;
        base.OnNetworkDespawn();
    }

    public void StartShift()
    {
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
        if (string.IsNullOrWhiteSpace(ingredientName)) return;

        serverIngredientNames.Add(ingredientName);
        serverCookPercentages.Add(-1f);
        RefreshServerOrderText();
    }

    [ServerRpc(RequireOwnership = false)]
    private void RemoveLastIngredientServerRpc()
    {
        if (serverIngredientNames.Count == 0) return;

        serverIngredientNames.RemoveAt(serverIngredientNames.Count - 1);
        serverCookPercentages.RemoveAt(serverCookPercentages.Count - 1);
        RefreshServerOrderText();
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetLastCookPercentageServerRpc(int percentage)
    {
        if (serverCookPercentages.Count == 0) return;
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
        GameManager.Instance.StartShiftServerRpc();
        UpdatePanelClientRpc(false, true);
    }

    [ServerRpc(RequireOwnership = false)]
    void SubmitOrderServerRpc()
    {
        if (serverIngredientNames.Count == 0) return;

        var names = new List<string>();
        for (int i = 0; i < serverIngredientNames.Count; i++)
        {
            float percentage = serverCookPercentages[i];
            names.Add(percentage >= 0f
                ? $"{serverIngredientNames[i]} ({percentage:0}%)"
                : serverIngredientNames[i]);
        }

        RegisterTest.Instance.NotifyOrderSubmittedServerRpc(string.Join(",", names));
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
        UpdatePanelClientRpc(true, false);
    }

    public void EndShift()
    {
        if (!NetworkManager.Singleton.IsHost) return;
        GameManager.Instance.EndShiftServerRpc();
    }

    public void CustomerEntered(Collider other)
    {
        if (!IsServer) return;

        CustomerAI customer = other.GetComponentInParent<CustomerAI>();
        if (customer != null)
            customer.TryEnterRestaurant();
    }
}