using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class OptionsMenu : MonoBehaviour
{
    private const string FieldOfViewKey = "Options.FieldOfView";
    private const string MasterVolumeKey = "Options.MasterVolume";
    private const string MouseSensitivityKey = "Options.MouseSensitivity";
    private const string WindowModeKey = "Options.WindowMode";

    [Header("Defaults")]
    [SerializeField, Range(60f, 120f)] private float defaultFieldOfView = 75f;
    [SerializeField, Range(0f, 1f)] private float defaultMasterVolume = 1f;
    [SerializeField, Range(0.01f, 1f)] private float defaultMouseSensitivity = 0.15f;
    [SerializeField] private FullScreenMode defaultWindowMode = FullScreenMode.FullScreenWindow;

    [Header("Value Labels")]
    public TMP_Text fieldOfViewValueText;
    public TMP_Text masterVolumeValueText;
    public TMP_Text mouseSensitivityValueText;

    [Header("Sliders")]
    public Slider fieldOfViewSlider;
    public Slider masterVolumeSlider;
    public Slider mouseSensitivitySlider;

    private void Awake()
    {
        ApplyGlobalSettings();
        ApplySavedValuesToSliders();
        RefreshValueLabels();
    }

    private void Update()
    {
        RefreshValueLabels();
    }

    public void SetFieldOfView(float value)
    {
        PlayerPrefs.SetFloat(FieldOfViewKey, Mathf.Clamp(value, 60f, 120f));
        PlayerPrefs.Save();
        ApplyFieldOfView();
        RefreshValueLabels();
    }

    public void SetMasterVolume(float value)
    {
        PlayerPrefs.SetFloat(MasterVolumeKey, Mathf.Clamp01(value));
        PlayerPrefs.Save();
        ApplyMasterVolume();
        RefreshValueLabels();
    }

    public void SetMouseSensitivity(float value)
    {
        PlayerPrefs.SetFloat(MouseSensitivityKey, Mathf.Clamp(value, 0.01f, 1f));
        PlayerPrefs.Save();
        ApplyMouseSensitivity();
        RefreshValueLabels();
    }

    public void SetWindowMode(int mode)
    {
        FullScreenMode windowMode = mode == 0
            ? FullScreenMode.Windowed
            : mode == 1
                ? FullScreenMode.FullScreenWindow
                : FullScreenMode.ExclusiveFullScreen;

        PlayerPrefs.SetInt(WindowModeKey, (int)windowMode);
        PlayerPrefs.Save();
        Screen.fullScreenMode = windowMode;
        Screen.fullScreen = windowMode != FullScreenMode.Windowed;
    }

    public void ToggleFullscreen()
    {
        SetWindowMode(Screen.fullScreenMode == FullScreenMode.Windowed ? 1 : 0);
    }

    public void ResetToDefaults()
    {
        SetFieldOfView(defaultFieldOfView);
        SetMasterVolume(defaultMasterVolume);
        SetMouseSensitivity(defaultMouseSensitivity);
        SetWindowMode((int)defaultWindowMode);
    }

    public float GetFieldOfView() => PlayerPrefs.GetFloat(FieldOfViewKey, defaultFieldOfView);
    public float GetMasterVolume() => PlayerPrefs.GetFloat(MasterVolumeKey, defaultMasterVolume);
    public float GetMouseSensitivity() => PlayerPrefs.GetFloat(MouseSensitivityKey, defaultMouseSensitivity);

    private void RefreshValueLabels()
    {
        if (fieldOfViewValueText != null)
        {
            float value = fieldOfViewSlider != null ? fieldOfViewSlider.value : GetFieldOfView();
            fieldOfViewValueText.text = $"{value:0}°";
        }

        if (masterVolumeValueText != null)
        {
            float value = masterVolumeSlider != null ? masterVolumeSlider.value : GetMasterVolume();
            masterVolumeValueText.text = $"{value * 100f:0}%";
        }

        if (mouseSensitivityValueText != null)
        {
            float value = mouseSensitivitySlider != null ? mouseSensitivitySlider.value : GetMouseSensitivity();
            mouseSensitivityValueText.text = $"{value:0.00}";
        }
    }

    private void ApplySavedValuesToSliders()
    {
        if (fieldOfViewSlider != null)
            fieldOfViewSlider.SetValueWithoutNotify(GetFieldOfView());

        if (masterVolumeSlider != null)
            masterVolumeSlider.SetValueWithoutNotify(GetMasterVolume());

        if (mouseSensitivitySlider != null)
            mouseSensitivitySlider.SetValueWithoutNotify(GetMouseSensitivity());
    }

    public static void ApplySavedSettingsToCamera(Camera camera, PlayerCamera playerCamera)
    {
        if (camera == null || playerCamera == null) return;

        camera.fieldOfView = PlayerPrefs.GetFloat(FieldOfViewKey, 75f);
        float sensitivity = PlayerPrefs.GetFloat(MouseSensitivityKey, 0.15f);
        playerCamera.sensitivityX = sensitivity;
        playerCamera.sensitivityY = sensitivity;
    }

    private void ApplyGlobalSettings()
    {
        ApplyMasterVolume();
        ApplyWindowMode();
        ApplyFieldOfView();
        ApplyMouseSensitivity();
    }

    private void ApplyMasterVolume()
    {
        AudioListener.volume = GetMasterVolume();
    }

    private void ApplyWindowMode()
    {
        FullScreenMode mode = (FullScreenMode)PlayerPrefs.GetInt(WindowModeKey, (int)defaultWindowMode);
        Screen.fullScreenMode = mode;
        Screen.fullScreen = mode != FullScreenMode.Windowed;
    }

    private void ApplyFieldOfView()
    {
        Camera camera = Camera.main;
        if (camera != null) camera.fieldOfView = GetFieldOfView();
    }

    private void ApplyMouseSensitivity()
    {
        PlayerCamera[] cameras = FindObjectsByType<PlayerCamera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < cameras.Length; i++)
        {
            if (!cameras[i].IsOwner) continue;
            float sensitivity = GetMouseSensitivity();
            cameras[i].sensitivityX = sensitivity;
            cameras[i].sensitivityY = sensitivity;
        }
    }
}
