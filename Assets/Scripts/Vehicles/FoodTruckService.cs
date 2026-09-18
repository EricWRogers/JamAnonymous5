using System;
using UnityEngine;

/// <summary>Scene queue/board bindings for the prefab-owned truck register.
/// The POS is a real child of the truck; its authored transform is never written here.</summary>
[DefaultExecutionOrder(250)]
public sealed class FoodTruckService : MonoBehaviour
{
    [Serializable]
    public struct Attachment
    {
        public Transform target;
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 localScale;
    }

    [SerializeField] private Transform counterPoint;
    [SerializeField] private Transform queueStart;
    [SerializeField] private Transform waitingQueueStart;
    [SerializeField] private CustomerEntryTrigger entryTrigger;
    [SerializeField] private Attachment[] attachments = Array.Empty<Attachment>();
    private RegisterTest register;
    private Transform truck;
    private float nextQueueRefresh;

    public void Bind(Transform vehicle)
    {
        if (vehicle == null) return;
        var target = vehicle.GetComponentInChildren<RegisterTest>(true);
        if (target == null) return;
        truck = vehicle;
        register = target;
        register.counterPoint = counterPoint;
        register.queueStart = queueStart;
        register.waitingQueueStart = waitingQueueStart;
        if (entryTrigger != null) entryTrigger.pos = register.GetComponent<POS>();
        register.SetTakeawayMode();
        if (vehicle.TryGetComponent<FoodTruckShop>(out var shop)) shop.Bind(register);
        foreach (Attachment attachment in attachments)
            if (attachment.target != null && attachment.target.GetComponent<OrderScreen>() != null)
                foreach (Collider displayCollider in attachment.target.GetComponentsInChildren<Collider>())
                    displayCollider.enabled = false;
        FollowTruck();
    }

    public void Unbind(Transform vehicle)
    {
        if (truck != vehicle) return;
        if (entryTrigger != null && register != null && entryTrigger.pos == register.GetComponent<POS>())
            entryTrigger.pos = null;
        register = null;
        truck = null;
    }

    public void FollowTruck()
    {
        if (truck == null || register == null) return;
        foreach (Attachment attachment in attachments)
        {
            // Never let stale scene authoring override any prefab-owned child.
            if (attachment.target == null || attachment.target.IsChildOf(truck)) continue;
            attachment.target.SetPositionAndRotation(truck.TransformPoint(attachment.localPosition),
                truck.rotation * Quaternion.Euler(attachment.localEulerAngles));
            attachment.target.localScale = attachment.localScale;
        }
        if (register.IsServer && Time.time >= nextQueueRefresh)
        {
            register.RefreshQueuePositions();
            nextQueueRefresh = Time.time + 0.2f;
        }
    }

    private void LateUpdate() => FollowTruck();
}
