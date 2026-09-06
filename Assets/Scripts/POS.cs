using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class POS : NetworkBehaviour
{
    public GameObject startShift;
    public GameObject ingredientSelect;

    public TextMeshProUGUI text;

    public List<FoodIngredientDefinition> ingredientsForOrder = new List<FoodIngredientDefinition>();
    private readonly List<float> cookPercentagesForOrder = new();

    public string orderText;

    public override void OnNetworkSpawn()
    {
        UpdatePanelClientRpc(true, false);
    }

    public void StartShift()
    {
        StartShiftServerRpc();
    }

    public void SubmitOrder()
    {
        var names = new List<string>();
        var percentages = new List<string>();

        for (int i = 0; i < ingredientsForOrder.Count; i++)
        {
            float percentage = i < cookPercentagesForOrder.Count
                ? cookPercentagesForOrder[i]
                : -1f;

            names.Add(percentage >= 0f
                ? $"{ingredientsForOrder[i].IngredientName} ({percentage:0}%)"
                : ingredientsForOrder[i].IngredientName);
            percentages.Add(percentage
                .ToString("0"));
        }

        string ingredientString = string.Join(",", names);
        string cookPercentageString = string.Join(",", percentages);

        RegisterTest.Instance.NotifyOrderSubmittedServerRpc(ingredientString, cookPercentageString);
        ingredientsForOrder.Clear();
        cookPercentagesForOrder.Clear();
        orderText = "";
        text.text = orderText;
        UpdateOrderTextServerRpc(orderText);
        SubmitOrderServerRpc();
    }

    public void AddIngredient(FoodIngredientButtonDefinition ingredient)
    {
        ingredientsForOrder.Add(ingredient.ingredient);
        cookPercentagesForOrder.Add(-1f);
        orderText += ingredient.ingredient.IngredientName + "\n";
        text.text = orderText;
        UpdateOrderTextServerRpc(orderText);
    }

    public void SetLastCookPercentage(CookPreferenceButtonDefinition button)
    {
        if (button == null) return;
        if (ingredientsForOrder.Count == 0) return;

        int percentage = Mathf.Clamp(button.percentage, 0, 100);

        int lastIndex = ingredientsForOrder.Count - 1;
        while (cookPercentagesForOrder.Count < ingredientsForOrder.Count)
        {
            cookPercentagesForOrder.Add(-1f);
        }

        cookPercentagesForOrder[lastIndex] = Mathf.Clamp(percentage, 0, 100);

        string[] orderLines = orderText.Split('\n');
        if (lastIndex < orderLines.Length)
        {
            orderLines[lastIndex] = $"{ingredientsForOrder[lastIndex].IngredientName} ({percentage}%)";
            orderText = string.Join("\n", orderLines);
            text.text = orderText;
            UpdateOrderTextServerRpc(orderText);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    void UpdateOrderTextServerRpc(string newText)
    {
        UpdateOrderTextClientRpc(newText);
    }

    [ClientRpc]
    void UpdateOrderTextClientRpc(string newText)
    {
        text.text = newText;
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
        ingredientsForOrder.Clear();
        cookPercentagesForOrder.Clear();
        orderText = "";
        text.text = orderText;
        UpdatePanelClientRpc(true, false);
    }

    public void EndShift()
    {
        if (!NetworkManager.Singleton.IsHost) return;
        GameManager.Instance.EndShiftServerRpc();
    }
}