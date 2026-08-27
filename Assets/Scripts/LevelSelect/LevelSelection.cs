using UnityEngine;

/// <summary>
/// App-lifetime singleton that carries the player's chosen level file name
/// between scenes. A GameObject in the main menu (UiScene) seeds it; it
/// survives scene loads via DontDestroyOnLoad and is consumed by
/// TerrainDataStore on Awake. Null means no level is selected, the selector
/// scenes are responsible for preventing the editor or game from being loaded
/// in that state.
/// </summary>
public class LevelSelection : MonoBehaviour
{
    public static LevelSelection Instance { get; private set; }

    /// <summary>File name including extension (e.g. "mylevel.json"), or null when nothing is selected.</summary>
    public string SelectedLevelFileName { get; private set; }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void Select(string fileName) => SelectedLevelFileName = fileName;
    public void Clear() => SelectedLevelFileName = null;
}
