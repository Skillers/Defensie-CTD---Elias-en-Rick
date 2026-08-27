using System.Collections.Generic;
using UnityEngine;

/// <summary>Runtime-only mirror of the avenues in the save file. No UI or visuals; those live in AvenuesOfApproachHandler.</summary>
public class AvenueRuntimeStore : MonoBehaviour
{
    [Header("References")]
    [SerializeField] TerrainDataStore terrainDataStore;

    readonly List<AvenueData> _avenues = new List<AvenueData>();

    /// <summary>All avenues loaded from the most recent save (read-only view).</summary>
    public IReadOnlyList<AvenueData> Avenues => _avenues;

    void Awake()
    {
        // Subscribe in Awake, before TerrainDataStore's Start auto-load fires.
        if (terrainDataStore != null)
            terrainDataStore.OnApplyingSaveData += HandleApplyingSaveData;
    }

    void OnDestroy()
    {
        if (terrainDataStore != null)
            terrainDataStore.OnApplyingSaveData -= HandleApplyingSaveData;
    }

    void HandleApplyingSaveData(SaveData data)
    {
        _avenues.Clear();
        if (data?.avenues == null) return;

        for (int i = 0; i < data.avenues.Length; i++)
        {
            AvenueDto dto = data.avenues[i];
            if (dto == null) continue;

            int xLen = dto.waypointXs?.Length ?? 0;
            int zLen = dto.waypointZs?.Length ?? 0;
            int n    = Mathf.Min(xLen, zLen);

            AvenueData avenue = new AvenueData { title = dto.title ?? string.Empty };
            for (int w = 0; w < n; w++)
                avenue.waypoints.Add(new Vector2Int(dto.waypointXs[w], dto.waypointZs[w]));

            _avenues.Add(avenue);
        }
    }
}

/// <summary>Plain-data view of an avenue for runtime use, distinct from the editor's AvenueOfApproach.</summary>
public class AvenueData
{
    public string title;
    public List<Vector2Int> waypoints = new List<Vector2Int>();
}
