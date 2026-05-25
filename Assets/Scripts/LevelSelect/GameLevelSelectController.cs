using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Game-side level selector. Lists existing saves as highlightable entries
/// (click to select, "Start Level" launches). No create form because the
/// game scene can't generate fresh terrain via <see cref="GameTerrainBuilder"/>.
/// </summary>
public class GameLevelSelectController : MonoBehaviour
{
    [Header("Scenes")]
#if UNITY_EDITOR
    [SerializeField] UnityEditor.SceneAsset gameScene;
    [SerializeField] UnityEditor.SceneAsset mainMenuScene;
#endif
    [SerializeField, HideInInspector] string gameSceneName = "SampleScene";
    [SerializeField, HideInInspector] string mainMenuSceneName = "UiScene";

#if UNITY_EDITOR
    void OnValidate()
    {
        if (gameScene != null) gameSceneName = gameScene.name;
        if (mainMenuScene != null) mainMenuSceneName = mainMenuScene.name;
    }
#endif

    [Header("Existing Levels")]
    [Tooltip("Drop the ScrollRect root, the Viewport, or the Content here — the script descends to the first inner LayoutGroup and uses that as the entry container. The ToggleGroup must live on that same GameObject (typically the Content).")]
    [SerializeField] Transform listParent;
    [SerializeField] LevelListEntry entryPrefab;
    [Tooltip("Optional. Shown when no saved levels exist on disk yet.")]
    [SerializeField] GameObject emptyState;
    [Tooltip("Launches the game with the highlighted level. Disabled until the player picks one.")]
    [SerializeField] Button startLevelButton;

    [Header("Navigation")]
    [SerializeField] Button backButton;

    string _selectedName;
    Transform _contentRoot;
    ToggleGroup _toggleGroup;

    void Start()
    {
        if (startLevelButton != null)
            startLevelButton.onClick.AddListener(OnStartLevelClicked);
        if (backButton != null)
            backButton.onClick.AddListener(() => SceneManager.LoadScene(mainMenuSceneName));

        _contentRoot = ResolveContentRoot(listParent);
        _toggleGroup = _contentRoot != null ? _contentRoot.GetComponent<ToggleGroup>() : null;
        if (_toggleGroup == null)
            Debug.LogWarning("GameLevelSelectController: no ToggleGroup found on the resolved content root — entries won't be mutually exclusive.");

        PopulateList();
    }

    void PopulateList()
    {
        _selectedName = null;
        if (startLevelButton != null) startLevelButton.interactable = false;

        if (_contentRoot == null || entryPrefab == null)
        {
            Debug.LogWarning("GameLevelSelectController: listParent or entryPrefab not assigned.");
            return;
        }

        for (int i = _contentRoot.childCount - 1; i >= 0; i--)
            Destroy(_contentRoot.GetChild(i).gameObject);

        string[] files = Directory.GetFiles(Application.persistentDataPath, "*.json");
        if (emptyState != null) emptyState.SetActive(files.Length == 0);

        foreach (string path in files)
        {
            string fileName = Path.GetFileName(path);
            string displayName = Path.GetFileNameWithoutExtension(path);
            LevelListEntry entry = Instantiate(entryPrefab, _contentRoot);
            entry.Bind(displayName, fileName, OnEntrySelected, _toggleGroup);
        }
    }

    void OnEntrySelected(string fileName)
    {
        _selectedName = fileName;
        if (startLevelButton != null) startLevelButton.interactable = true;
    }

    void OnStartLevelClicked()
    {
        if (string.IsNullOrEmpty(_selectedName)) return;
        EnsureSelection().Select(_selectedName);
        SceneManager.LoadScene(gameSceneName);
    }

    // listParent may be the Content GameObject or any of its ancestors (the
    // ScrollView root, the Viewport, etc.). Descend to the first inner
    // LayoutGroup, since that's where the rows (and the ToggleGroup) belong.
    static Transform ResolveContentRoot(Transform assigned)
    {
        if (assigned == null) return null;
        if (assigned.GetComponent<LayoutGroup>() != null) return assigned;
        LayoutGroup found = assigned.GetComponentInChildren<LayoutGroup>(includeInactive: true);
        return found != null ? found.transform : assigned;
    }

    static LevelSelection EnsureSelection()
    {
        if (LevelSelection.Instance != null) return LevelSelection.Instance;
        GameObject go = new GameObject(nameof(LevelSelection));
        return go.AddComponent<LevelSelection>();
    }
}
