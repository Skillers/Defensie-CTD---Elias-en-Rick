using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Game-scene entry point: listens for the TerrainDataStore's auto-load, then
/// rebuilds the visual terrain asynchronously from the loaded grid. Emits
/// progress + completion events so a loading UI can follow along.
///
/// This builder assumes the save was created by the level editor. If no save
/// exists it fires OnBuildFailed — the game scene cannot generate fresh terrain.
/// </summary>
public class GameTerrainBuilder : MonoBehaviour
{
    [Header("Data")]
    public TerrainDataStore terrainDataStore;

    [Header("Pipeline Steps")]
    public PerlinNoisePlane     noisePlane;
    public SlopeMap             slopeMap;
    public MarchingCubesTerrain marchingCubes;

    [Header("Main Menu Scene (fallback when no save exists)")]
#if UNITY_EDITOR
    [SerializeField] private UnityEditor.SceneAsset mainMenuScene;
#endif
    [SerializeField, HideInInspector] private string mainMenuSceneName = "UiScene";

#if UNITY_EDITOR
    void OnValidate()
    {
        if (mainMenuScene != null) mainMenuSceneName = mainMenuScene.name;
    }
#endif

    // ── Events ───────────────────────────────────────────────────────────
    /// <summary>Fired once the build coroutine starts.</summary>
    public event Action OnBuildStarted;

    /// <summary>Fired with (progress 0..1, status message) during the build.</summary>
    public event Action<float, string> OnBuildProgress;

    /// <summary>Fired once the terrain is fully visual and playable.</summary>
    public event Action OnBuildComplete;

    /// <summary>Fired when the build cannot proceed (no save, failed load, misconfigured).</summary>
    public event Action<string> OnBuildFailed;

    // Phase weights (must sum to 1.0)
    const float WEIGHT_VISUAL = 0.10f;
    const float WEIGHT_SLOPE  = 0.10f;
    const float WEIGHT_MC     = 0.80f;

    bool _building;

    void Awake()
    {
        if (terrainDataStore == null)
        {
            Debug.LogError("GameTerrainBuilder: no TerrainDataStore assigned.");
            return;
        }

        // Subscribe in Awake so we're registered before TerrainDataStore's Start
        // auto-load fires.
        terrainDataStore.OnSaveLoaded  += HandleSaveLoaded;
        terrainDataStore.OnSaveCreated += HandleSaveCreated;
        terrainDataStore.OnSaveFailed  += HandleSaveFailed;
    }

    void OnDestroy()
    {
        if (terrainDataStore == null) return;
        terrainDataStore.OnSaveLoaded  -= HandleSaveLoaded;
        terrainDataStore.OnSaveCreated -= HandleSaveCreated;
        terrainDataStore.OnSaveFailed  -= HandleSaveFailed;
    }

    // ── Event handlers ───────────────────────────────────────────────────

    void HandleSaveLoaded()
    {
        Debug.Log($"GameTerrainBuilder: save loaded from {terrainDataStore.SaveFilePath}. Starting async build.");
        if (!_building) StartCoroutine(BuildRoutine());
    }

    void HandleSaveCreated()
    {
        string msg = $"No save file at {terrainDataStore.SaveFilePath}. Returning to main menu.";
        Debug.LogError($"GameTerrainBuilder: {msg}");
        OnBuildFailed?.Invoke(msg);
        ReturnToMainMenu();
    }

    void HandleSaveFailed(string reason)
    {
        Debug.LogError($"GameTerrainBuilder: save load failed — {reason}. Returning to main menu.");
        OnBuildFailed?.Invoke(reason);
        ReturnToMainMenu();
    }

    void ReturnToMainMenu()
    {
        if (string.IsNullOrEmpty(mainMenuSceneName))
        {
            Debug.LogError("GameTerrainBuilder: mainMenuSceneName is empty — cannot redirect.");
            return;
        }
        SceneManager.LoadScene(mainMenuSceneName);
    }

    // ── Async build ──────────────────────────────────────────────────────

    IEnumerator BuildRoutine()
    {
        _building = true;
        OnBuildStarted?.Invoke();
        ReportProgress(0f, "Starting build...");

        float baseProgress = 0f;

        // 1. Noise plane visual (reads heights from the loaded grid)
        if (noisePlane != null)
        {
            ReportProgress(baseProgress, "Building noise plane visual...");
            Debug.Log("GameTerrainBuilder: step 1/3 — building noise plane visual.");
            noisePlane.BuildVisual();
            yield return null;
        }
        else
        {
            Debug.LogWarning("GameTerrainBuilder: no PerlinNoisePlane assigned — skipping noise step.");
        }
        baseProgress += WEIGHT_VISUAL;
        ReportProgress(baseProgress, "Noise plane built.");

        // 2. Slope map visual
        if (slopeMap != null)
        {
            ReportProgress(baseProgress, "Building slope map...");
            Debug.Log("GameTerrainBuilder: step 2/3 — generating slope map.");
            slopeMap.Generate();
            yield return null;
        }
        baseProgress += WEIGHT_SLOPE;
        ReportProgress(baseProgress, "Slope map built.");

        // 3. Marching cubes — heaviest step; per-chunk progress
        if (marchingCubes != null)
        {
            Debug.Log("GameTerrainBuilder: step 3/3 — building marching cubes.");
            float mcStart = baseProgress;
            yield return marchingCubes.GenerateAsync(mcFrac =>
            {
                float overall = mcStart + mcFrac * WEIGHT_MC;
                ReportProgress(overall, $"Building terrain mesh... {Mathf.RoundToInt(mcFrac * 100f)}%");
            });
        }
        else
        {
            Debug.LogWarning("GameTerrainBuilder: no MarchingCubesTerrain assigned — skipping mesh step.");
        }

        ReportProgress(1f, "Done.");
        Debug.Log("GameTerrainBuilder: build complete.");
        OnBuildComplete?.Invoke();
        _building = false;
    }

    void ReportProgress(float value, string message)
    {
        Debug.Log($"GameTerrainBuilder: [{Mathf.RoundToInt(value * 100f),3}%] {message}");
        OnBuildProgress?.Invoke(Mathf.Clamp01(value), message);
    }
}
