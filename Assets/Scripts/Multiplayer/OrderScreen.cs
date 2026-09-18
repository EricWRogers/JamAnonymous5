using UnityEngine;
using TMPro;
using System.Text;

public class OrderScreen : MonoBehaviour
{
    public int slot;
    public bool showAllOrders;
    public TMP_Text customerIdText;
    public TMP_Text ingredientsText;

    private OrderManager subscribedManager;

    void OnEnable()
    {
        BindManager();
        Refresh();
    }

    void Update()
    {
        // Managers may spawn after this screen, or be replaced between sessions.
        BindManager();
    }

    void BindManager()
    {
        var manager = OrderManager.Instance;
        if (manager == null) manager = null; // Normalize destroyed Unity objects.
        if (ReferenceEquals(subscribedManager, manager)) return;
        Unsubscribe();
        subscribedManager = manager;
        if (subscribedManager != null)
            subscribedManager.OrdersUpdated += Refresh;
        Refresh();
    }

    void OnDisable()
    {
        Unsubscribe();
    }

    void Unsubscribe()
    {
        if (!ReferenceEquals(subscribedManager, null))
            subscribedManager.OrdersUpdated -= Refresh;
        subscribedManager = null;
    }

    void Refresh()
    {
        if (showAllOrders)
        {
            var ingredients = new StringBuilder();
            if (subscribedManager != null)
            {
                foreach (var order in subscribedManager.GetActiveOrders())
                {
                    if (ingredients.Length > 0)
                        ingredients.Append('\n');
                    ingredients.Append('#').Append(order.CustomerId).Append(" — ");
                    ingredients.Append((order.Ingredients ?? "").Replace(",", " | "));
                }
            }
            SetText("ACTIVE ORDERS", ingredients.Length == 0 ? "No active orders" : ingredients.ToString());
            return;
        }

        ulong id = subscribedManager != null && slot >= 0 && slot < 3
            ? subscribedManager.GetSlotId(slot) : 0;
        SetText(id == 0 ? "" : $"#{id}", id == 0 ? "" :
            (subscribedManager.GetSlotIngredients(slot) ?? "").Replace(",", " | "));
    }

    void SetText(string ids, string ingredients)
    {
        if (customerIdText != null) customerIdText.text = ids;
        if (ingredientsText != null) ingredientsText.text = ingredients;
    }
}