using UnityEngine;
using System.Collections;
using Unity.Netcode;

public class CustomerSpawner : NetworkBehaviour
{
    public static CustomerSpawner Instance;

    public GameObject customerPrefab;
    public Transform spawnPoint;
    public Transform exitPoint;
    public float spawnInterval = 10f;
    public int desiredCustomerCount = 8;

    void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsHost)
            StartCoroutine(MaintainCustomerPopulation());
    }

    IEnumerator MaintainCustomerPopulation()
    {
        while (IsHost)
        {
            int currentCustomerCount = FindObjectsByType<CustomerAI>(FindObjectsSortMode.None).Length;
            int customersToSpawn = Mathf.Max(0, desiredCustomerCount - currentCustomerCount);

            for (int i = 0; i < customersToSpawn; i++)
                SpawnCustomer();

            yield return new WaitForSeconds(spawnInterval);
        }
    }

    void SpawnCustomer()
    {
        var go = Instantiate(customerPrefab, spawnPoint.position, spawnPoint.rotation);
        go.GetComponent<NetworkObject>().Spawn();
    }
}