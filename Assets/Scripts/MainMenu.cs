using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenu : MonoBehaviour
{
    [Header("Scenes")]
#if UNITY_EDITOR
    [SerializeField] private UnityEditor.SceneAsset levelEditorScene;
    [SerializeField] private UnityEditor.SceneAsset gameScene;
#endif
    [SerializeField, HideInInspector] private string levelEditorSceneName = "MainSceneRD";
    [SerializeField, HideInInspector] private string gameSceneName = "SampleScene";

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (levelEditorScene != null) levelEditorSceneName = levelEditorScene.name;
        if (gameScene != null) gameSceneName = gameScene.name;
    }
#endif

    [Header("Main Buttons")]
    [SerializeField] private Button levelEditorButton;
    [SerializeField] private Button gameButton;
    [SerializeField] private Button notReadyButton1;
    [SerializeField] private Button notReadyButton2;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button creditsButton;
    [SerializeField] private Button exitButton;

    [Header("Popups")]
    [SerializeField] private GameObject notReadyPanel;
    [SerializeField] private GameObject optionsPanel;
    [SerializeField] private GameObject creditsPanel;
    [SerializeField] private Button popupCloseButton;

    private void Awake()
    {
        // Hide all popups and the shared close button on start
        if (notReadyPanel != null) notReadyPanel.SetActive(false);
        if (optionsPanel != null) optionsPanel.SetActive(false);
        if (creditsPanel != null) creditsPanel.SetActive(false);
        if (popupCloseButton != null) popupCloseButton.gameObject.SetActive(false);

        // Wire up main buttons
        if (levelEditorButton != null) levelEditorButton.onClick.AddListener(LoadLevelEditor);
        if (gameButton != null) gameButton.onClick.AddListener(LoadGame);
        if (notReadyButton1 != null) notReadyButton1.onClick.AddListener(ShowNotReadyPopup);
        if (notReadyButton2 != null) notReadyButton2.onClick.AddListener(ShowNotReadyPopup);
        if (optionsButton != null) optionsButton.onClick.AddListener(ShowOptions);
        if (creditsButton != null) creditsButton.onClick.AddListener(ShowCredits);
        if (exitButton != null) exitButton.onClick.AddListener(QuitGame);

        // Shared close button closes whichever popup is open
        if (popupCloseButton != null) popupCloseButton.onClick.AddListener(CloseAllPopups);
    }

    public void LoadLevelEditor()
    {
        SceneManager.LoadScene(levelEditorSceneName);
    }

    public void LoadGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    public void ShowNotReadyPopup()
    {
        if (notReadyPanel == null)
        {
            Debug.LogWarning("MainMenu: notReadyPanel is not assigned.");
            return;
        }

        notReadyPanel.SetActive(true);
        ShowCloseButton();
    }

    public void ShowOptions()
    {
        if (optionsPanel == null)
        {
            Debug.LogWarning("MainMenu: optionsPanel is not assigned.");
            return;
        }
        optionsPanel.SetActive(true);
        ShowCloseButton();
    }

    public void ShowCredits()
    {
        if (creditsPanel == null)
        {
            Debug.LogWarning("MainMenu: creditsPanel is not assigned.");
            return;
        }
        creditsPanel.SetActive(true);
        ShowCloseButton();
    }

    public void CloseAllPopups()
    {
        if (notReadyPanel != null) notReadyPanel.SetActive(false);
        if (optionsPanel != null) optionsPanel.SetActive(false);
        if (creditsPanel != null) creditsPanel.SetActive(false);
        if (popupCloseButton != null) popupCloseButton.gameObject.SetActive(false);
    }

    private void ShowCloseButton()
    {
        if (popupCloseButton != null) popupCloseButton.gameObject.SetActive(true);
    }

    public void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
