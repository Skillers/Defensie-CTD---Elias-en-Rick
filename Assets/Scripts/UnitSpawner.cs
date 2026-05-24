using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns a unit at the start flag cell when the save is loaded, then hands it the
/// route built by <see cref="AStarPathGeneration"/> for its unit type. If that
/// route runs through an avenue, an on-screen warning naming the avenue is shown
/// for <see cref="warningSeconds"/> before the unit starts moving.
/// Path <em>making</em> lives in the scene path maker, not on the unit.
/// </summary>
public class UnitSpawner : MonoBehaviour
{
    [Header("References")]
    public TerrainDataStore terrainDataStore;
    [Tooltip("Recommended. Builder that does the async visual terrain build. When wired, the spawner waits for OnBuildComplete (terrain visually ready) instead of OnSaveLoaded (data only).")]
    public GameTerrainBuilder gameTerrainBuilder;
    [Tooltip("Optional. Black-screen overlay shown while the terrain builds. Hides itself before the AoA warning appears.")]
    public LoadingScreen loadingScreen;
    [Tooltip("Scene path maker. Generates one route per unit type. Auto-found if left empty.")]
    public AStarPathGeneration pathGeneration;
    [Tooltip("Optional. If set, the avenue title is shown on screen for warningSeconds before the unit starts moving.")]
    public WarningDisplay warningDisplay;

    [Header("Unit")]
    [Tooltip("Prefab spawned at the start cell. Must have a UnitMover component on its root.")]
    public GameObject unitPrefab;
    [Tooltip("Unit type used for biome cost lookup and slope rules.")]
    public UnitTypeSO unitType;

    [Header("Placement")]
    [Tooltip("Extra height added to the terrain surface at the spawn position.")]
    public float heightOffset = 0f;

    [Header("AoA Warning")]
    [Tooltip("Seconds the on-screen warning is displayed before the unit starts moving along the chosen avenue.")]
    public float warningSeconds = 3f;

    [Header("Ghost")]
    [Tooltip("Behavioural settings for the unit's ghost orb. Pushed into UnitMover at spawn time. Visual fields (colour / size / hover / stem) live on the UnitGhost prefab.")]
    public GhostSettings ghostSettings = new GhostSettings();

    GameObject _spawned;

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
        if (pathGeneration == null) pathGeneration = FindFirstObjectByType<AStarPathGeneration>();

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
    }

    void HandleReady()
    {
        // Loading screen waits out its min-display time, then chains into the spawn sequence.
        // Without one, spawn immediately.
        if (loadingScreen != null) loadingScreen.Hide(SpawnUnit);
        else SpawnUnit();
    }

    void SpawnUnit()
    {
        if (terrainDataStore == null) return;

        if (!terrainDataStore.StartCell.HasValue || !terrainDataStore.EndCell.HasValue)
        {
            Debug.LogWarning("UnitSpawner: save has no start and/or end flag — skipping spawn.");
            return;
        }

        if (unitPrefab == null)
        {
            Debug.LogWarning("UnitSpawner: no unitPrefab assigned.");
            return;
        }

        if (pathGeneration == null)
        {
            Debug.LogError("UnitSpawner: no AStarPathGeneration in the scene — cannot route the unit.");
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

        AStarPathGeneration.GeneratedRoute route = pathGeneration.GetRoute(unitType);
        if (route == null)
        {
            Debug.LogWarning($"UnitSpawner: AStarPathGeneration produced no route for unit type '{unitType?.typeName}'.");
            return;
        }

        // Capture for the closure so a later spawn can't redirect this unit's route.
        // Plan registration is deferred to movement start to keep MissionSession's
        // latest-only timing identical to the old per-unit flow.
        TerrainDataStore tds  = terrainDataStore;
        UnitTypeSO       type = unitType;
        AStarPathGeneration gen = pathGeneration;

        if (warningDisplay != null && warningSeconds > 0f && !string.IsNullOrEmpty(route.avenueTitle))
        {
            warningDisplay.Show(
                $"Unit taking: {route.avenueTitle}",
                warningSeconds,
                () =>
                {
                    UnitPathPlan plan = gen.BuildAndRegisterPlan(mover.unitId, type);
                    mover.FollowPath(tds, type, route.goalCell, route.path, plan);
                });
        }
        else
        {
            UnitPathPlan plan = gen.BuildAndRegisterPlan(mover.unitId, type);
            mover.FollowPath(tds, type, route.goalCell, route.path, plan);
        }
    }
}
