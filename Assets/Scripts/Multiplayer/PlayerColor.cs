using Unity.Netcode;
using UnityEngine;

public class PlayerColor : NetworkBehaviour
{
    public override void OnNetworkSpawn()
    {
        var color = GameManager.Instance.GetColor(OwnerClientId);
        GetComponent<Renderer>().material.color = color;
    }
}