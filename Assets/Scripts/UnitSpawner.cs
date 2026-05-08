using UnityEngine;

/// <summary>
/// Spawns a unit at the start flag cell when the save is loaded and tells it to
/// pathfind toward the end flag cell. Drop one of these into a gameplay scene
/// alongside the TerrainDataStore.
/// </summary>
public class UnitSpawner : MonoBehaviour
{
    [Header("References")]
    public TerrainDataStore terrainDataStore;

    [Header("Unit")]
    [Tooltip("Prefab spawned at the start cell. Must have a UnitMover component on its root.")]
    public GameObject unitPrefab;
    [Tooltip("Unit type used for biome cost lookup and slope rules.")]
    public UnitTypeSO unitType;

    [Header("Placement")]
    [Tooltip("Extra height added to the terrain surface at the spawn position.")]
    public float heightOffset = 0f;

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

        mover.Initialize(terrainDataStore, endCell, unitType);
    }
}
