using UnityEngine;

/// <summary>
/// Spawns the start/end flag prefabs at the cells stored in the
/// <see cref="TerrainDataStore"/> when a save is loaded. Used by scenes that
/// need to *display* flags but don't run the placement controls (e.g. the
/// in-game scene). The level editor's FlagPlacementTool handles its own
/// visuals during placement.
/// </summary>
public class FlagDisplay : MonoBehaviour
{
    [Header("References")]
    public TerrainDataStore terrainDataStore;

    [Header("Flag Prefabs")]
    [Tooltip("Prefab spawned at the start cell.")]
    public GameObject startFlagPrefab;
    [Tooltip("Prefab spawned at the end cell. If null, startFlagPrefab is reused.")]
    public GameObject endFlagPrefab;

    [Header("Placement Offset")]
    [Tooltip("Extra height added to the terrain surface when positioning flags.")]
    public float heightOffset = 0f;

    GameObject _startFlagInstance;
    GameObject _endFlagInstance;

    void Awake()
    {
        // Subscribe in Awake so we're hooked up before TerrainDataStore.Start
        // fires its auto-load and we'd miss OnSaveLoaded.
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

        if (terrainDataStore.StartCell.HasValue)
        {
            Vector3 worldPos = WorldPosForCell(terrainDataStore.StartCell.Value);
            SpawnOrMove(ref _startFlagInstance, startFlagPrefab, worldPos, "StartFlag");
        }

        if (terrainDataStore.EndCell.HasValue)
        {
            var prefab = endFlagPrefab != null ? endFlagPrefab : startFlagPrefab;
            Vector3 worldPos = WorldPosForCell(terrainDataStore.EndCell.Value);
            SpawnOrMove(ref _endFlagInstance, prefab, worldPos, "EndFlag");
        }
    }

    Vector3 WorldPosForCell(Vector2Int cell)
    {
        Vector3 pos = terrainDataStore.GridToWorld(cell);
        pos.y = terrainDataStore.GetRoundedHeight(cell.x, cell.y) + heightOffset;
        return pos;
    }

    void SpawnOrMove(ref GameObject instance, GameObject prefab, Vector3 worldPos, string objectName)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"FlagDisplay: no prefab assigned for {objectName}.");
            return;
        }

        if (instance == null)
        {
            instance = Instantiate(prefab, worldPos, Quaternion.identity, transform);
            instance.name = objectName;
        }
        else
        {
            instance.transform.position = worldPos;
        }
    }
}
