using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Editor-side level selector. Lists existing saves in
/// <see cref="Application.persistentDataPath"/> as highlightable entries
/// (click to select, then "Start Level" launches the editor with the
/// highlighted name), and provides a separate input field + "Create New
/// Level" button for fresh saves.
/// </summary>
public class EditorLevelSelectController : MonoBehaviour
{
    [Header("Scenes")]
#if UNITY_EDITOR
    [SerializeField] UnityEditor.SceneAsset editorScene;
    [SerializeField] UnityEditor.SceneAsset mainMenuScene;
#endif
    [SerializeField, HideInInspector] string editorSceneName = "MainSceneRD";
    [SerializeField, HideInInspector] string mainMenuSceneName = "UiScene";

#if UNITY_EDITOR
    void OnValidate()
    {
        if (editorScene != null) editorSceneName = editorScene.name;
        if (mainMenuScene != null) mainMenuSceneName = mainMenuScene.name;
    }
#endif

    [Header("Existing Levels")]
    [Tooltip("Drop the ScrollRect root, the Viewport, or the Content here — the script descends to the first inner LayoutGroup and uses that as the entry container. The ToggleGroup must live on that same GameObject (typically the Content).")]
    [SerializeField] Transform listParent;
    [SerializeField] LevelListEntry entryPrefab;
    [Tooltip("Optional. Shown when no saved levels exist on disk yet.")]
    [SerializeField] GameObject emptyState;
    [Tooltip("Launches the editor with the highlighted level. Disabled until the player picks one.")]
    [SerializeField] Button startLevelButton;

    [Header("Create New Level")]
    [SerializeField] TMP_InputField newLevelNameInput;
    [SerializeField] Button createNewLevelButton;
    [Tooltip("Optional. Used for validation errors (empty name, invalid chars, collision).")]
    [SerializeField] TMP_Text errorText;

    [Header("Navigation")]
    [SerializeField] Button backButton;

    string _selectedName;
    Transform _contentRoot;
    ToggleGroup _toggleGroup;

    void Start()
    {
        if (errorText != null) errorText.text = "";

        if (createNewLevelButton != null)
            createNewLevelButton.onClick.AddListener(OnCreateNewClicked);
        if (startLevelButton != null)
            startLevelButton.onClick.AddListener(OnStartLevelClicked);
        if (backButton != null)
            backButton.onClick.AddListener(() => SceneManager.LoadScene(mainMenuSceneName));

        _contentRoot = ResolveContentRoot(listParent);
        _toggleGroup = _contentRoot != null ? _contentRoot.GetComponent<ToggleGroup>() : null;
        if (_toggleGroup == null)
            Debug.LogWarning("EditorLevelSelectController: no ToggleGroup found on the resolved content root — entries won't be mutually exclusive.");

        PopulateList();
    }

    void PopulateList()
    {
        _selectedName = null;
        if (startLevelButton != null) startLevelButton.interactable = false;

        if (_contentRoot == null || entryPrefab == null)
        {
            Debug.LogWarning("EditorLevelSelectController: listParent or entryPrefab not assigned.");
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
        SceneManager.LoadScene(editorSceneName);
    }

    void OnCreateNewClicked()
    {
        string raw = newLevelNameInput != null ? newLevelNameInput.text : "";
        string name = SanitizeName(raw);
        if (name == null)
        {
            SetError("Enter a non-empty name without path characters.");
            return;
        }
        if (File.Exists(Path.Combine(Application.persistentDataPath, name)))
        {
            SetError($"'{Path.GetFileNameWithoutExtension(name)}' already exists. Open it from the list or pick a different name.");
            return;
        }
        EnsureSelection().Select(name);
        SceneManager.LoadScene(editorSceneName);
    }

    void SetError(string msg)
    {
        if (errorText != null) errorText.text = msg;
        else Debug.LogWarning($"EditorLevelSelectController: {msg}");
    }

    // Spaces collapse to underscores so that "my level" and "my_level" can't
    // sneak past the collision check as different names on disk.
    static string SanitizeName(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        string trimmed = input.Trim().Replace(' ', '_');
        foreach (char c in Path.GetInvalidFileNameChars())
            if (trimmed.IndexOf(c) >= 0) return null;
        if (!trimmed.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            trimmed += ".json";
        return trimmed;
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

    // Lets this scene be Play-tested directly in Unity without first going
    // through the main menu (where the persistent LevelSelection is normally
    // seeded).
    static LevelSelection EnsureSelection()
    {
        if (LevelSelection.Instance != null) return LevelSelection.Instance;
        GameObject go = new GameObject(nameof(LevelSelection));
        return go.AddComponent<LevelSelection>();
    }
}
