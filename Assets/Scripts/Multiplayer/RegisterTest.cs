using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;
public class RegisterTest : NetworkBehaviour
{
    public static RegisterTest Instance;

    public Transform counterPoint;
    public Transform queueStart;
    public float queueSpacing = 1.5f;
    public Transform waitingQueueStart;
    public float waitingQueueSpacing = 1.5f;
    public bool playerIsPresent;

    public readonly List<CustomerAI> queue = new();
    public readonly List<CustomerAI> waitingForFoodQueue = new();
    public bool orderSubmitted = false;
    public bool OrderSubmitted => orderSubmitted;
    public ulong CurrentCustomerId => queue.Count > 0 ? queue[0].customerId.Value : 0;

    
    void Awake() 
    {
        Instance = this;
    } 

    public void JoinQueue(CustomerAI customer)
    {
        queue.Add(customer);
        UpdateQueuePositions();
    }

    public void LeaveQueue(CustomerAI customer)
    {
        queue.Remove(customer);
        UpdateQueuePositions();
    }

    public void JoinWaitingForFoodQueue(CustomerAI customer)
    {
        if (waitingForFoodQueue.Contains(customer)) return;

        waitingForFoodQueue.Add(customer);
        customer.IsInWaitingForFoodQueue = true;
        UpdateWaitingQueuePositions();
    }

    public void LeaveWaitingForFoodQueue(CustomerAI customer)
    {
        if (!waitingForFoodQueue.Remove(customer)) return;

        customer.IsInWaitingForFoodQueue = false;
        UpdateWaitingQueuePositions();
    }

    public bool IsFirstWaitingForFoodCustomer(CustomerAI customer)
    {
        return waitingForFoodQueue.Count > 0 && waitingForFoodQueue[0] == customer;
    }

    void UpdateQueuePositions()
    {
        for (int i = 0; i < queue.Count; i++)
        {
            Vector3 pos = i == 0 
                ? counterPoint.position 
                : queueStart.position - queueStart.forward * ((i - 1) * queueSpacing);
            queue[i].SetQueueDestination(pos, i == 0);
        }
    }

    void UpdateWaitingQueuePositions()
    {
        Transform start = waitingQueueStart != null ? waitingQueueStart : queueStart;
        if (start == null) return;

        for (int i = 0; i < waitingForFoodQueue.Count; i++)
        {
            Vector3 pos = start.position - start.forward * (i * waitingQueueSpacing);
            waitingForFoodQueue[i].SetQueueDestination(pos, false);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void NotifyOrderSubmittedServerRpc(string ingredientNames)
    {
        orderSubmitted = true;
        ulong customerId = queue.Count > 0 ? queue[0].customerId.Value : 0;

        GameManager.Instance.SubmitOrderServerRpc(customerId);
        OrderManager.Instance.AddOrder(customerId, ingredientNames);
    }


    public void ResetOrder() 
    {
        orderSubmitted = false;
    }
}