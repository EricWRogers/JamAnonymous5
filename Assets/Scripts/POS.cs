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
        RegisterTest.Instance.NotifyOrderSubmittedServerRpc();
        GameManager.Instance.SubmitOrderServerRpc(RegisterTest.Instance.CurrentCustomerId);
        ingredientsForOrder.Clear();
        orderText = "";
        text.text = orderText;
        SubmitOrderServerRpc();
    }

    public void AddIngredient(FoodIngredientButtonDefinition ingredient)
    {
        ingredientsForOrder.Add(ingredient.ingredient);
        orderText += ingredient.ingredient.IngredientName + "\n";
        text.text = orderText;
        UpdateOrderTextServerRpc(orderText);
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
}