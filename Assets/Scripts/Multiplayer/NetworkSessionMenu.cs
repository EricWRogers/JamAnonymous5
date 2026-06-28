using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class NetworkSessionMenu : MonoBehaviour
{
    private const string MainMenuSceneName = "MainMenu";
    private const string LobbySceneName = "Lobby";
    private const string GameSceneName = "Game";
    private const int UiLayer = 5;

    private static NetworkSessionMenu instance;

    private NetworkManager registeredNetworkManager;
    private GameObject sessionUiRoot;
    private GameObject lobbyLeaveButtonObject;
    private bool gameMenuOpen;
    private bool returningToMenu;

    public static bool IsGameMenuOpen => instance != null && instance.gameMenuOpen;
    public static event System.Action<bool> GameMenuOpenChanged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Initialize()
    {
        EnsureInstance();
    }

    public static void ReturnToMainMenu()
    {
        EnsureInstance();
        instance.BeginReturnToMainMenu();
    }

    public static void SetGameMenuOpen(bool open)
    {
        EnsureInstance();
        instance.SetGameMenuOpenInternal(open);
    }

    public static void ToggleGameMenu()
    {
        EnsureInstance();
        instance.SetGameMenuOpenInternal(!instance.gameMenuOpen);
    }

    private static void EnsureInstance()
    {
        if (instance != null) return;

        GameObject menuObject = new GameObject(nameof(NetworkSessionMenu));
        menuObject.AddComponent<NetworkSessionMenu>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
        RegisterNetworkManager();
        BuildSceneUi(SceneManager.GetActiveScene().name);
    }

    private void OnDestroy()
    {
        if (instance != this) return;

        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnregisterNetworkManager();
        instance = null;
    }

    private void Update()
    {
        RegisterNetworkManager();

        if (gameMenuOpen)
        {
            ShowCursor();
        }

        if (returningToMenu) return;
        if (SceneManager.GetActiveScene().name != GameSceneName) return;
        if (Keyboard.current == null) return;
        if (!Keyboard.current.escapeKey.wasPressedThisFrame) return;

        ToggleGameMenu();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode loadSceneMode)
    {
        RegisterNetworkManager();
        BuildSceneUi(scene.name);
    }

    private void RegisterNetworkManager()
    {
        NetworkManager current = NetworkManager.Singleton;
        if (registeredNetworkManager == current) return;

        UnregisterNetworkManager();

        registeredNetworkManager = current;
        if (registeredNetworkManager == null) return;

        registeredNetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void UnregisterNetworkManager()
    {
        if (registeredNetworkManager != null)
        {
            registeredNetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        }

        registeredNetworkManager = null;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (returningToMenu) return;
        if (registeredNetworkManager == null) return;
        if (registeredNetworkManager.IsServer) return;
        if (clientId != registeredNetworkManager.LocalClientId) return;
        if (!IsSessionScene(SceneManager.GetActiveScene().name)) return;

        BeginReturnToMainMenu();
    }

    private void BuildSceneUi(string sceneName)
    {
        DestroySessionUi();
        gameMenuOpen = false;

        if (sceneName == MainMenuSceneName)
        {
            returningToMenu = false;
        }

        if (sceneName == LobbySceneName)
        {
            ShowCursor();
            EnsureEventSystem();
            CreateLobbyLeaveButton();
            return;
        }

        if (sceneName == GameSceneName)
        {
            LockCursorForGame();
            EnsureEventSystem();
            SetGameMenuOpenInternal(false);
            return;
        }

        ShowCursor();
    }

    private static bool IsSessionScene(string sceneName)
    {
        return sceneName == LobbySceneName || sceneName == GameSceneName;
    }

    private void CreateLobbyLeaveButton()
    {
        RectTransform startButtonRect = FindStartButtonRect();

        if (startButtonRect == null)
        {
            CreateRootCanvas();
            Button fallbackButton = CreateButton(
                sessionUiRoot.transform,
                "LeaveLobbyButton",
                "Leave",
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0.5f),
                new Vector2(176f, 70f),
                new Vector2(160f, 30f),
                new Color32(176, 52, 52, 235));
            fallbackButton.onClick.AddListener(BeginReturnToMainMenu);
            lobbyLeaveButtonObject = fallbackButton.gameObject;
            return;
        }

        Vector2 startSize = startButtonRect.sizeDelta;
        Vector2 leavePosition = startButtonRect.anchoredPosition + new Vector2(startSize.x + 16f, 0f);

        Button leaveButton = CreateButton(
            startButtonRect.parent,
            "LeaveLobbyButton",
            "Leave",
            startButtonRect.anchorMin,
            startButtonRect.anchorMax,
            startButtonRect.pivot,
            leavePosition,
            startSize,
            new Color32(176, 52, 52, 235));
        leaveButton.onClick.AddListener(BeginReturnToMainMenu);
        lobbyLeaveButtonObject = leaveButton.gameObject;
    }

    private static RectTransform FindStartButtonRect()
    {
        GameObject startButton = GameObject.Find("Start Button");
        if (startButton != null && startButton.TryGetComponent(out RectTransform startRect))
        {
            return startRect;
        }

        Button[] buttons = FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Button button in buttons)
        {
            TMP_Text label = button.GetComponentInChildren<TMP_Text>();
            if (label != null && label.text == "Start")
            {
                return button.GetComponent<RectTransform>();
            }
        }

        return null;
    }

    private void CreateRootCanvas()
    {
        sessionUiRoot = new GameObject("Session Menu Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        sessionUiRoot.layer = UiLayer;

        Canvas canvas = sessionUiRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;

        CanvasScaler scaler = sessionUiRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
    }

    private static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;

        GameObject eventSystemObject = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        eventSystemObject.layer = UiLayer;
    }

    private static Button CreateButton(
        Transform parent,
        string objectName,
        string label,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 size,
        Color imageColor)
    {
        GameObject buttonObject = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
        buttonObject.layer = UiLayer;
        buttonObject.transform.SetParent(parent, false);

        RectTransform rect = buttonObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Image image = buttonObject.GetComponent<Image>();
        image.color = imageColor;

        Button button = buttonObject.GetComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color32(238, 238, 238, 255);
        colors.pressedColor = new Color32(202, 202, 202, 255);
        colors.selectedColor = new Color32(230, 230, 230, 255);
        button.colors = colors;

        CreateLabel(
            buttonObject.transform,
            "Label",
            label,
            Vector2.zero,
            Vector2.one,
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            Vector2.zero,
            22f,
            Color.white);

        return button;
    }

    private static TMP_Text CreateLabel(
        Transform parent,
        string objectName,
        string text,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 pivot,
        Vector2 anchoredPosition,
        Vector2 size,
        float fontSize,
        Color color)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.layer = UiLayer;
        textObject.transform.SetParent(parent, false);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        TMP_Text label = textObject.GetComponent<TMP_Text>();
        label.text = text;
        label.alignment = TextAlignmentOptions.Center;
        label.color = color;
        label.fontSize = fontSize;
        label.enableAutoSizing = true;
        label.fontSizeMin = 14f;
        label.fontSizeMax = fontSize;
        label.raycastTarget = false;

        return label;
    }

    private void SetGameMenuOpenInternal(bool open)
    {
        if (SceneManager.GetActiveScene().name != GameSceneName)
        {
            open = false;
        }

        if (gameMenuOpen == open) return;

        gameMenuOpen = open;
        GameMenuOpenChanged?.Invoke(gameMenuOpen);

        if (open)
        {
            ShowCursor();
        }
        else if (SceneManager.GetActiveScene().name == GameSceneName && !returningToMenu)
        {
            LockCursorForGame();
        }
    }

    private void BeginReturnToMainMenu()
    {
        if (returningToMenu) return;

        StartCoroutine(ReturnToMainMenuRoutine());
    }

    private IEnumerator ReturnToMainMenuRoutine()
    {
        returningToMenu = true;
        SetGameMenuOpenInternal(false);
        ShowCursor();
        DestroySessionUi();

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null)
        {
            bool wasServer = networkManager.IsServer;
            UnregisterNetworkManager();

            if (networkManager.IsListening)
            {
                networkManager.Shutdown();
                yield return wasServer ? new WaitForSecondsRealtime(0.1f) : null;
            }

            if (networkManager != null)
            {
                Destroy(networkManager.gameObject);
                yield return null;
            }
        }

        if (GameManager.Instance != null)
        {
            Destroy(GameManager.Instance.gameObject);
        }

        SceneManager.LoadScene(MainMenuSceneName, LoadSceneMode.Single);
        returningToMenu = false;
    }

    private void DestroySessionUi()
    {
        if (lobbyLeaveButtonObject != null)
        {
            Destroy(lobbyLeaveButtonObject);
            lobbyLeaveButtonObject = null;
        }

        if (sessionUiRoot == null) return;

        Destroy(sessionUiRoot);
        sessionUiRoot = null;
    }

    private static void ShowCursor()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private static void LockCursorForGame()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }
}
