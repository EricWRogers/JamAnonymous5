using UnityEngine;
using TMPro;

public class PlayerLobbyUI : MonoBehaviour
{
    //Add more later. Ping, status, whatever.
    public TMP_Text label;

    public void SetName(string name, Color32 color)
    {
        label.text = name;
        label.color = color;
    }
}
