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
    }

    public ulong GetSlotId(int slot) => slotIds[slot];
    public string GetSlotIngredients(int slot) => slotIngredients[slot];
}