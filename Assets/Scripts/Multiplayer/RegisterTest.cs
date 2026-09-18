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
    // Service policy must exist before the runtime truck spawns. A transient
    // Bind-only flag allowed the normal order flow to fall back to restaurant seats.
    [SerializeField] private bool takeawayOnly;
    public bool TakeawayOnly => takeawayOnly;
    public FoodTruckShop Shop { get; private set; }
    // Truck registers fail closed even before the dynamically spawned truck binds.
    [SerializeField] private bool requiresOpenShop;
    public bool RequiresOpenShop => requiresOpenShop;
    public bool CanAcceptCustomers => !requiresOpenShop || (Shop != null && Shop.IsOpen);
    public void BindShop(FoodTruckShop shop) { Shop = shop; requiresOpenShop = true; }
    public void UnbindShop(FoodTruckShop shop) { if (Shop == shop) Shop = null; }
    public void SetTakeawayMode() => takeawayOnly = true;

    public readonly List<CustomerAI> queue = new();
    public readonly List<CustomerAI> waitingForFoodQueue = new();
    public bool orderSubmitted = false;
    public bool OrderSubmitted => orderSubmitted;
    public ulong CurrentCustomerId => queue.Count > 0 ? queue[0].customerId.Value : 0;

    
    void Awake() 
    {
        Instance = this;
    } 

    public override void OnNetworkSpawn() => Instance = this;

    public override void OnNetworkDespawn()
    {
        if (Instance == this) Instance = null;
        base.OnNetworkDespawn();
    }

    public void JoinQueue(CustomerAI customer)
    {
        if (!CanAcceptCustomers || customer == null || queue.Contains(customer)) return;
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
        if (!CanAcceptCustomers || customer == null || waitingForFoodQueue.Contains(customer)) return;

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
        queue.RemoveAll(customer => customer == null);
        if (counterPoint == null || queueStart == null) return;
        for (int i = 0; i < queue.Count; i++)
        {
            Vector3 pos = i == 0
                ? counterPoint.position
                : queueStart.position - queueStart.forward * ((i - 1) * queueSpacing);
            queue[i].SetQueueDestination(pos);
        }
    }

    void UpdateWaitingQueuePositions()
    {
        waitingForFoodQueue.RemoveAll(customer => customer == null);
        Transform start = waitingQueueStart != null ? waitingQueueStart : queueStart;
        if (start == null) return;

        for (int i = 0; i < waitingForFoodQueue.Count; i++)
        {
            Vector3 pos = start.position - start.forward * (i * waitingQueueSpacing);
            waitingForFoodQueue[i].SetQueueDestination(pos);
        }
    }

    public void RefreshQueuePositions()
    {
        UpdateQueuePositions();
        UpdateWaitingQueuePositions();
    }

    [ServerRpc(RequireOwnership = false)]
    public void NotifyOrderSubmittedServerRpc(string ingredientNames)
    {
        if (!CanAcceptCustomers) return;
        queue.RemoveAll(customer => customer == null);
        if (queue.Count == 0 || queue[0].State != CustomerAI.CustomerState.Yapping ||
            string.IsNullOrWhiteSpace(ingredientNames)) return;
        CustomerAI customer = queue[0];
        ulong customerId = customer.customerId.Value;
        GameManager.Instance.SubmitOrderServerRpc(customerId);
        OrderManager.Instance.AddOrder(customerId, ingredientNames);
        customer.OnOrderSubmitted();
        orderSubmitted = false;
    }


    public void ServerDismissCustomers()
    {
        if (!IsServer) return;
        var pickup = waitingForFoodQueue.ToArray();
        var ordering = queue.ToArray();
        // Clear before callbacks so no head advances into an order during closing.
        waitingForFoodQueue.Clear();
        queue.Clear();
        orderSubmitted = false;
        playerIsPresent = false;
        foreach (var customer in pickup)
            if (customer != null) customer.ServerDismissForShopClose(true);
        foreach (var customer in ordering)
            if (customer != null) customer.ServerDismissForShopClose(false);
        if (OrderManager.Instance != null) OrderManager.Instance.ClearAllOrders();
        if (TryGetComponent<POS>(out var pos)) pos.ResetToStartShift();
    }

    public void ResetOrder() 
    {
        orderSubmitted = false;
    }
}