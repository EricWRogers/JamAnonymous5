using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;
public class RegisterTest : NetworkBehaviour
{
    public static RegisterTest Instance;

    public Transform counterPoint;
    public Transform queueStart;
    public float queueSpacing = 1.5f;
    public bool playerIsPresent;

    public readonly List<CustomerAI> queue = new();
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

    [ServerRpc(RequireOwnership = false)]
    public void NotifyOrderSubmittedServerRpc()
    {
        orderSubmitted = true;
        ulong customerId = queue.Count > 0 ? queue[0].customerId.Value : 0;
        GameManager.Instance.SubmitOrderServerRpc(customerId);
    }

    

    public void ResetOrder() 
    {
        orderSubmitted = false;
    }
}