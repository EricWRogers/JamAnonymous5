using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class OrderManager : NetworkBehaviour
{
    public static OrderManager Instance;

    private ulong[] slotIds = new ulong[3] { 0, 0, 0 };
    private string[] slotIngredients = new string[3] { "", "", "" };
    private Queue<(ulong, string)> backlog = new();

    public event System.Action OrdersUpdated;

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
    }

    public void AddOrder(ulong customerId, string ingredientNames)
    {
        if (!IsServer) return;
        AddOrderClientRpc(customerId, ingredientNames);
    }

    [ClientRpc]
    void AddOrderClientRpc(ulong customerId, string ingredientNames)
    {
        ApplyAddOrder(customerId, ingredientNames);
    }

    // Local state operations are shared by RPC delivery and edit-mode regression tests.
    void ApplyAddOrder(ulong customerId, string ingredientNames)
    {
        for (int i = 0; i < 3; i++)
        {
            if (slotIds[i] == 0)
            {
                slotIds[i] = customerId;
                slotIngredients[i] = ingredientNames;
                OrdersUpdated?.Invoke();
                return;
            }
        }

        backlog.Enqueue((customerId, ingredientNames));
        OrdersUpdated?.Invoke();
    }

    public void ClearOrder(ulong customerId)
    {
        Debug.Log($"ClearOrder called, IsServer: {IsServer}, customerId: {customerId}");

        if (!IsServer) return;
        Debug.Log($"Firing ClearOrderClientRpc for {customerId}");
        ClearOrderClientRpc(customerId);
    }

    [ClientRpc]
    void ClearOrderClientRpc(ulong customerId)
    {
        ApplyClearOrder(customerId);
    }

    void ApplyClearOrder(ulong customerId)
    {
        if (customerId == 0) return;
        Debug.Log($"ClearOrderClientRpc received for {customerId}, checking {slotIds[0]}, {slotIds[1]}, {slotIds[2]}");
        for (int i = 0; i < 3; i++)
        {
            if (slotIds[i] == customerId)
            {
                if (backlog.Count > 0)
                {
                    var next = backlog.Dequeue();
                    slotIds[i] = next.Item1;
                    slotIngredients[i] = next.Item2;
                }
                else
                {
                    slotIds[i] = 0;
                    slotIngredients[i] = "";
                }

                OrdersUpdated?.Invoke();
                return;
            }
        }
        // Remove a queued order without changing FIFO order for remaining entries.
        bool removed = false;
        int count = backlog.Count;
        for (int i = 0; i < count; i++)
        {
            var order = backlog.Dequeue();
            if (!removed && order.Item1 == customerId)
                removed = true;
            else
                backlog.Enqueue(order);
        }
        if (removed) OrdersUpdated?.Invoke();
    }

    public void ClearAllOrders()
    {
        if (!IsServer) return;
        ClearAllOrdersClientRpc();
    }

    [ClientRpc]
    void ClearAllOrdersClientRpc()
    {
        ApplyClearAllOrders();
    }

    void ApplyClearAllOrders()
    {
        for (int i = 0; i < 3; i++)
        {
            slotIds[i] = 0;
            slotIngredients[i] = "";
        }
        backlog.Clear();
        OrdersUpdated?.Invoke();
    }

    public ulong GetSlotId(int slot) => slotIds[slot];
    public string GetSlotIngredients(int slot) => slotIngredients[slot];

    public IEnumerable<(ulong CustomerId, string Ingredients)> GetActiveOrders()
    {
        for (int i = 0; i < slotIds.Length; i++)
            if (slotIds[i] != 0)
                yield return (slotIds[i], slotIngredients[i]);
        foreach (var order in backlog)
            yield return (order.Item1, order.Item2);
    }
}