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

    private const float RoundStep = 0.25f;
    private const float NoiseStep = 0.5f;

    void Start()
    {
        Generate();
    }

    [ContextMenu("Regenerate")]
    public void Generate()
    {
        if (terrainDataStore == null) { Debug.LogError("MapGenerator: no TerrainDataStore assigned."); return; }

        int width  = Mathf.RoundToInt(terrainDataStore.extentX * 2f);
        int height = Mathf.RoundToInt(terrainDataStore.extentZ * 2f);

        // 1. Create empty grid
        var grid = new CellData[width, height];

        // 2. Generate Perlin noise
        if (noisePlane == null) { Debug.LogError("MapGenerator: no PerlinNoisePlane assigned."); return; }
        noisePlane.Generate();
        Debug.Log("MapGenerator: Perlin noise generated.");

        // 3. Bake raw heights into cells
        BakeRawHeights(grid, width, height);
        Debug.Log("MapGenerator: raw heights baked.");

        // 4. Compute outgoing slopes from raw heights (before rounding — smoother)
        BakeSlopes(grid, width, height);
        Debug.Log("MapGenerator: slopes baked.");

        // 5. Generate slope visualization (optional, uses the noise plane data)
        if (slopeMap != null)
        {
            slopeMap.Generate();
            Debug.Log("MapGenerator: slope map generated.");
        }

        // 6. Compute rounded heights for marching cubes
        BakeRoundedHeights(grid, width, height);
        Debug.Log("MapGenerator: rounded heights baked.");

        // 7. Assign biomes
        if (biomeAssigner != null)
        {
            biomeAssigner.Assign(grid, width, height);
            Debug.Log("MapGenerator: biomes assigned.");
        }

        // 8. Hand grid to TerrainDataStore
        terrainDataStore.SetGrid(grid);
        Debug.Log("MapGenerator: grid assigned to TerrainDataStore.");

        // 9. Generate marching cubes mesh
        if (marchingCubes != null)
        {
            marchingCubes.Generate();
            Debug.Log("MapGenerator: marching cubes generated.");
        }

        // 10. Disable rendering on source maps
        DisableSourceRendering();

        Debug.Log("MapGenerator: pipeline complete.");
    }

    void BakeRawHeights(CellData[,] grid, int width, int height)
    {
        for (int x = 0; x < width; x++)
        for (int z = 0; z < height; z++)
        {
            grid[x, z].rawHeight = SampleHeight(x, z, width, height);
        }
    }

    void BakeSlopes(CellData[,] grid, int width, int height)
    {
        for (int x = 0; x < width; x++)
        for (int z = 0; z < height; z++)
        {
            grid[x, z].slopeOutgoing = new float[8];
            float h = grid[x, z].rawHeight;

            for (int d = 0; d < 8; d++)
            {
                int nx = x + CellData.Directions[d].x;
                int nz = z + CellData.Directions[d].y;

                if (nx < 0 || nx >= width || nz < 0 || nz >= height)
                {
                    grid[x, z].slopeOutgoing[d] = 0f;
                    continue;
                }

                float nh = grid[nx, nz].rawHeight;
                bool isDiagonal = CellData.Directions[d].x != 0 && CellData.Directions[d].y != 0;
                float dist = isDiagonal ? 1.41421356f : 1f;
                grid[x, z].slopeOutgoing[d] = Mathf.Atan2(nh - h, dist) * Mathf.Rad2Deg;
            }
        }
    }

    void BakeRoundedHeights(CellData[,] grid, int width, int height)
    {
        for (int x = 0; x < width; x++)
        for (int z = 0; z < height; z++)
        {
            grid[x, z].roundedHeight = Mathf.Round(grid[x, z].rawHeight / RoundStep) * RoundStep;
        }
    }

    float SampleHeight(int gridX, int gridZ, int width, int height)
    {
        if (noisePlane.NoiseValues == null) return 0f;

        float worldX = gridX - width  * 0.5f + 0.5f;
        float worldZ = gridZ - height * 0.5f + 0.5f;
        int xi = Mathf.Clamp(Mathf.RoundToInt((worldX + terrainDataStore.extentX) / NoiseStep), 0, noisePlane.VertsX - 1);
        int zi = Mathf.Clamp(Mathf.RoundToInt((worldZ + terrainDataStore.extentZ) / NoiseStep), 0, noisePlane.VertsZ - 1);
        return noisePlane.GetValue(xi, zi) * terrainDataStore.heightMultiplier;
    }

    void DisableSourceRendering()
    {
        if (noisePlane != null)
        {
            var mr = noisePlane.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
        }
        if (slopeMap != null)
        {
            var mr = slopeMap.GetComponent<MeshRenderer>();
            if (mr != null) mr.enabled = false;
        }
    }
}
