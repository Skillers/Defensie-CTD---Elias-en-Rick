using UnityEngine;

/// <summary>
/// Central data store for per-cell terrain information.
/// Aggregates biome data, height from PerlinNoisePlane,
/// and slope from SlopeMap into a single queryable place.
/// Also provides grid/world coordinate conversion used by all gameplay systems.
/// </summary>
public class TerrainDataStore : MonoBehaviour
{
    [Header("Config")]
    public TerrainConfigHolder configHolder;
    private PlaneConfig config => configHolder?.config;

    [Header("Data Sources")]
    public PerlinNoisePlane noisePlane;
    public SlopeMap         slopeMap;

    /// <summary>The biome grid. Set externally by whatever system generates biomes.</summary>
    [HideInInspector] public BiomeCell[,] grid;

    public int GridWidth  { get; private set; }
    public int GridHeight { get; private set; }

    public event System.Action OnGridReady;

    void Awake()
    {
        if (noisePlane != null) noisePlane.OnGenerated += OnTerrainGenerated;
    }

    void OnDestroy()
    {
        if (noisePlane != null) noisePlane.OnGenerated -= OnTerrainGenerated;
    }

    void OnTerrainGenerated()
    {
        if (grid != null)
        {
            GridWidth  = grid.GetLength(0);
            GridHeight = grid.GetLength(1);
            OnGridReady?.Invoke();
        }
    }

    /// <summary>Call after assigning the grid to update dimensions and notify listeners.</summary>
    public void SetGrid(BiomeCell[,] biomeGrid)
    {
        grid = biomeGrid;
        if (grid != null)
        {
            GridWidth  = grid.GetLength(0);
            GridHeight = grid.GetLength(1);
            OnGridReady?.Invoke();
        }
    }

    // ── Queries ──────────────────────────────────────────────────────────

    /// <summary>Returns terrain data at the given world position.</summary>
    public TerrainPoint GetData(Vector3 worldPos, UnitTypeSO unitType = null)
    {
        Vector2Int g = WorldToGrid(worldPos);
        return GetData(g.x, g.y, unitType);
    }

    /// <summary>Returns terrain data at the given grid coordinate.</summary>
    public TerrainPoint GetData(int gridX, int gridZ, UnitTypeSO unitType = null)
    {
        var point = new TerrainPoint();

        if (grid != null)
        {
            int x = Mathf.Clamp(gridX, 0, GridWidth  - 1);
            int z = Mathf.Clamp(gridZ, 0, GridHeight - 1);
            var cell = grid[x, z];
            if (cell?.biome != null)
            {
                point.biome        = cell.biome;
                point.movementCost = cell.biome.GetMovementCost(unitType);
            }
        }

        if (noisePlane != null && noisePlane.NoiseValues != null && config != null)
        {
            float worldX = gridX - GridWidth  * 0.5f + 0.5f;
            float worldZ = gridZ - GridHeight * 0.5f + 0.5f;
            int xi = Mathf.Clamp(Mathf.RoundToInt((worldX + config.extentX) / 0.5f), 0, noisePlane.VertsX - 1);
            int zi = Mathf.Clamp(Mathf.RoundToInt((worldZ + config.extentZ) / 0.5f), 0, noisePlane.VertsZ - 1);
            point.height = noisePlane.GetValue(xi, zi) * config.heightMultiplier;
        }

        if (slopeMap != null && slopeMap.SlopeAngles != null && config != null)
        {
            float worldX = gridX - GridWidth  * 0.5f + 0.5f;
            float worldZ = gridZ - GridHeight * 0.5f + 0.5f;
            int xi = Mathf.Clamp(Mathf.RoundToInt((worldX + config.extentX) / 0.5f), 0, slopeMap.VertsX - 1);
            int zi = Mathf.Clamp(Mathf.RoundToInt((worldZ + config.extentZ) / 0.5f), 0, slopeMap.VertsZ - 1);
            point.slopeDegrees = slopeMap.GetSlope(xi, zi);
        }

        return point;
    }

    // ── Grid / World conversion ──────────────────────────────────────────

    public bool InBounds(int x, int z)
    {
        return grid != null && x >= 0 && x < GridWidth && z >= 0 && z < GridHeight;
    }

    public Vector2Int WorldToGrid(Vector3 world)
    {
        Vector3 local = world - transform.position;
        int gx = Mathf.FloorToInt(local.x + GridWidth  * 0.5f);
        int gz = Mathf.FloorToInt(local.z + GridHeight * 0.5f);
        return new Vector2Int(
            Mathf.Clamp(gx, 0, GridWidth  - 1),
            Mathf.Clamp(gz, 0, GridHeight - 1));
    }

    public Vector3 GridToWorld(Vector2Int cell)
    {
        float wx = cell.x - GridWidth  * 0.5f + 0.5f;
        float wz = cell.y - GridHeight * 0.5f + 0.5f;
        return transform.position + new Vector3(wx, 0f, wz);
    }
}

/// <summary>All per-cell terrain data in one place.</summary>
public struct TerrainPoint
{
    public BiomeSO biome;
    public int   movementCost;
    public float height;
    public float slopeDegrees;
}
