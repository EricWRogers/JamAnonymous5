using UnityEngine;
using TMPro;

public class HUDManager : MonoBehaviour
{
    public TMP_Text clockText;

    void Update()
    {
        clockText.text = GameManager.Instance.GetFormattedTime();
    }
}