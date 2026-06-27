using Unity.Netcode;
using UnityEngine;

public class SpawnProp : MonoBehaviour
{
    public GameObject prefab;
    public Transform[] spawnPoints;

    void Start()
    {
        if (!NetworkManager.Singleton.IsHost) return;

        SpawnPlayers();
    }

    void SpawnPlayers()
    {
        var clients = NetworkManager.Singleton.ConnectedClientsList;

        for (int i = 0; i < clients.Count; i++)
        {
            Transform spawnPoint = spawnPoints[i % spawnPoints.Length];

            GameObject player = Instantiate(prefab, spawnPoint.position, spawnPoint.rotation);

            player.GetComponent<NetworkObject>()
                  .SpawnAsPlayerObject(clients[i].ClientId);
        }
    }
}