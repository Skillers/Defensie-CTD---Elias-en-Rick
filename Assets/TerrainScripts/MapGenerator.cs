using UnityEngine;

/// <summary>
/// Main orchestrator that creates the CellData grid and drives the terrain
/// generation pipeline step-by-step:
///   1. Create empty grid
///   2. Generate Perlin noise → bake raw heights
///   3. Compute outgoing slopes (from raw data — nicer than rounded)
///   4. Compute rounded heights (0.25 snap for marching cubes)
///   5. Assign biomes (via BiomeAssigner)
///   6. Hand grid to TerrainDataStore
///   7. Generate marching cubes mesh
///   8. Disable rendering on source maps
/// </summary>
public class MapGenerator : MonoBehaviour
{
    [Header("Pipeline Steps")]
    public PerlinNoisePlane    noisePlane;
    public SlopeMap            slopeMap;
    public MarchingCubesTerrain marchingCubes;
    public BiomeAssigner       biomeAssigner;

    [Header("Data")]
    public TerrainDataStore terrainDataStore;


    void Start()
    {
        Generate();
    }

    [ContextMenu("Regenerate")]
    public void Generate()
    {
        if (terrainDataStore == null) { Debug.LogError("MapGenerator: no TerrainDataStore assigned."); return; }

        float stp = terrainDataStore.step;
        int width  = Mathf.RoundToInt(terrainDataStore.extentX * 2f / stp) + 1;
        int height = Mathf.RoundToInt(terrainDataStore.extentZ * 2f / stp) + 1;

        // 1. Create empty grid
        CellData[,] grid = new CellData[width, height];

        // 2. Generate Perlin noise and bake raw heights into cells
        if (noisePlane == null) { Debug.LogError("MapGenerator: no PerlinNoisePlane assigned."); return; }
        if (!noisePlane.Generate()) { Debug.LogError("MapGenerator: Perlin noise generation failed."); return; }

        for (int x = 0; x < width; x++)
        for (int z = 0; z < height; z++)
        {
            grid[x, z].rawHeight = noisePlane.GetValue(x, z) * terrainDataStore.heightMultiplier;
        }
        Debug.Log("MapGenerator: Perlin noise generated and raw heights baked.");

        // 3. Compute rounded heights for marching cubes
        BakeRoundedHeights(grid, width, height);
        Debug.Log("MapGenerator: rounded heights baked.");



 
        // 7. Hand grid to TerrainDataStore
        terrainDataStore.SetGrid(grid);
        Debug.Log("MapGenerator: grid assigned to TerrainDataStore.");

        // 8. Generate slope visualization (reads from grid)
        if (slopeMap != null)
        {
            slopeMap.Generate();
            Debug.Log("MapGenerator: slope map generated.");
        }

        // 9. Generate marching cubes mesh
        if (marchingCubes != null)
        {
            marchingCubes.Generate();
            Debug.Log("MapGenerator: marching cubes generated.");
        }

        Debug.Log("MapGenerator: pipeline complete.");
    }

    void BakeRoundedHeights(CellData[,] grid, int width, int height)
    {
        for (int x = 0; x < width; x++)
        for (int z = 0; z < height; z++)
        {
            grid[x, z].roundedHeight = Mathf.Round(grid[x, z].rawHeight / terrainDataStore.roundStep) * terrainDataStore.roundStep;
        }
    }

}
