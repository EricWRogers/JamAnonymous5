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
    private readonly List<float> wantedCookPercentages = new();
    private List<string> syncedIngredientNames = new();

    [Header("Possible Meals")]
    public MealTemplate[] possibleMeals;

    [Header("References")]
    public CustomerOrderUI orderUI;

    private NavMeshAgent agent;
    private Seat claimedSeat;
    private bool hasDecidedToEnter;
    public bool IsInWaitingForFoodQueue { get; set; }

    public float eatingDuration = 10f;
    [Range(0f, 1f)] public float chanceToEnter = 0.5f;
    [Min(0.1f)] public float entryRetryCooldown = 5f;
    public float burgerCooldown = 60f;
    private float nextBurgerTime;
    private float nextEntryAttemptTime;

    [Header("Sidewalk Roaming")]
    public string sidewalkAreaName = "Sidewalk";
    public float sidewalkRoamRadius = 20f;

    
    [Header("Food Placement")]
    public Vector3 foodOffset = new Vector3(0, 0, 0.5f);

    private Item deliveredTray;

    public Renderer customerRenderer;

    [Header("Food Scoring")]
    public int flatRatePerIngredient = 2;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        agent.enabled = false;
    }

   
    public override void OnNetworkSpawn()
    {
        if (!IsHost) return;

        agent.enabled = true;
        customerId.Value = (ulong)Random.Range(1000, 9999);
        GenerateOrder();

        Color randomColor = Random.ColorHSV(0f, 1f, 0.5f, 1f, 0.7f, 1f);
        customerRenderer.material.color = randomColor;
        SetColorClientRpc(randomColor.r, randomColor.g, randomColor.b);


        SetState(CustomerState.WalkingToRegister);
        SetSidewalkMovement();
        ChooseSidewalkDestination();
    }

    public void TryEnterRestaurant()
    {
        if (!IsServer || hasDecidedToEnter || State != CustomerState.WalkingToRegister ||
            GameManager.Instance == null || !GameManager.Instance.shiftStarted.Value ||
            Time.time < nextBurgerTime || Time.time < nextEntryAttemptTime)
            return;

        if (Random.value <= chanceToEnter)
        {
            hasDecidedToEnter = true;
            agent.areaMask = NavMesh.AllAreas;
            RegisterTest.Instance.JoinQueue(this);
            SetState(CustomerState.InQueue);
        }
        else
        {
            nextEntryAttemptTime = Time.time + entryRetryCooldown;
        }
    }

    void GenerateOrder()
    {
        if (possibleMeals == null || possibleMeals.Length == 0) return;
        var meal = possibleMeals[Random.Range(0, possibleMeals.Length)];
        wantedIngredients.AddRange(meal.ingredients);
        wantedCookPercentages.Clear();

        var names = new List<string>();
        foreach (var ing in wantedIngredients)
        {
            float requestedCookPercentage = ing != null && ing.CanBeCooked
                ? (float)(FoodCookPreference)new[]
                {
                    FoodCookPreference.TwentyFive,
                    FoodCookPreference.Fifty,
                    FoodCookPreference.SeventyFive,
                    FoodCookPreference.OneHundred
                }[Random.Range(0, 4)]
                : -1f;

            wantedCookPercentages.Add(requestedCookPercentage);
            names.Add(requestedCookPercentage >= 0f
                ? $"{ing.IngredientName} ({requestedCookPercentage:0}%)"
                : ing.IngredientName);
        }

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
                orderUI.StartYappingNames(syncedIngredientNames);
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
                StartCoroutine(ReturnToSidewalkWhenArrived());
                break;
        }
    }

    void Update()
    {
        if (!IsHost) return;

        switch (State)
        {
            case CustomerState.WalkingToRegister:
                if (ArrivedAt(agent.destination))
                    ChooseSidewalkDestination();
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
                    if (ClaimSeat())
                    {
                        SetState(CustomerState.WalkingToSeat);
                    }
                    else
                    {
                        RegisterTest.Instance.LeaveQueue(this);
                        SetState(CustomerState.WaitingForFood);
                        RegisterTest.Instance.JoinWaitingForFoodQueue(this);
                    }
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

    bool ClaimSeat()
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
                return true;
            }
        }
        
        Debug.Log("No seat found, joining the waiting-for-food queue");
        return false;
    }

    void SetSidewalkMovement()
    {
        int areaIndex = NavMesh.GetAreaFromName(sidewalkAreaName);
        if (areaIndex < 0)
        {
            Debug.LogWarning($"NavMesh area '{sidewalkAreaName}' was not found. Using all areas.");
            agent.areaMask = NavMesh.AllAreas;
            return;
        }

        agent.areaMask = 1 << areaIndex;
    }

    void ChooseSidewalkDestination()
    {
        Vector2 offset = Random.insideUnitCircle * sidewalkRoamRadius;
        Vector3 candidate = transform.position + new Vector3(offset.x, 0f, offset.y);
        int areaIndex = NavMesh.GetAreaFromName(sidewalkAreaName);
        int sidewalkMask = areaIndex >= 0 ? 1 << areaIndex : NavMesh.AllAreas;

        if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, sidewalkRoamRadius, sidewalkMask))
            agent.SetDestination(hit.position);
        else if (CustomerEntryTrigger.Instance != null)
            agent.SetDestination(CustomerEntryTrigger.Instance.transform.position);
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

    IEnumerator ReturnToSidewalkWhenArrived()
    {
        yield return new WaitUntil(() => ArrivedAt(CustomerSpawner.Instance.exitPoint.position));
        if (IsHost)
        {
            nextBurgerTime = Time.time + burgerCooldown;
            hasDecidedToEnter = false;
            SetState(CustomerState.WalkingToRegister);
            SetSidewalkMovement();
            ChooseSidewalkDestination();
        }
    }

    public void DeliverFood()
    {
        if (State != CustomerState.WaitingForFood) return;
        if (IsInWaitingForFoodQueue && !RegisterTest.Instance.IsFirstWaitingForFoodCustomer(this)) return;
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
        if (IsInWaitingForFoodQueue)
            RegisterTest.Instance.LeaveWaitingForFoodQueue(this);

        deliveredTray = tray;

        float score = ScoreOrder(tray);
        float maxScore = wantedIngredients.Count;
        float scoreRatio = Mathf.Clamp01(score / maxScore);

        float flatRatePerIngredient = 2f; 
        int payout = Mathf.RoundToInt(wantedIngredients.Count * flatRatePerIngredient * scoreRatio);

        Debug.Log($"Order score: {score}/{maxScore}, Payout: ${payout:F2}");

        RestaurantMoney.Instance.ServerAddMoney(payout);
        ShowScoreClientRpc(score, maxScore);
        OrderManager.Instance.ClearOrder(customerId.Value);

        tray.NetworkObject.RemoveOwnership();
        tray.ServerStopHolding(transform.position + transform.TransformDirection(foodOffset), transform.rotation);
        tray.NetworkObject.TrySetParent(transform);

        foreach (var rb in tray.GetComponentsInChildren<Rigidbody>())
            rb.isKinematic = true;

        foreach (var col in tray.GetComponentsInChildren<Collider>())
            col.enabled = false;

        LockFoodObjectClientRpc(tray.NetworkObject.NetworkObjectId);
        SetState(CustomerState.Eating);
        ShowScoreClientRpc(score, maxScore); 
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

    public string GetIngredientNamesString()
    {
        return string.Join(",", syncedIngredientNames);
    }

    float ScoreOrder(Item tray)
    {
        ServingTray servingTray = tray != null ? tray.GetComponent<ServingTray>() : null;
        IReadOnlyList<FoodIngredient> delivered = servingTray != null
            ? servingTray.GetCarriedIngredients()
            : tray.GetComponentsInChildren<FoodIngredient>();
        var wanted = new List<FoodIngredientDefinition>(wantedIngredients);

        float score = 0f;

        foreach (var ingredient in delivered)
        {
            if (ingredient.Definition == null) continue;

            bool exactMatch = false;
            for (int i = 0; i < wanted.Count; i++)
            {
                if (wanted[i] == ingredient.Definition)
                {
                    float ingredientScore = 1f;
                    float requestedCookPercentage = i < wantedCookPercentages.Count
                        ? wantedCookPercentages[i]
                        : -1f;

                    if (requestedCookPercentage >= 0f && ingredient.CanBeCooked)
                    {
                        float actualCookPercentage = ingredient.CookProgress * 100f;
                        float cookDifference = Mathf.Abs(actualCookPercentage - requestedCookPercentage);
                        const float forgivingCookThreshold = 5f;
                        ingredientScore = cookDifference <= forgivingCookThreshold
                            ? 1f
                            : 1f - (cookDifference - forgivingCookThreshold) /
                                (100f - forgivingCookThreshold);
                    }

                    score += Mathf.Clamp01(ingredientScore);
                    wanted.RemoveAt(i);
                    wantedCookPercentages.RemoveAt(i);
                    exactMatch = true;
                    break;
                }
            }

            if (exactMatch) continue;

            if (ingredient.Definition.IsTrash)
            {
                bool subMatch = false;
                for (int i = 0; i < wanted.Count; i++)
                {
                    if (wanted[i].TrashEquivalent == ingredient.Definition)
                    {
                        score += 0.5f;
                        wanted.RemoveAt(i);
                        if (i < wantedCookPercentages.Count)
                        {
                            wantedCookPercentages.RemoveAt(i);
                        }
                        subMatch = true;
                        break;
                    }
                }

                if (!subMatch) score -= 0.5f;
            }
        }

        score -= wanted.Count * 0.5f;

        return score;
    }

    [ClientRpc]
    void ShowScoreClientRpc(float score, float maxScore)
    {
        orderUI.ShowScore(score, maxScore);
    }


    [ClientRpc]
    void SetColorClientRpc(float r, float g, float b)
    {
        if (IsHost) return;
        customerRenderer.material.color = new Color(r, g, b);
    }
}
