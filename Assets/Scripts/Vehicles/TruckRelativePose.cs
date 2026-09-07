using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>A pose relative to a spawned support, or a world pose when Support is None.</summary>
public struct TruckRelativePose : INetworkSerializable, IEquatable<TruckRelativePose>
{
    public const ulong None = ulong.MaxValue;
    public ulong Support;
    public Vector3 Position;
    public Quaternion Rotation;
    public static TruckRelativePose Detached => new() { Support = None, Rotation = Quaternion.identity };
    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Support);
        serializer.SerializeValue(ref Position);
        serializer.SerializeValue(ref Rotation);
    }
    public bool Equals(TruckRelativePose other) => Support == other.Support && Position == other.Position && Rotation == other.Rotation;
}
