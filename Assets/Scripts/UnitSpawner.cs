using UnityEngine;

/// <summary>
/// Spawns a unit at the start flag cell when the save is loaded. If an
/// <see cref="AvenueRuntimeStore"/> is wired and the save contains avenues, picks
/// one at random, shows a 3-second on-screen warning naming it, then walks the
/// unit through that avenue's waypoints to the end flag. Otherwise pathfinds
/// directly start → end.
/// </summary>
public class UnitSpawner : MonoBehaviour
{
    [Header("References")]
    public TerrainDataStore terrainDataStore;
    [Tooltip("Recommended. Builder that does the async visual terrain build. When wired, the spawner waits for OnBuildComplete (terrain visually ready) instead of OnSaveLoaded (data only).")]
    public GameTerrainBuilder gameTerrainBuilder;
    [Tooltip("Optional. Black-screen overlay shown while the terrain builds. Hides itself before the AoA warning appears.")]
    public LoadingScreen loadingScreen;
    [Tooltip("Optional. If set and the save has avenues, the unit picks one at random and walks its waypoints before reaching the end flag.")]
    public AvenueRuntimeStore avenueStore;
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

    GameObject _spawned;

    static int _nextUnitId = 1;

    void Awake()
    {
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

        Vector2Int startCell = terrainDataStore.StartCell.Value;
        Vector2Int endCell   = terrainDataStore.EndCell.Value;

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

        AvenueData picked = PickRandomAvenue();
        if (picked == null)
        {
            // No avenues loaded — preserve the original direct-to-end behaviour.
            mover.Initialize(terrainDataStore, endCell, unitType);
            return;
        }

        if (warningDisplay != null && warningSeconds > 0f)
        {
            // Capture for the closure so a later spawn can't redirect this unit's route.
            TerrainDataStore tds = terrainDataStore;
            UnitTypeSO type      = unitType;
            warningDisplay.Show(
                $"Unit taking: {picked.title}",
                warningSeconds,
                () => mover.InitializeWithRoute(tds, picked.waypoints, endCell, type));
        }
        else
        {
            mover.InitializeWithRoute(terrainDataStore, picked.waypoints, endCell, unitType);
        }
    }

    AvenueData PickRandomAvenue()
    {
        if (avenueStore == null) return null;
        var list = avenueStore.Avenues;
        if (list == null || list.Count == 0) return null;
        return list[Random.Range(0, list.Count)];
    }
}
