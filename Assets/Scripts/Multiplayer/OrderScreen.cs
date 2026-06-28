using UnityEngine;
using TMPro;

public class OrderScreen : MonoBehaviour
{
    public int slot;
    public TMP_Text customerIdText;
    public TMP_Text ingredientsText;

    void Start()
{
    if (OrderManager.Instance != null)
        OrderManager.Instance.OrdersUpdated += Refresh;
}

    void OnEnable()
    {
        if (OrderManager.Instance != null)
            OrderManager.Instance.OrdersUpdated += Refresh;
    }

    void OnDisable()
    {
        if (OrderManager.Instance != null)
            OrderManager.Instance.OrdersUpdated -= Refresh;
    }

   void Refresh()
    {
        ulong id = OrderManager.Instance.GetSlotId(slot);
        if (id == 0)
        {
            customerIdText.text = "";
            ingredientsText.text = "";
            return;
        }

        customerIdText.text = $"#{id}";
        ingredientsText.text = OrderManager.Instance.GetSlotIngredients(slot)
            .Replace(",", " | ");
    }
}