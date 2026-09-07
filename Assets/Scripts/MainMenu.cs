using UnityEngine;

public class MainMenu : MonoBehaviour
{
    public GameObject mainMenu;
    public GameObject optionsMenu;

    public GameObject creditsMenu;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
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
