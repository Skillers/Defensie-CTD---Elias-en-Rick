using UnityEngine;

/// <summary>Creates the CellData grid and drives the terrain generation pipeline: noise → slopes → biomes → visuals.</summary>
public class MapGenerator : MonoBehaviour
{
    [Header("Pipeline Steps")]
    public PerlinNoisePlane     noisePlane;
    public SlopeMap             slopeMap;
    public MarchingCubesTerrain marchingCubes;
    public BiomeAssigner        biomeAssigner;

    [Header("Data")]
    public TerrainDataStore terrainDataStore;

    void Awake()
    {
        if (terrainDataStore == null) { Debug.LogError("MapGenerator: no TerrainDataStore assigned."); return; }

        // Subscribe in Awake, before TerrainDataStore's Start auto-load fires.
        terrainDataStore.OnSaveLoaded  += HandleSaveLoaded;
        terrainDataStore.OnSaveCreated += HandleSaveCreated;
        terrainDataStore.OnSaveFailed  += HandleSaveFailed;
    }

    void OnDestroy()
    {
        if (terrainDataStore == null) return;
        terrainDataStore.OnSaveLoaded  -= HandleSaveLoaded;
        terrainDataStore.OnSaveCreated -= HandleSaveCreated;
        terrainDataStore.OnSaveFailed  -= HandleSaveFailed;
    }

    void HandleSaveLoaded()
    {
        Debug.Log($"MapGenerator: save loaded from {terrainDataStore.SaveFilePath} — rebuilding visuals only.");
        BuildVisualsFromGrid();
    }

    void HandleSaveCreated()
    {
        Debug.Log($"MapGenerator: no save at {terrainDataStore.SaveFilePath} — generating fresh terrain.");
        Generate();
        terrainDataStore.WriteSave();
        Debug.Log($"MapGenerator: save written to {terrainDataStore.SaveFilePath}.");
    }

    void HandleSaveFailed(string reason)
    {
        Debug.LogError($"MapGenerator: save load failed ({reason}) — regenerating.");
        Generate();
        terrainDataStore.WriteSave();
    }

    /// <summary>Rebuilds the visual meshes from the loaded grid; no Perlin re-sampling needed.</summary>
    void BuildVisualsFromGrid()
    {
        if (noisePlane != null) noisePlane.BuildVisual();
        if (slopeMap != null) slopeMap.Generate();
        if (marchingCubes != null) marchingCubes.Generate();
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
        Debug.Log($"MapGenerator: grid created ({width}x{height}).");

        // 2. Generate Perlin noise and bake raw heights into cells
        if (noisePlane == null) { Debug.LogError("MapGenerator: no PerlinNoisePlane assigned."); return; }
        if (!noisePlane.GenerateNoise()) { Debug.LogError("MapGenerator: Perlin noise generation failed."); return; }

        for (int x = 0; x < width; x++)
        for (int z = 0; z < height; z++)
            grid[x, z].rawHeight = noisePlane.GetValue(x, z) * terrainDataStore.heightMultiplier;
        Debug.Log("MapGenerator: step 2 — raw heights baked.");

        // 3. Compute outgoing slopes from raw heights
        if (slopeMap != null)
        {
            slopeMap.ComputeAndBake(grid, width, height);
            Debug.Log("MapGenerator: step 3 — slopes computed.");
        }

        // 4. Assign biomes
        if (biomeAssigner != null)
        {
            biomeAssigner.Assign(grid, width, height);
            Debug.Log("MapGenerator: step 4 — biomes assigned.");
        }
        else
        {
            Debug.LogWarning("MapGenerator: no BiomeAssigner — cells will have no biome.");
        }

        // 5. Hand grid to TerrainDataStore
        terrainDataStore.SetGrid(grid);
        Debug.Log("MapGenerator: step 5 — grid assigned to TerrainDataStore.");

        // 6. Build visual meshes
        noisePlane.BuildVisual();
        Debug.Log("MapGenerator: step 6 — noise plane mesh built.");

        if (slopeMap != null)
        {
            slopeMap.Generate();
            Debug.Log("MapGenerator: step 6 — slope map visual built.");
        }

        // 7. Generate marching cubes mesh (also bakes rounded heights + biome colors)
        if (marchingCubes != null)
        {
            marchingCubes.Generate();
            Debug.Log("MapGenerator: step 7 — marching cubes generated.");
        }

        Debug.Log("MapGenerator: pipeline complete.");
    }
}
