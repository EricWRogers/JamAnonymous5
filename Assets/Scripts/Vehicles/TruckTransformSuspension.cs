using Unity.Netcode.Components;

/// <summary>Disabling NetworkTransform alone does not stop NGO's network tick.</summary>
public sealed class TruckTransformSuspension
{
    private readonly NetworkTransform target;
    private readonly bool enabled, px, py, pz, rx, ry, rz, sx, sy, sz, quaternion;
    public TruckTransformSuspension(NetworkTransform value)
    {
        target = value;
        enabled = value.enabled;
        px = value.SyncPositionX; py = value.SyncPositionY; pz = value.SyncPositionZ;
        rx = value.SyncRotAngleX; ry = value.SyncRotAngleY; rz = value.SyncRotAngleZ;
        sx = value.SyncScaleX; sy = value.SyncScaleY; sz = value.SyncScaleZ;
        quaternion = value.UseQuaternionSynchronization;
        Suspend();
    }
    public void Suspend()
    {
        if (target == null) return;
        target.SyncPositionX = target.SyncPositionY = target.SyncPositionZ = false;
        target.SyncRotAngleX = target.SyncRotAngleY = target.SyncRotAngleZ = false;
        target.SyncScaleX = target.SyncScaleY = target.SyncScaleZ = false;
        target.UseQuaternionSynchronization = false;
        target.enabled = false;
    }
    public void Restore()
    {
        if (target == null) return;
        target.SyncPositionX = px; target.SyncPositionY = py; target.SyncPositionZ = pz;
        target.SyncRotAngleX = rx; target.SyncRotAngleY = ry; target.SyncRotAngleZ = rz;
        target.SyncScaleX = sx; target.SyncScaleY = sy; target.SyncScaleZ = sz;
        target.UseQuaternionSynchronization = quaternion;
        target.enabled = enabled;
        if (target.IsSpawned && (target.IsServerAuthoritative() ? target.IsServer : target.IsOwner))
            target.Teleport(target.transform.position, target.transform.rotation, target.transform.localScale);
    }
}
