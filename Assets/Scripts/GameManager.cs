using UnityEngine;
using Unity.Netcode;  
public class GameManager : NetworkBehaviour
{
    //This whole thing may need to be a networkobj.

     public static GameManager Instance;

    public string JoinCode;

    public NetworkVariable<float> shiftTimer = new();

    public float maxShiftTime = 600f; //10 minutes. The huds know what to do with this. Should be 8pm is the end of shift.

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    public void SetJoinCode(string code)
    {
        JoinCode = code;
    }


    void Update()
    {
        if(!IsServer) return;

        shiftTimer.Value = Mathf.Min(shiftTimer.Value + Time.deltaTime, maxShiftTime);
    }

    public string GetFormattedTime()
    {
        float progress = shiftTimer.Value / maxShiftTime;
        float totalInGameMinutes = progress * 14 * 60; //14 hours in min

        int hour = 6 + Mathf.FloorToInt(totalInGameMinutes / 60);
        int minutes = Mathf.FloorToInt(totalInGameMinutes % 60);

        //Snap to :00 or :30. This way we don't have a stopwatch eque situation which isnt very cool.
        minutes = minutes >= 30 ? 30 : 0;

        string period = hour >= 12 ? "PM" : "AM";
        int displayHour = hour > 12 ? hour - 12 : hour;

        return minutes == 0
            ? $"{displayHour}{period}"
            : $"{displayHour}:{minutes:D2}{period}";
    }
}
