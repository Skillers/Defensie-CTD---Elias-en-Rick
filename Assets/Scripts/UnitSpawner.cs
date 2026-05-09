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

    void Awake()
    {
        if (terrainDataStore != null)
            terrainDataStore.OnSaveLoaded += HandleSaveLoaded;
    }

    void OnDestroy()
    {
        if (terrainDataStore != null)
            terrainDataStore.OnSaveLoaded -= HandleSaveLoaded;
    }

    void HandleSaveLoaded()
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
