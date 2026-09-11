using UnityEngine;
using TMPro;

public class MainMenu : MonoBehaviour
{
    public GameObject mainMenu;
    public GameObject optionsMenu;

    public GameObject creditsMenu;
    public TMP_InputField playerNameInput;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (playerNameInput == null) return;
        playerNameInput.characterLimit = PlayerIdentity.MaxNameLength;
        playerNameInput.SetTextWithoutNotify(PlayerIdentity.SavedName);
        playerNameInput.onValueChanged.AddListener(SavePlayerName);
        playerNameInput.onEndEdit.AddListener(FinishPlayerName);
    }

    private void SavePlayerName(string value)
    {
        PlayerIdentity.SaveName(value);
    }

    private void FinishPlayerName(string value)
    {
        SavePlayerName(value);
        playerNameInput.SetTextWithoutNotify(PlayerIdentity.SavedName);
    }

    private void OnDestroy()
    {
        if (playerNameInput == null) return;
        playerNameInput.onValueChanged.RemoveListener(SavePlayerName);
        playerNameInput.onEndEdit.RemoveListener(FinishPlayerName);
    }

    public void OptionsMenuToggle()
    {
        bool showOptions = optionsMenu != null && !optionsMenu.activeSelf;

        if (mainMenu != null) mainMenu.SetActive(!showOptions);
        if (optionsMenu != null) optionsMenu.SetActive(showOptions);
    }
    public void CreditsMenuToggle()
    {
        bool showCredits = creditsMenu != null && !creditsMenu.activeSelf;

        if (mainMenu != null) mainMenu.SetActive(!showCredits);
        if (creditsMenu != null) creditsMenu.SetActive(showCredits);
    }
}
