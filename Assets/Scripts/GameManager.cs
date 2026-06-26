using UnityEngine;
using Unity.Netcode;  
using UnityEngine.SceneManagement;
using System.Collections.Generic;
public class GameManager : NetworkBehaviour
{
    //This whole thing may need to be a networkobj.

     public static GameManager Instance;

    public string JoinCode;

    public NetworkVariable<float> shiftTimer = new();

    public float maxShiftTime = 600f; //10 minutes. The huds know what to do with this. Should be 8pm is the end of shift.

    private readonly Color32[] palette = new Color32[]
    {
        new(220, 80,  80,  255), //red
        new(80,  140, 220, 255), //blue
        new(80,  200, 120, 255), //green
        new(220, 180, 60,  255), //yellow
    };

    private readonly Dictionary<ulong, Color32> playerColors = new();

    ulong[] GetIds() => new List<ulong>(playerColors.Keys).ToArray();
    Color32[] GetColors() => new List<Color32>(playerColors.Values).ToArray();

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

        if(SceneManager.GetActiveScene().name == "Game")
        {
            shiftTimer.Value = Mathf.Min(shiftTimer.Value + Time.deltaTime, maxShiftTime);
        }
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

    public void AssignColor(ulong clientId)
    {
        if (playerColors.ContainsKey(clientId)) return;
        playerColors[clientId] = palette[playerColors.Count % palette.Length];
        SyncColorsClientRpc(GetIds(), GetColors());
    }

    public Color32 GetColor(ulong clientId)
    {
        return playerColors.TryGetValue(clientId, out var color) ? color : Color.magenta;
    }

    [ClientRpc]
    void SyncColorsClientRpc(ulong[] ids, Color32[] colors)
    {
        playerColors.Clear();
        for (int i = 0; i < ids.Length; i++)
        {
            playerColors[ids[i]] = colors[i];
        }
    }
            
}
