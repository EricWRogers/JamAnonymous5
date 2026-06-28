using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using TMPro;
using UnityEngine.SceneManagement;

public class LobbyUIManager : NetworkBehaviour
{
    public Transform container;
    public GameObject entryPrefab;

    private readonly List<GameObject> players = new();
    private NetworkList<ulong> clientIds;

    public TMP_Text joinCodeText;

    void Awake()
    {
        clientIds = new NetworkList<ulong>();
    }

    public override void OnNetworkSpawn()
    {
        joinCodeText.text = "CODE: " + GameManager.Instance.JoinCode;
        //We will be watching this on all clients so we know to update.
        clientIds.OnListChanged += OnListChanged;

        if (IsServer)
        {
            //Handy but only runs on host/server
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;

            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                GameManager.Instance.AssignColor(client.ClientId);
                clientIds.Add(client.ClientId);
            }
        }

        Rebuild();
    }

    public override void OnNetworkDespawn()
    {
        clientIds.OnListChanged -= OnListChanged;

        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }

    void OnClientConnected(ulong clientId) {

        GameManager.Instance.AssignColor(clientId);
        clientIds.Add(clientId);
    } 
    void OnClientDisconnected(ulong clientId) => clientIds.Remove(clientId);
    void OnListChanged(NetworkListEvent<ulong> _) => Rebuild();

    void Rebuild() //Just rebuild the list of clients whenever someone joins or leaves.
    {
        foreach (var obj in players) Destroy(obj);
        players.Clear();

        foreach (var clientId in clientIds)
{
        var obj = Instantiate(entryPrefab, container);
        var color = GameManager.Instance.GetColor(clientId);
        obj.GetComponent<PlayerLobbyUI>().SetName($"Player {clientId}", color);
        players.Add(obj);
}
    }

    public void StartGame()
    {
        if (!NetworkManager.Singleton.IsHost)
        {
            Debug.Log("Only host can start the game.");
            return;
        }

        NetworkManager.Singleton.SceneManager.LoadScene("Game", LoadSceneMode.Single);
    }

    public void LeaveLobby()
    {
        NetworkSessionMenu.ReturnToMainMenu();
    }
}
