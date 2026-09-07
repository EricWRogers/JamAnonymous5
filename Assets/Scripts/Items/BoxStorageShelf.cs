using Unity.Netcode;
using UnityEngine;

/// <summary>Trigger volumes describe the usable space of individual box compartments.</summary>
[RequireComponent(typeof(NetworkObject))]
public sealed class BoxStorageShelf : NetworkBehaviour
{
    [SerializeField] private BoxCollider[] compartments = System.Array.Empty<BoxCollider>();

    public bool IsCompartment(Collider collider) => System.Array.IndexOf(compartments, collider) >= 0;

    public bool TryGetPlacement(Item item, Collider target, out Vector3 position, out Quaternion rotation, out bool valid)
    {
        position = item.transform.position;
        rotation = transform.rotation;
        valid = false;
        if (!IsSpawned || target is not BoxCollider slot || !IsCompartment(slot) ||
            !slot.enabled || !slot.isTrigger || !item.TryGetComponent(out IngredientBox box) ||
            !ItemPlacement.TryGetBounds(item, out Bounds bounds)) return false;

        rotation = slot.transform.rotation;
        Vector3 scale = item.transform.lossyScale;
        Vector3 size = Vector3.Scale(bounds.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        Vector3 slotScale = slot.transform.lossyScale;
        Vector3 space = Vector3.Scale(slot.size, new Vector3(Mathf.Abs(slotScale.x), Mathf.Abs(slotScale.y), Mathf.Abs(slotScale.z)));
        Vector3 center = slot.transform.TransformPoint(slot.center) - slot.transform.up * ((space.y - size.y) * 0.5f - 0.01f);
        position = center - rotation * Vector3.Scale(bounds.center, scale);
        if (!box.IsPurchased || size.x > space.x - 0.02f || size.y > space.y - 0.02f || size.z > space.z - 0.02f) return true;

        // Replicated attachment state reserves a compartment even before physics
        // catches up with a newly placed box or a late-joining object's pose.
        foreach (NetworkObject obj in NetworkManager.SpawnManager.SpawnedObjectsList)
        {
            if (!obj.TryGetComponent(out Item other) || other == item || other.IsHeld || !other.IsAttachedTo(NetworkObject)) continue;
            Vector3 local = slot.transform.InverseTransformPoint(other.AttachmentWorldPosition) - slot.center;
            if (Mathf.Abs(local.x) <= slot.size.x * 0.5f && Mathf.Abs(local.y) <= slot.size.y * 0.5f &&
                Mathf.Abs(local.z) <= slot.size.z * 0.5f) return true;
        }
        foreach (Collider overlap in Physics.OverlapBox(center, size * 0.5f, rotation,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!overlap.transform.IsChildOf(item.transform)) return true;
        valid = true;
        return true;
    }
}
