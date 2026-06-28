using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
[System.Serializable]
public class MealTemplate
{
    
    public string mealName;
    public FoodIngredientDefinition[] ingredients;
}

public class CustomerAI : NetworkBehaviour
{
    public enum CustomerState
    {
        WalkingToRegister,
        InQueue,
        AtCounter,
        Yapping,
        WalkingToSeat,
        WaitingForFood,
        Eating,
        Leaving
    }

    public CustomerState State;

    [Header("Order")]
    public NetworkVariable<ulong> customerId = new NetworkVariable<ulong>();
    public List<FoodIngredientDefinition> wantedIngredients = new();
    private List<string> syncedIngredientNames = new();

    [Header("Possible Meals")]
    public MealTemplate[] possibleMeals;

    [Header("References")]
    public CustomerOrderUI orderUI;

    private NavMeshAgent agent;
    private Seat claimedSeat;

    public float eatingDuration = 10f;

    
    [Header("Food Placement")]
    public Vector3 foodOffset = new Vector3(0, 0, 0.5f);

    private Item deliveredTray;

    public override void OnNetworkSpawn()
    {
        if (!IsHost) return;

        agent = GetComponent<NavMeshAgent>();
        customerId.Value = (ulong)Random.Range(1000, 9999);
        GenerateOrder();

        RegisterTest.Instance.JoinQueue(this);
        SetState(CustomerState.WalkingToRegister);
    }

    void GenerateOrder()
    {
        if (possibleMeals == null || possibleMeals.Length == 0) return;
        var meal = possibleMeals[Random.Range(0, possibleMeals.Length)];
        wantedIngredients.AddRange(meal.ingredients);

        var names = new List<string>();
        foreach (var ing in wantedIngredients)
            names.Add(ing.IngredientName);

        syncedIngredientNames = names;
        SyncIngredientsClientRpc(string.Join(",", names));
    }

    [ClientRpc]
    void SyncIngredientsClientRpc(string ingredientNames)
    {
        if (IsHost) return;
        syncedIngredientNames = new List<string>(ingredientNames.Split(','));
    }

    public void SetQueueDestination(Vector3 position, bool isAtCounter)
    {
        if (agent == null) agent = GetComponent<NavMeshAgent>();
        agent.SetDestination(position);

        if (isAtCounter && State == CustomerState.InQueue)
            SetState(CustomerState.AtCounter);
    }

    [ClientRpc]
    void SyncStateClientRpc(CustomerState newState)
    {
        if (IsHost) return;
        State = newState;
    }

    public void SetState(CustomerState newState)
    {
        State = newState;
        SyncStateClientRpc(newState);

        switch (State)
        {
            case CustomerState.WalkingToRegister:
                break;

            case CustomerState.InQueue:
                break;

            case CustomerState.AtCounter:
                SetState(CustomerState.Yapping);
                break;

            case CustomerState.Yapping:
                orderUI.StartYapping(wantedIngredients);
                UpdateOrderUIClientRpc((int)CustomerState.Yapping);
                break;

            case CustomerState.WalkingToSeat:
                RegisterTest.Instance.LeaveQueue(this);
                orderUI.StopYapping();
                orderUI.ShowOrderNumber(customerId.Value);
                UpdateOrderUIClientRpc((int)CustomerState.WalkingToSeat);
                agent.SetDestination(claimedSeat.transform.position);
                break;

            case CustomerState.WaitingForFood:
                agent.ResetPath();
                break;

            case CustomerState.Eating:
                orderUI.Hide();
                UpdateOrderUIClientRpc((int)CustomerState.Eating);
                StartCoroutine(EatAndLeave());
                break;

            case CustomerState.Leaving:
                if (claimedSeat != null) claimedSeat.Vacate();
                    orderUI.Hide();
                UpdateOrderUIClientRpc((int)CustomerState.Leaving);
                
                if (deliveredTray != null && deliveredTray.NetworkObject != null)
                {
                    NetworkObject[] childNetObjs = deliveredTray.GetComponentsInChildren<NetworkObject>();
                    foreach (NetworkObject child in childNetObjs)
                    {
                        if (child != deliveredTray.NetworkObject && child.IsSpawned)
                            child.Despawn();
                    }

                    deliveredTray.NetworkObject.Despawn();
                    deliveredTray = null;
                }

                agent.SetDestination(CustomerSpawner.Instance.exitPoint.position);
                StartCoroutine(DestroyWhenArrived());
                break;
        }
    }

    void Update()
    {
        if (!IsHost) return;

        switch (State)
        {
            case CustomerState.WalkingToRegister:
                if (ArrivedAt(RegisterTest.Instance.queueStart.position))
                    SetState(CustomerState.InQueue);
                break;

            case CustomerState.InQueue:
                if (ArrivedAt(agent.destination) && 
                    RegisterTest.Instance.queue.Count > 0 && 
                    RegisterTest.Instance.queue[0] == this)
                    SetState(CustomerState.AtCounter);
                break;

            case CustomerState.Yapping:
                if (RegisterTest.Instance.OrderSubmitted)
                {
                    RegisterTest.Instance.ResetOrder();
                    ClaimSeat();
                    SetState(CustomerState.WalkingToSeat);
                }
                break;

            case CustomerState.WalkingToSeat:
                if (ArrivedAt(claimedSeat.transform.position))
                    SetState(CustomerState.WaitingForFood);
                break;

            case CustomerState.WaitingForFood:
                var order = GameManager.Instance.GetOrder(customerId.Value);
                if (order.HasValue && order.Value.State == OrderState.Delivered)
                    SetState(CustomerState.Eating);
                break;
        }
    }

    void ClaimSeat()
    {
        var seats = FindObjectsByType<Seat>(FindObjectsSortMode.None);
        Debug.Log($"Found {seats.Length} seats");
        
        foreach (var seat in seats)
        {
            Debug.Log($"Seat {seat.name} occupied: {seat.IsOccupied}");
            if (!seat.IsOccupied)
            {
                claimedSeat = seat;
                seat.Claim();
                Debug.Log($"Claimed seat {seat.name}");
                return;
            }
        }
        
        Debug.Log("No seat found, leaving");
        SetState(CustomerState.Leaving);
    }

    bool ArrivedAt(Vector3 destination)
    {
        if (agent == null) return false;
        if (agent.pathPending) return false;
        if (agent.remainingDistance > agent.stoppingDistance) return false;
        return true;
    }

    IEnumerator EatAndLeave()
    {
        yield return new WaitForSeconds(eatingDuration);
        SetState(CustomerState.Leaving);
    }

    IEnumerator DestroyWhenArrived()
    {
        yield return new WaitUntil(() => ArrivedAt(CustomerSpawner.Instance.exitPoint.position));
        if (IsHost) GetComponent<NetworkObject>().Despawn();
    }

    public void DeliverFood()
    {
    if (State != CustomerState.WaitingForFood) return;
    SetState(CustomerState.Eating);
    }


    [ClientRpc]
    void UpdateOrderUIClientRpc(int stateIndex)
    {
        if (IsHost) return;
        var state = (CustomerState)stateIndex;

        switch (state)
        {
            case CustomerState.Yapping:
                orderUI.StartYappingNames(syncedIngredientNames);
                break;
            case CustomerState.WalkingToSeat:
                orderUI.StopYapping();
                orderUI.ShowOrderNumber(customerId.Value);
                break;
            case CustomerState.Eating:
                orderUI.Hide();
                break;
            case CustomerState.Leaving:
                orderUI.Hide();
                break;
        }
    }


    public void ReceiveFood(Item tray)
    {
        deliveredTray = tray;

        tray.NetworkObject.RemoveOwnership();

        tray.ServerStopHolding(transform.position + transform.TransformDirection(foodOffset), transform.rotation);
        tray.NetworkObject.TrySetParent(transform);

        foreach (var rb in tray.GetComponentsInChildren<Rigidbody>())
            rb.isKinematic = true;

        foreach (var col in tray.GetComponentsInChildren<Collider>())
            col.enabled = false;

        LockFoodObjectClientRpc(tray.NetworkObject.NetworkObjectId);
        SetState(CustomerState.Eating);
    }

    [ClientRpc]
    void LockFoodObjectClientRpc(ulong trayNetId)
    {
        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(trayNetId, out NetworkObject netObj))
            return;

        foreach (var rb in netObj.GetComponentsInChildren<Rigidbody>())
            rb.isKinematic = true;

        foreach (var col in netObj.GetComponentsInChildren<Collider>())
            col.enabled = false;

    }
}