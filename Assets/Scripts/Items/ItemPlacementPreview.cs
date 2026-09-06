using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public sealed class ItemPlacementPreview
{
    private GameObject root;
    private Material material;
    private Item source;
    private readonly Dictionary<Transform, Transform> ghostTransforms = new();
    private readonly List<(MeshRenderer source, MeshRenderer ghost)> ghostRenderers = new();
    private int meshCount;

    public void Show(Item item, Vector3 position, Quaternion rotation, bool valid)
    {
        if (source != item || meshCount != item.GetComponentsInChildren<MeshFilter>(true).Length) Build(item);
        if (root == null) return;
        root.SetActive(true);
        root.transform.localScale = item.transform.lossyScale;
        foreach (var pair in ghostTransforms)
        {
            if (pair.Key == null || pair.Value == null || pair.Key == item.transform) continue;
            pair.Value.localPosition = pair.Key.localPosition;
            pair.Value.localRotation = pair.Key.localRotation;
            pair.Value.localScale = pair.Key.localScale;
            pair.Value.gameObject.SetActive(pair.Key.gameObject.activeSelf);
        }
        foreach (var pair in ghostRenderers)
            if (pair.source != null && pair.ghost != null) pair.ghost.enabled = pair.source.enabled;
        root.transform.SetPositionAndRotation(position, rotation);
        material.SetColor("_BaseColor", valid ? new Color(0.1f, 0.65f, 1f, 0.4f) : new Color(1f, 0.15f, 0.1f, 0.4f));
    }

    public void Hide() { if (root != null) root.SetActive(false); }

    public void Clear()
    {
        if (root != null) Object.Destroy(root);
        if (material != null) Object.Destroy(material);
        root = null; material = null; source = null;
        ghostTransforms.Clear();
        ghostRenderers.Clear();
    }

    private void Build(Item item)
    {
        Clear();
        source = item;
        Material template = Resources.Load<Material>("ItemPlacementPreview");
        if (template == null) { Debug.LogError("Missing ItemPlacementPreview material in Resources."); return; }
        material = new Material(template);
        root = new GameObject("Item placement preview") { layer = 2 };
        root.transform.localScale = item.transform.lossyScale;
        ghostTransforms[item.transform] = root.transform;
        // Keep the original transform hierarchy: flattening world rotations and
        // lossy scales distorts layers beneath rotated, non-uniformly scaled parents.
        foreach (Transform original in item.GetComponentsInChildren<Transform>(true))
        {
            if (original == item.transform) continue;
            var clone = new GameObject(original.name) { layer = 2 };
            clone.transform.SetParent(ghostTransforms[original.parent], false);
            ghostTransforms[original] = clone.transform;
        }
        MeshFilter[] filters = item.GetComponentsInChildren<MeshFilter>(true);
        meshCount = filters.Length;
        foreach (MeshFilter filter in filters)
        {
            if (filter.sharedMesh == null || !filter.TryGetComponent(out MeshRenderer renderer)) continue;
            GameObject child = ghostTransforms[filter.transform].gameObject;
            child.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var ghost = child.AddComponent<MeshRenderer>();
            var materials = new Material[filter.sharedMesh.subMeshCount];
            for (int i = 0; i < materials.Length; i++) materials[i] = material;
            ghost.sharedMaterials = materials;
            ghost.shadowCastingMode = ShadowCastingMode.Off;
            ghost.receiveShadows = false;
            ghostRenderers.Add((renderer, ghost));
        }
    }
}

public static class ItemPlacement
{
    public static bool IsFinite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

    public static bool TryGetBounds(Item item, out Bounds bounds) => TryGetBounds(item, out bounds, false);

    private static bool TryGetBounds(Item item, out Bounds bounds, bool physicsOnly)
    {
        bounds = default;
        bool found = false;
        foreach (MeshFilter mesh in item.GetComponentsInChildren<MeshFilter>())
        {
            if (physicsOnly || mesh.sharedMesh == null || !mesh.TryGetComponent(out MeshRenderer renderer) || !renderer.enabled) continue;
            Bounds local = mesh.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 point = item.transform.InverseTransformPoint(mesh.transform.TransformPoint(corner));
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                else bounds.Encapsulate(point);
            }
        }
        // Held colliders are disabled, so Collider.bounds is empty. Use their
        // authored dimensions to keep the placement volume at least as big as physics.
        foreach (Collider collider in item.GetComponentsInChildren<Collider>())
        {
            if (collider.isTrigger || !item.IsColliderEnabledAfterRelease(collider)) continue;
            Bounds local;
            if (collider is BoxCollider box) local = new Bounds(box.center, box.size);
            else if (collider is SphereCollider sphere) local = new Bounds(sphere.center, Vector3.one * sphere.radius * 2f);
            else if (collider is CapsuleCollider capsule)
            {
                Vector3 size = Vector3.one * capsule.radius * 2f;
                size[capsule.direction] = Mathf.Max(capsule.height, capsule.radius * 2f);
                local = new Bounds(capsule.center, size);
            }
            else if (collider is MeshCollider mesh && mesh.sharedMesh != null) local = mesh.sharedMesh.bounds;
            else continue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 point = item.transform.InverseTransformPoint(collider.transform.TransformPoint(corner));
                if (!found) { bounds = new Bounds(point, Vector3.zero); found = true; }
                else bounds.Encapsulate(point);
            }
        }
        return found;
    }

    public static bool TryFindPose(Item item, Transform player, Ray ray, float range,
        out Vector3 position, out Quaternion rotation, out bool valid)
    {
        position = default; rotation = Quaternion.identity; valid = false;
        if (!IsFinite(ray.origin) || !IsFinite(ray.direction) || ray.direction.sqrMagnitude < 0.5f) return false;
        RaycastHit nearest = default;
        float distance = float.PositiveInfinity;
        foreach (RaycastHit hit in Physics.RaycastAll(ray, range, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(player) || hit.collider.transform.IsChildOf(item.transform)) continue;
            if (hit.distance < distance) { nearest = hit; distance = hit.distance; }
        }
        if (nearest.collider == null || !TryGetBounds(item, out Bounds bounds)) return false;
        Vector3 forward = Vector3.ProjectOnPlane(ray.direction, Vector3.up);
        rotation = forward.sqrMagnitude > 0.001f ? Quaternion.LookRotation(forward) : Quaternion.Euler(0, player.eulerAngles.y, 0);
        Vector3 scale = item.transform.lossyScale;
        Vector3 center = Vector3.Scale(bounds.center, scale);
        Vector3 extents = Vector3.Scale(bounds.extents, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        // Visual layers determine clearance, but only restored solid colliders
        // determine where the released stack will actually rest.
        Bounds support = TryGetBounds(item, out Bounds physicsBounds, true) ? physicsBounds : bounds;
        Vector3 supportCenter = Vector3.Scale(support.center, scale);
        Vector3 supportExtents = Vector3.Scale(support.extents,
            new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
        float supportBottom = support.center.y * scale.y - support.extents.y * Mathf.Abs(scale.y);
        position = nearest.point - rotation * new Vector3(supportCenter.x, 0f, supportCenter.z);
        position.y -= supportBottom;
        position.y += 0.005f;
        if (nearest.normal.y < 0.95f || nearest.collider.GetComponentInParent<PlayerPickup>() != null) return true;
        foreach (Collider overlap in Physics.OverlapBox(position + rotation * supportCenter, supportExtents, rotation,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (!overlap.transform.IsChildOf(item.transform)) return true;
        foreach (Collider overlap in Physics.OverlapBox(position + rotation * center, extents, rotation,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (overlap.transform.IsChildOf(item.transform) || overlap == nearest.collider) continue;
            return true;
        }
        valid = true;
        return true;
    }
}
