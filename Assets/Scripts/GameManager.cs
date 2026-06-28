using UnityEngine;
using Unity.Netcode;  
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System.Collections;
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance;

    public string JoinCode;

    public NetworkVariable<float> shiftTimer = new();
    public NetworkVariable<bool> shiftStarted = new();
    public float maxShiftTime = 600f; //10 minutes. The huds know what to do with this. Should be 8pm is the end of shift.

    private readonly Color32[] palette = new Color32[]
    {
        new(220, 80,  80,  255), //red
        new(80,  140, 220, 255), //blue
        new(80,  200, 120, 255), //green
        new(220, 180, 60,  255), //yellow
    };

    private readonly Dictionary<ulong, Color32> playerColors = new();

    private readonly List<Order> activeOrders = new();
    public event System.Action OrdersChanged;




    ulong[] GetIds() => new List<ulong>(playerColors.Keys).ToArray();
    Color32[] GetColors() => new List<Color32>(playerColors.Values).ToArray();

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public override void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }

        base.OnDestroy();
    }

    public void SetJoinCode(string code)
    {
        JoinCode = code;
    }


    void Update()
    {
        if (!IsServer) return;

        if (shiftStarted.Value && SceneManager.GetActiveScene().name == "Game")
        {
            shiftTimer.Value = Mathf.Min(shiftTimer.Value + Time.deltaTime, maxShiftTime);

            if (shiftTimer.Value >= maxShiftTime)
            {
                shiftStarted.Value = false;
                shiftTimer.Value = 0f;
            }
        }
    }

    public string GetFormattedTime()
    {
        float progress = shiftTimer.Value / maxShiftTime;
        float totalInGameMinutes = progress * 14 * 60; //14 hours in min

        int hour = 6 + Mathf.FloorToInt(totalInGameMinutes / 60);
        int minutes = Mathf.FloorToInt(totalInGameMinutes % 60);

        //Snap to :00 or :30. This way we don't have a stopwatch eque situation which isnt very cool.
        minutes = minutes >= 30 ? 30 : 0;

        string period = hour >= 12 ? "PM" : "AM";
        int displayHour = hour > 12 ? hour - 12 : hour;

        return minutes == 0
            ? $"{displayHour}{period}"
            : $"{displayHour}:{minutes:D2}{period}";
    }

    public void AssignColor(ulong clientId)
    {
        if (playerColors.ContainsKey(clientId)) return;
        playerColors[clientId] = palette[playerColors.Count % palette.Length];
        SyncColorsClientRpc(GetIds(), GetColors());
    }

    public Color32 GetColor(ulong clientId)
    {
        return playerColors.TryGetValue(clientId, out var color) ? color : Color.magenta;
    }

    [ClientRpc]
    void SyncColorsClientRpc(ulong[] ids, Color32[] colors)
    {
        playerColors.Clear();
        for (int i = 0; i < ids.Length; i++)
        {
            playerColors[ids[i]] = colors[i];
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void StartShiftServerRpc()
    {
        if (shiftStarted.Value) return; 
        shiftStarted.Value = true;
    }

    //Orders ==========================
[ServerRpc(RequireOwnership = false)]
public void SubmitOrderServerRpc(ulong customerId)
{
    activeOrders.Add(new Order
    {
        CustomerId = customerId,
        State = OrderState.Submitted,
        WantedIngredients = new string[0],
        ReceivedIngredients = new string[0]
    });
    SyncOrdersClientRpc(SerializeOrders());
}

[ServerRpc(RequireOwnership = false)]
public void MarkOrderReadyServerRpc(ulong customerId)
{
    for (int i = 0; i < activeOrders.Count; i++)
    {
        if (activeOrders[i].CustomerId != customerId) continue;
        var order = activeOrders[i];
        order.State = OrderState.Ready;
        activeOrders[i] = order;
        SyncOrdersClientRpc(SerializeOrders());
        return;
    }
}

[ServerRpc(RequireOwnership = false)]
public void DeliverOrderServerRpc(ulong customerId)
{
    for (int i = 0; i < activeOrders.Count; i++)
    {
        if (activeOrders[i].CustomerId != customerId) continue;
        var order = activeOrders[i];
        order.State = OrderState.Delivered;
        activeOrders[i] = order;
        SyncOrdersClientRpc(SerializeOrders());
        StartCoroutine(ClearOrderAfterDelay(customerId));
        return;
    }
}

IEnumerator ClearOrderAfterDelay(ulong customerId)
{
    yield return new WaitForSeconds(2f);
    for (int i = 0; i < activeOrders.Count; i++)
    {
        if (activeOrders[i].CustomerId != customerId) continue;
        activeOrders.RemoveAt(i);
        SyncOrdersClientRpc(SerializeOrders());
        yield break;
    }
}

public Order? GetOrder(ulong customerId)
{
    foreach (var order in activeOrders)
        if (order.CustomerId == customerId) return order;
    return null;
}

public List<Order> AllOrders() => activeOrders;

string SerializeOrders()
{
    var sb = new System.Text.StringBuilder();
    foreach (var order in activeOrders)
        sb.Append($"{order.CustomerId},{(int)order.State};");
    return sb.ToString();
}

[ClientRpc]
void SyncOrdersClientRpc(string data)
{
    activeOrders.Clear();
    if (string.IsNullOrEmpty(data)) return;

    foreach (var entry in data.Split(';'))
    {
        if (string.IsNullOrEmpty(entry)) continue;
        var parts = entry.Split(',');
        activeOrders.Add(new Order
        {
            CustomerId = ulong.Parse(parts[0]),
            State = (OrderState)int.Parse(parts[1]),
            WantedIngredients = new string[0],
            ReceivedIngredients = new string[0]
        });
    }

    OrdersChanged?.Invoke();
}
            
}
