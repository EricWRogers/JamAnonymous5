using Unity.Netcode;
using UnityEngine;

public class RestaurantMoney : NetworkBehaviour
{
    public static RestaurantMoney Instance { get; private set; }

    [SerializeField] private int startingMoney = 100;

    private NetworkVariable<int> money = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public int Money => money.Value;

    public override void OnNetworkSpawn()
    {
        Instance = this;

        if (IsServer)
        {
            money.Value = startingMoney;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public bool ServerTrySpend(int amount)
    {
        if (!IsServer) return false;
        if (amount < 0) return false;
        if (money.Value < amount) return false;

        money.Value -= amount;
        return true;
    }

    public void ServerAddMoney(int amount)
    {
        if (!IsServer) return;
        if (amount <= 0) return;

        money.Value += amount;
    }
}
