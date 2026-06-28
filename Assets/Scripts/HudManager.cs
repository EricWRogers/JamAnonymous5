using UnityEngine;
using TMPro;
using Unity.Netcode;
using UnityEngine.UI;

public class HUDManager : MonoBehaviour
{
    public TMP_Text clockText;
    public TMP_Text moneyText;

    private const int UiLayer = 5;

    private NetworkObject ownerNetworkObject;
    private Canvas hudCanvas;
    private GameObject menuPanelObject;
    private bool ownerResolved;
    private bool isLocalHud;

    void Awake()
    {
        hudCanvas = GetComponentInChildren<Canvas>(true);
    }

    void OnEnable()
    {
        NetworkSessionMenu.GameMenuOpenChanged += OnGameMenuOpenChanged;
    }

    void OnDisable()
    {
        NetworkSessionMenu.GameMenuOpenChanged -= OnGameMenuOpenChanged;
    }

    void Start()
    {
        TryInitializeForOwner();
    }

    void Update()
    {
        if (!TryInitializeForOwner()) return;

        if (clockText != null && GameManager.Instance != null)
        {
            clockText.text = GameManager.Instance.GetFormattedTime();
        }

        if (moneyText != null && RestaurantMoney.Instance != null)
        {
            moneyText.text = $"${RestaurantMoney.Instance.Money}";
        }
    }

    private bool TryInitializeForOwner()
    {
        if (ownerResolved)
        {
            return isLocalHud;
        }

        if (ownerNetworkObject == null)
        {
            ownerNetworkObject = GetComponentInParent<NetworkObject>();
        }

        if (ownerNetworkObject != null && !ownerNetworkObject.IsSpawned)
        {
            return false;
        }

        isLocalHud = ownerNetworkObject == null || ownerNetworkObject.IsOwner;
        ownerResolved = true;

        if (hudCanvas != null)
        {
            hudCanvas.enabled = isLocalHud;
        }

        if (!isLocalHud) return false;

        BuildMenu();
        OnGameMenuOpenChanged(NetworkSessionMenu.IsGameMenuOpen);
        return true;
    }

    private void BuildMenu()
    {
        if (hudCanvas == null || menuPanelObject != null) return;

        Transform canvasTransform = hudCanvas.transform;

        menuPanelObject = new GameObject("HudSessionMenu", typeof(RectTransform), typeof(Image));
        menuPanelObject.layer = UiLayer;
        menuPanelObject.transform.SetParent(canvasTransform, false);

        RectTransform panelRect = menuPanelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panelImage = menuPanelObject.GetComponent<Image>();
        panelImage.color = new Color32(0, 0, 0, 160);

        GameObject modal = new GameObject("HudSessionMenuPanel", typeof(RectTransform), typeof(Image));
        modal.layer = UiLayer;
        modal.transform.SetParent(menuPanelObject.transform, false);

        RectTransform modalRect = modal.GetComponent<RectTransform>();
        modalRect.anchorMin = new Vector2(0.5f, 0.5f);
        modalRect.anchorMax = new Vector2(0.5f, 0.5f);
        modalRect.pivot = new Vector2(0.5f, 0.5f);
        modalRect.anchoredPosition = Vector2.zero;
        modalRect.sizeDelta = new Vector2(360f, 280f);

        Image modalImage = modal.GetComponent<Image>();
        modalImage.color = new Color32(28, 32, 36, 245);

        CreateLabel(
            modal.transform,
            "MenuTitle",
            "Menu",
            new Vector2(0f, 1f),
            new Vector2(1f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -34f),
            new Vector2(0f, 64f),
            32f,
            Color.white);

        Button resumeButton = CreateButton(
            modal.transform,
            "ResumeButton",
            "Resume",
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -118f),
            new Vector2(230f, 56f),
            new Color32(54, 120, 88, 245));
        resumeButton.onClick.AddListener(() => NetworkSessionMenu.SetGameMenuOpen(false));

        Button leaveButton = CreateButton(
            modal.transform,
            "LeaveButton",
            "Leave",
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0.5f, 1f),
            new Vector2(0f, -190f),
            new Vector2(230f, 56f),
            new Color32(176, 52, 52, 245));
        leaveButton.onClick.AddListener(NetworkSessionMenu.ReturnToMainMenu);

        menuPanelObject.SetActive(false);
    }

    private void OnGameMenuOpenChanged(bool open)
    {
        if (!isLocalHud) return;

        if (menuPanelObject == null)
        {
            BuildMenu();
        }

        if (menuPanelObject != null)
        {
            menuPanelObject.SetActive(open);
        }
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
}
