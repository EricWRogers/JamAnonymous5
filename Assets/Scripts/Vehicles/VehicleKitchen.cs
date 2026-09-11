using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DefaultExecutionOrder(-250)]
public sealed class VehicleKitchen : NetworkBehaviour
{
    [Serializable]
    public struct Station
    {
        public NetworkObject prefab;
        public Transform marker;
    }
    private static readonly HashSet<VehicleKitchen> Active = new();
    [SerializeField] private BoxCollider interior;
    [SerializeField] private Station[] stations = Array.Empty<Station>();
    private readonly List<NetworkObject> spawnedStations = new();
    private Vector3 previousPosition, velocity, angularVelocity;
    private Quaternion previousRotation;
    private Rigidbody body;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRegistry() => Active.Clear();

    public bool Contains(Vector3 point, float margin = 0f)
    {
        if (interior == null) return false;
        Vector3 p = interior.transform.InverseTransformPoint(point) - interior.center;
        Vector3 half = interior.size * 0.5f + Vector3.one * margin;
        return Mathf.Abs(p.x) <= half.x && Mathf.Abs(p.y) <= half.y && Mathf.Abs(p.z) <= half.z;
    }
    public Vector3 PointVelocity(Vector3 point) => body != null && !body.isKinematic
        ? body.GetPointVelocity(point) : velocity + Vector3.Cross(angularVelocity, point - transform.position);

    public override void OnNetworkSpawn()
    {
        body = GetComponent<Rigidbody>();
        previousPosition = transform.position;
        previousRotation = transform.rotation;
        Active.Add(this);
        if (!IsServer) return;
        foreach (Station station in stations)
        {
            if (station.prefab == null || station.marker == null) continue;
            if (!NetworkManager.NetworkConfig.Prefabs.Contains(station.prefab.gameObject))
            {
                Debug.LogError($"Kitchen station {station.prefab.name} is not registered in network prefabs.", this);
                continue;
            }
            NetworkObject instance = Instantiate(station.prefab, station.marker.position, station.marker.rotation);
            instance.DontDestroyWithOwner = true;
            instance.Spawn(destroyWithScene: true);
            if (instance.TryGetComponent(out Item item))
            {
                item.ServerAttachToSurface(NetworkObject, station.marker.position, station.marker.rotation);
                spawnedStations.Add(instance);
                continue;
            }
            if (!instance.TrySetParent(NetworkObject, true))
            {
                instance.Despawn(true);
                Debug.LogError("Could not parent kitchen station to truck.", this);
                continue;
            }
            spawnedStations.Add(instance);
        }
    }
    private void FixedUpdate()
    {
        if (!IsSpawned) return;
        velocity = (transform.position - previousPosition) / Time.fixedDeltaTime;
        Quaternion delta = transform.rotation * Quaternion.Inverse(previousRotation);
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f) angle -= 360f;
        angularVelocity = ItemPlacement.IsFinite(axis) ? axis * (angle * Mathf.Deg2Rad / Time.fixedDeltaTime) : Vector3.zero;
        previousPosition = transform.position;
        previousRotation = transform.rotation;
    }
    public override void OnNetworkDespawn()
    {
        Active.Remove(this);
        if (IsServer)
            foreach (NetworkObject station in spawnedStations)
                if (station != null && station.IsSpawned) station.Despawn(true);
        spawnedStations.Clear();
    }
    public static VehicleKitchen FindAt(Vector3 point)
    {
        foreach (VehicleKitchen kitchen in Active)
            if (kitchen != null && kitchen.IsSpawned && kitchen.Contains(point)) return kitchen;
        return null;
    }
}
