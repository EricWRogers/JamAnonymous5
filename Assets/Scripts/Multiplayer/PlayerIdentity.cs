using System;
using System.Text;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public static class PlayerIdentity
{
    public const int MaxNameLength = 20;
    private const string NamePreferenceKey = "Player.DisplayName";
    public static string SavedName => Normalize(PlayerPrefs.GetString(NamePreferenceKey, string.Empty));

    public static void SaveName(string value)
    {
        PlayerPrefs.SetString(NamePreferenceKey, Normalize(value));
        PlayerPrefs.Save();
    }

    public static string Normalize(string value)
    {
        var result = new StringBuilder(MaxNameLength);
        if (value == null) return string.Empty;
        for (int i = 0; i < value.Length && result.Length < MaxNameLength; i++)
        {
            char c = value[i];
            if (char.IsControl(c) || c == '<' || c == '>') continue;
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]) && result.Length + 2 <= MaxNameLength)
                {
                    result.Append(c);
                    result.Append(value[++i]);
                }
            }
            else if (!char.IsLowSurrogate(c)) result.Append(c);
        }
        return result.ToString().Trim();
    }
}

public struct PlayerNameEntry : INetworkSerializable, IEquatable<PlayerNameEntry>
{
    public ulong ClientId;
    public FixedString64Bytes Name;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Name);
    }

    public bool Equals(PlayerNameEntry other) => ClientId == other.ClientId && Name.Equals(other.Name);
}
