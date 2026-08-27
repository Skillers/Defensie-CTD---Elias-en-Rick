using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drives the prep → play → walk flow for a single unit: shows the route preview during
/// prep, then on Play instantiates the unit at the start cell and hands it the route.
/// </summary>
public class UnitSpawner : MonoBehaviour
{
    [Header("References")]
    public TerrainDataStore terrainDataStore;
    [Tooltip("When wired, the spawner waits for OnBuildComplete (terrain visually ready) instead of OnSaveLoaded (data only).")]
    public GameTerrainBuilder gameTerrainBuilder;
    [Tooltip("Optional overlay shown while the terrain builds.")]
    public LoadingScreen loadingScreen;
    [Tooltip("Generates one route per unit type. Auto-found if empty.")]
    public AStarPathGeneration pathGeneration;
    [Tooltip("Optional. Shows the avenue title at the start of prep.")]
    public WarningDisplay warningDisplay;
    [Tooltip("Owns the Play button. Auto-found if empty; without one the spawner auto-starts (no prep phase).")]
    public MissionFlowController flowController;
    [Tooltip("Optional. Renders the precomputed path during prep. Auto-created if empty.")]
    public PathPreviewRenderer pathPreview;

    [Header("Unit")]
    [Tooltip("Must have a UnitMover on its root.")]
    public GameObject unitPrefab;
    [Tooltip("Unit type for biome cost lookup and slope rules.")]
    public UnitTypeSO unitType;

    [Header("Placement")]
    [Tooltip("Extra height above the terrain at the spawn position.")]
    public float heightOffset = 0f;

    [Header("AoA Warning")]
    [Tooltip("Seconds the avenue warning is displayed. Informational only.")]
    public float warningSeconds = 3f;

    [Header("Ghost")]
    [Tooltip("Behavioural ghost settings. Visual fields live on the UnitGhost prefab.")]
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

    /// <summary>Units this spawner will spawn, read by <see cref="AStarPathGeneration"/>. Single entry for now.</summary>
    public IEnumerable<SpawnRequest> GetSpawnRequests()
    {
        yield return new SpawnRequest { prefab = unitPrefab, unitType = unitType };
    }

    void Awake()
    {
        if (pathGeneration  == null) pathGeneration  = FindFirstObjectByType<AStarPathGeneration>();
        if (flowController  == null) flowController  = FindFirstObjectByType<MissionFlowController>();

        // Prefer OnBuildComplete: OnSaveLoaded fires before the terrain mesh exists.
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
        if (loadingScreen != null) loadingScreen.Hide(BeginPrep);
        else BeginPrep();
    }

    /// <summary>Shows the path preview and AoA warning, then waits on OnPlay. Auto-starts without a flow controller.</summary>
    void BeginPrep()
    {
        if (_prepped) return;  // re-fired ready events must not double-prep
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

    /// <summary>Tears down prep visuals, spawns the unit and hands it the cached route.</summary>
    void HandlePlay()
    {
        if (_prepSubscribed && flowController != null)
        {
            flowController.OnPlay -= HandlePlay;
            _prepSubscribed = false;
        }

        if (pathPreview != null) pathPreview.Hide();

        // _prepRoute is null on the auto-start path with no prep.
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
