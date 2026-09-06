using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A vehicle command shared by human drivers and AI drivers.
/// Values are normalized so input sources do not need to know vehicle tuning.
/// </summary>
[Serializable]
public struct VehicleInputState : INetworkSerializable, IEquatable<VehicleInputState>
{
    [Range(-1f, 1f)] public float Steering;
    [Range(-1f, 1f)] public float Throttle;
    [Range(0f, 1f)] public float Brake;
    public bool Handbrake;

    public VehicleInputState(float steering, float throttle, float brake, bool handbrake = false)
    {
        Steering = IsFinite(steering) ? Mathf.Clamp(steering, -1f, 1f) : 0f;
        Throttle = IsFinite(throttle) ? Mathf.Clamp(throttle, -1f, 1f) : 0f;
        Brake = IsFinite(brake) ? Mathf.Clamp01(brake) : 1f;
        Handbrake = handbrake;
    }

    public VehicleInputState Sanitized()
    {
        return new VehicleInputState(Steering, Throttle, Brake, Handbrake);
    }

    public bool HasFiniteValues => IsFinite(Steering) && IsFinite(Throttle) && IsFinite(Brake);

    private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Steering);
        serializer.SerializeValue(ref Throttle);
        serializer.SerializeValue(ref Brake);
        serializer.SerializeValue(ref Handbrake);
    }

    public bool Equals(VehicleInputState other)
    {
        return Steering.Equals(other.Steering) &&
               Throttle.Equals(other.Throttle) &&
               Brake.Equals(other.Brake) &&
               Handbrake == other.Handbrake;
    }

    public override bool Equals(object obj)
    {
        return obj is VehicleInputState other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Steering, Throttle, Brake, Handbrake);
    }
}

public interface IVehicleInputSource
{
    bool TryGetVehicleInput(out VehicleInputState input);
}
