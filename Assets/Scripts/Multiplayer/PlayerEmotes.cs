using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class PlayerEmotes : NetworkBehaviour
{
    private Animator animator;

    void Awake()
    {
        animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (!IsOwner) return;

        if (Keyboard.current.digit1Key.wasPressedThisFrame) PlayEmoteServerRpc(0);
        else if (Keyboard.current.digit2Key.wasPressedThisFrame) PlayEmoteServerRpc(1);
        else if (Keyboard.current.digit3Key.wasPressedThisFrame) PlayEmoteServerRpc(2);
        else if (Keyboard.current.digit4Key.wasPressedThisFrame) PlayEmoteServerRpc(3);
    }

    [ServerRpc]
    void PlayEmoteServerRpc(int emoteIndex)
    {
        PlayEmoteClientRpc(emoteIndex);
    }

    [ClientRpc]
    void PlayEmoteClientRpc(int emoteIndex)
    {
        string trigger = $"Emote{emoteIndex}";
        animator.SetTrigger(trigger);
    }
}