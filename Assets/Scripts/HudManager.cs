using UnityEngine;
using TMPro;

public class HUDManager : MonoBehaviour
{
    public TMP_Text clockText;
    public TMP_Text moneyText;

    void Update()
    {
            clockText.text = GameManager.Instance.GetFormattedTime();

            moneyText.text = $"${RestaurantMoney.Instance.Money}";

    }
}
