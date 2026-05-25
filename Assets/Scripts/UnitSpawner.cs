using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the prep → play → walk flow for a single unit. After the terrain is ready
/// the spawner enters the prep phase: the route built by <see cref="AStarPathGeneration"/>
/// is shown via <see cref="PathPreviewRenderer"/> and the AoA warning naming the
/// chosen avenue is displayed once. The unit prefab itself is not instantiated yet.
/// Pressing the play button on <see cref="MissionFlowController"/> ends prep — the
/// spawner then instantiates the unit at the start cell and hands it the route via
/// <see cref="UnitMover.FollowPath"/>, which is when the mission timer begins.
/// Path <em>making</em> lives in the scene path maker, not on the unit.
/// </summary>
public class UnitSpawner : MonoBehaviour
{
    [Header("References")]
    public TerrainDataStore terrainDataStore;
    [Tooltip("Recommended. Builder that does the async visual terrain build. When wired, the spawner waits for OnBuildComplete (terrain visually ready) instead of OnSaveLoaded (data only).")]
    public GameTerrainBuilder gameTerrainBuilder;
    [Tooltip("Optional. Black-screen overlay shown while the terrain builds. Hides itself before prep begins.")]
    public LoadingScreen loadingScreen;
    [Tooltip("Scene path maker. Generates one route per unit type. Auto-found if left empty.")]
    public AStarPathGeneration pathGeneration;
    [Tooltip("Optional. If set, the avenue title is shown on screen for warningSeconds at the start of prep.")]
    public WarningDisplay warningDisplay;
    [Tooltip("Owns the Play button. Spawn is gated on its OnPlay event. Auto-found if left empty; without one the spawner falls back to auto-start (no prep phase).")]
    public MissionFlowController flowController;
    [Tooltip("Optional. Renders the precomputed path during prep. Auto-created if left empty.")]
    public PathPreviewRenderer pathPreview;

    [Header("Unit")]
    [Tooltip("Prefab spawned at the start cell when the player presses Play. Must have a UnitMover component on its root.")]
    public GameObject unitPrefab;
    [Tooltip("Unit type used for biome cost lookup and slope rules.")]
    public UnitTypeSO unitType;

    [Header("Placement")]
    [Tooltip("Extra height added to the terrain surface at the spawn position.")]
    public float heightOffset = 0f;

    [Header("AoA Warning")]
    [Tooltip("Seconds the on-screen warning is displayed at the start of prep. Purely informational now — Play is what triggers the unit, not this timer.")]
    public float warningSeconds = 3f;

    [Header("Ghost")]
    [Tooltip("Behavioural settings for the unit's ghost orb. Pushed into UnitMover at spawn time. Visual fields (colour / size / hover / stem) live on the UnitGhost prefab.")]
    public GhostSettings ghostSettings = new GhostSettings();

    GameObject _spawned;
    AStarPathGeneration.GeneratedRoute _prepRoute;
    bool _prepSubscribed;
    bool _prepped;

    static int _nextUnitId = 1;

    /// <summary>One unit the spawner will instantiate, paired with its type.</summary>
    public struct SpawnRequest
    {
        public GameObject prefab;
        public UnitTypeSO unitType;
    }

    /// <summary>
    /// The units this spawner will spawn, read by <see cref="AStarPathGeneration"/>
    /// to generate one route per type. Single entry today; becomes a real list when
    /// multi-type spawning is added.
    /// </summary>
    public IEnumerable<SpawnRequest> GetSpawnRequests()
    {
        yield return new SpawnRequest { prefab = unitPrefab, unitType = unitType };
    }

    void Awake()
    {
        if (pathGeneration  == null) pathGeneration  = FindFirstObjectByType<AStarPathGeneration>();
        if (flowController  == null) flowController  = FindFirstObjectByType<MissionFlowController>();

        // Prefer OnBuildComplete: data-only OnSaveLoaded fires before the terrain mesh exists,
        // so the unit would spawn into a void. Fall back to OnSaveLoaded for scenes without a builder.
        if (gameTerrainBuilder != null)
            gameTerrainBuilder.OnBuildComplete += HandleReady;
        else if (terrainDataStore != null)
            terrainDataStore.OnSaveLoaded += HandleReady;
    }

    void OnDestroy()
    {
        if (gameTerrainBuilder != null)
            gameTerrainBuilder.OnBuildComplete -= HandleReady;
        else if (terrainDataStore != null)
            terrainDataStore.OnSaveLoaded -= HandleReady;

        if (_prepSubscribed && flowController != null)
        {
            flowController.OnPlay -= HandlePlay;
            _prepSubscribed = false;
        }
    }

    void HandleReady()
    {
        // Loading screen waits out its min-display time, then chains into prep.
        // Without one, enter prep immediately.
        if (loadingScreen != null) loadingScreen.Hide(BeginPrep);
        else BeginPrep();
    }

    /// <summary>
    /// Prep phase: show the precomputed path and AoA warning, then wait on
    /// <see cref="MissionFlowController.OnPlay"/>. If no flow controller is wired,
    /// auto-starts so older scenes without a Play button still work.
    /// </summary>
    void BeginPrep()
    {
        if (_prepped) return;  // Re-fired OnSaveLoaded / OnBuildComplete shouldn't double-prep.
        if (!ValidateForSpawn()) return;
        _prepped = true;

        _prepRoute = pathGeneration.GetRoute(unitType);
        if (_prepRoute == null)
        {
            Debug.LogWarning($"UnitSpawner: AStarPathGeneration produced no route for unit type '{unitType?.typeName}'.");
            return;
        }

        EnsurePathPreview();
        pathPreview.Show(terrainDataStore, _prepRoute.path);

        if (warningDisplay != null && warningSeconds > 0f && !string.IsNullOrEmpty(_prepRoute.avenueTitle))
            warningDisplay.Show($"Unit taking: {_prepRoute.avenueTitle}", warningSeconds);

        if (flowController != null)
        {
            flowController.OnPlay += HandlePlay;
            _prepSubscribed = true;
        }
        else
        {
            Debug.LogWarning("UnitSpawner: no MissionFlowController in the scene — starting immediately without a prep phase.");
            HandlePlay();
        }
    }

    /// <summary>
    /// Play handler: tears down the prep visuals, instantiates the unit at the start
    /// cell and hands it the cached route. <see cref="UnitMover.FollowPath"/> is what
    /// kicks off movement and starts the mission timer, so this is also where the
    /// plan is registered with <see cref="MissionSession"/>.
    /// </summary>
    void HandlePlay()
    {
        if (_prepSubscribed && flowController != null)
        {
            flowController.OnPlay -= HandlePlay;
            _prepSubscribed = false;
        }

        if (pathPreview != null) pathPreview.Hide();

        // Re-fetch in case _prepRoute was never set (auto-start path with no prep).
        AStarPathGeneration.GeneratedRoute route = _prepRoute ?? pathGeneration.GetRoute(unitType);
        if (route == null)
        {
            Debug.LogWarning($"UnitSpawner: no route for '{unitType?.typeName}' at play time — skipping spawn.");
            return;
        }

        Vector2Int startCell = terrainDataStore.StartCell.Value;
        Vector3 spawnPos = terrainDataStore.GridToWorld(startCell);
        spawnPos.y = terrainDataStore.GetRoundedHeight(startCell.x, startCell.y) + heightOffset;

        if (_spawned != null) Destroy(_spawned);
        _spawned = Instantiate(unitPrefab, spawnPos, Quaternion.identity, transform);
        _spawned.name = "Unit";

        UnitMover mover = _spawned.GetComponent<UnitMover>();
        if (mover == null)
        {
            Debug.LogError("UnitSpawner: spawned prefab has no UnitMover component on its root.");
            return;
        }

        mover.unitId = _nextUnitId++;
        mover.ApplyGhostSettings(ghostSettings);

        UnitPathPlan plan = pathGeneration.BuildAndRegisterPlan(mover.unitId, unitType);
        mover.FollowPath(terrainDataStore, unitType, route.goalCell, route.path, plan);
    }

    bool ValidateForSpawn()
    {
        if (terrainDataStore == null) return false;

        if (!terrainDataStore.StartCell.HasValue || !terrainDataStore.EndCell.HasValue)
        {
            Debug.LogWarning("UnitSpawner: save has no start and/or end flag — skipping spawn.");
            return false;
        }

        if (unitPrefab == null)
        {
            Debug.LogWarning("UnitSpawner: no unitPrefab assigned.");
            return false;
        }

        if (pathGeneration == null)
        {
            Debug.LogError("UnitSpawner: no AStarPathGeneration in the scene — cannot route the unit.");
            return false;
        }

        return true;
    }

    void EnsurePathPreview()
    {
        if (pathPreview != null) return;
        var go = new GameObject("PathPreview");
        go.transform.SetParent(transform, false);
        pathPreview = go.AddComponent<PathPreviewRenderer>();
    }
}
