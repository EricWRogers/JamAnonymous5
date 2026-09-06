using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>Spawns the shared food truck once on the host when the gameplay scene starts.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class VehicleSpawner : NetworkBehaviour
{
    [SerializeField] private GameObject vehiclePrefab;
    private NetworkObject spawnedVehicle;
    private Coroutine pendingSpawn;

    public override void OnNetworkSpawn()
    {
        if (!IsServer || spawnedVehicle != null || pendingSpawn != null) return;
        pendingSpawn = StartCoroutine(SpawnAfterSceneNotification());
    }

    private IEnumerator SpawnAfterSceneNotification()
    {
        yield return null;
        pendingSpawn = null;
        if (!IsSpawned || !IsServer || spawnedVehicle != null) yield break;
        if (vehiclePrefab == null || vehiclePrefab.GetComponent<VehicleController>() == null)
        {
            Debug.LogError("VehicleSpawner requires a vehicle prefab with VehicleController.", this);
            yield break;
        }
        GameObject instance = Instantiate(vehiclePrefab, transform.position, transform.rotation);
        spawnedVehicle = instance.GetComponent<NetworkObject>();
        spawnedVehicle.DontDestroyWithOwner = true;
        spawnedVehicle.Spawn(destroyWithScene: true);
    }

    public override void OnNetworkDespawn()
    {
        if (pendingSpawn != null) StopCoroutine(pendingSpawn);
        pendingSpawn = null;
    }
}
