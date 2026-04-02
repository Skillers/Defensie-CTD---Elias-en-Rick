using UnityEngine;

/// <summary>
/// Central data store for per-cell terrain information.
/// Aggregates terrain type/cost from a biome map, height from PerlinNoisePlane,
/// and slope from SlopeMap into a single queryable place.
///
/// Query by world position:  TerrainDataStore.GetData(worldPos)
/// Query by grid coordinate: TerrainDataStore.GetData(gridX, gridZ)
/// </summary>
public class TerrainDataStore : MonoBehaviour
{
    [Header("Config")]
    public TerrainConfigHolder configHolder;
    private PlaneConfig config => configHolder?.config;

    [Header("Data Sources")]
    public BiomeVoronoiMap voronoiMap;
    public PerlinNoisePlane noisePlane;
    public SlopeMap         slopeMap;

    // Cached grid dimensions derived from the biome map grid after generation
    public int GridWidth  { get; private set; }
    public int GridHeight { get; private set; }

    void Awake()
    {
        if (noisePlane != null) noisePlane.OnGenerated  += OnTerrainGenerated;
        if (voronoiMap != null) voronoiMap.OnGenerated  += OnTerrainGenerated;
    }

    void OnDestroy()
    {
        if (noisePlane != null) noisePlane.OnGenerated  -= OnTerrainGenerated;
        if (voronoiMap != null) voronoiMap.OnGenerated  -= OnTerrainGenerated;
    }

    void OnTerrainGenerated()
    {
        var grid = GetBiomeGrid();
        if (grid != null)
        {
            GridWidth  = grid.GetLength(0);
            GridHeight = grid.GetLength(1);
        }
    }

    /// <summary>Returns terrain data at the given world position.</summary>
    public TerrainPoint GetData(Vector3 worldPos)
    {
        if (config == null) return default;

        int gx = Mathf.Clamp(Mathf.FloorToInt(worldPos.x + GridWidth  * 0.5f), 0, GridWidth  - 1);
        int gz = Mathf.Clamp(Mathf.FloorToInt(worldPos.z + GridHeight * 0.5f), 0, GridHeight - 1);
        return GetData(gx, gz);
    }

    /// <summary>Returns terrain data at the given grid coordinate.</summary>
    public TerrainPoint GetData(int gridX, int gridZ)
    {
        var point = new TerrainPoint();

        // --- Terrain type and movement cost from biome map ---
        var grid = GetBiomeGrid();
        if (grid != null)
        {
            int x = Mathf.Clamp(gridX, 0, grid.GetLength(0) - 1);
            int z = Mathf.Clamp(gridZ, 0, grid.GetLength(1) - 1);
            var cell = grid[x, z];
            point.terrainType  = cell.type;
            point.movementCost = cell.movementCost;
        }

        // --- Height from PerlinNoisePlane ---
        if (noisePlane != null && noisePlane.NoiseValues != null && config != null)
        {
            // Biome cells are 1 unit wide; noise vertices are 0.5 units apart.
            // Map grid cell center to the nearest noise vertex.
            float worldX = gridX - GridWidth  * 0.5f + 0.5f;
            float worldZ = gridZ - GridHeight * 0.5f + 0.5f;
            int xi = Mathf.Clamp(Mathf.RoundToInt((worldX + config.extentX) / 0.5f), 0, noisePlane.VertsX - 1);
            int zi = Mathf.Clamp(Mathf.RoundToInt((worldZ + config.extentZ) / 0.5f), 0, noisePlane.VertsZ - 1);
            point.height = noisePlane.GetValue(xi, zi) * config.heightMultiplier;
        }

        // --- Slope from SlopeMap ---
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

    MilitaryTerrainCell[,] GetBiomeGrid()
    {
        if (voronoiMap != null && voronoiMap.grid != null) return voronoiMap.grid;
        return null;
    }
}

/// <summary>All per-cell terrain data in one place.</summary>
public struct TerrainPoint
{
    public MilitaryTerrainType terrainType;
    public int   movementCost;
    public float height;
    public float slopeDegrees;
}
