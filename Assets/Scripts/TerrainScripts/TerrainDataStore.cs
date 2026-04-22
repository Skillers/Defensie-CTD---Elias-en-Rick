using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Central data holder for the pre-computed CellData grid.
/// Provides queries and grid/world coordinate conversion for all gameplay systems.
/// The grid is populated by MapGenerator.
/// </summary>
public class TerrainDataStore : MonoBehaviour
{
    [Header("Plane Extents (units from center)")]
    public float extentX = 10f;
    public float extentZ = 10f;

    [Header("Grid Settings")]
    public float step = 0.5f;
    public float roundStep = 0.25f;

    [Header("Noise Settings")]
    public int seed = 0;
    public float noiseScale = 0.1f;
    public Vector2 noiseOffset = Vector2.zero;
    public float heightMultiplier = 0f;

    [Header("Save / Load")]
    [SerializeField] string saveFileName = "level.json";
    [Tooltip("If true, the store checks its save file automatically on Start. Subscribers should register in Awake so they see the fired event.")]
    [SerializeField] bool autoLoadOnStart = true;
    [Tooltip("Base biome always registered as used by this terrain.")]
    [SerializeField] BiomeSO baseBiome;

    [HideInInspector] public CellData[,] grid;

    // Runtime list of biomes actually used in this terrain. Base is always present.
    readonly List<BiomeSO> _usedBiomes = new List<BiomeSO>();
    public IReadOnlyList<BiomeSO> UsedBiomes => _usedBiomes;

    public int GridWidth  { get; private set; }
    public int GridHeight { get; private set; }

    public event System.Action OnGridReady;

    // ── Save / Load events ───────────────────────────────────────────────
    public event System.Action OnSaveLoaded;
    public event System.Action OnSaveCreated;
    public event System.Action<string> OnSaveFailed;

    public string SaveFilePath => Path.Combine(Application.persistentDataPath, saveFileName);
    public bool SaveFileExists => File.Exists(SaveFilePath);


    Vector2Int? _startCell;
    public Vector2Int? StartCell => _startCell;

    Vector2Int? _endCell;
    public Vector2Int? EndCell => _endCell;


    void Awake()
    {
        RegisterBiome(baseBiome);
    }

    void Start()
    {
        if (autoLoadOnStart) CheckSaveFile();
    }

    /// <summary>
    /// Adds a biome to the used-biome list if not already present. Called when a
    /// biome is applied to a cell (by BiomeAssigner or the paint brush).
    /// </summary>
    public void RegisterBiome(BiomeSO biome)
    {
        if (biome == null) return;
        if (!_usedBiomes.Contains(biome))
            _usedBiomes.Add(biome);
    }

    public void SetGrid(CellData[,] cellGrid)
    {
        grid = cellGrid;
        if (grid != null)
        {
            GridWidth  = grid.GetLength(0);
            GridHeight = grid.GetLength(1);
            OnGridReady?.Invoke();
        }
    }


    public CellData GetData(Vector3 worldPos)
    {
        Vector2Int g = WorldToGrid(worldPos);
        return GetData(g.x, g.y);
    }

    public CellData GetData(int gridX, int gridZ)
    {
        if (grid == null) return default;
        int x = Mathf.Clamp(gridX, 0, GridWidth  - 1);
        int z = Mathf.Clamp(gridZ, 0, GridHeight - 1);
        return grid[x, z];
    }


    public bool InBounds(int x, int z)
    {
        return grid != null && x >= 0 && x < GridWidth && z >= 0 && z < GridHeight;
    }

    public Vector2Int WorldToGrid(Vector3 world)
    {
        Vector3 local = world - transform.position;
        int gx = Mathf.RoundToInt((local.x + extentX) / step);
        int gz = Mathf.RoundToInt((local.z + extentZ) / step);
        return new Vector2Int(
            Mathf.Clamp(gx, 0, GridWidth  - 1),
            Mathf.Clamp(gz, 0, GridHeight - 1));
    }

    public Vector3 GridToWorld(Vector2Int cell)
    {
        float wx = -extentX + cell.x * step;
        float wz = -extentZ + cell.y * step;
        return transform.position + new Vector3(wx, 0f, wz);
    }

    public float GetRoundedHeight(int gx, int gz)
    {
        if (grid == null) return 0f;
        int x = Mathf.Clamp(gx, 0, GridWidth  - 1);
        int z = Mathf.Clamp(gz, 0, GridHeight - 1);
        return grid[x, z].roundedHeight;
    }

    public float GetRoundedHeight(Vector3 worldPos)
    {
        Vector2Int g = WorldToGrid(worldPos);
        return GetRoundedHeight(g.x, g.y);
    }

    public float GetRawHeight(int gx, int gz)
    {
        if (grid == null) return 0f;
        int x = Mathf.Clamp(gx, 0, GridWidth  - 1);
        int z = Mathf.Clamp(gz, 0, GridHeight - 1);
        return grid[x, z].rawHeight;
    }

    public float GetRawHeight(Vector3 worldPos)
    {
        Vector2Int g = WorldToGrid(worldPos);
        return GetRawHeight(g.x, g.y);
    }


    public void SetStartCell(Vector2Int cell) => _startCell = cell;
    public void SetEndCell(Vector2Int cell)   => _endCell   = cell;

    public bool RaycastTerrain(Ray ray, out Vector3 hitPoint, float maxDistance = 500f)
    {
        hitPoint = Vector3.zero;
        if (grid == null) return false;

        float stepDist = step * 0.5f;
        Vector3 pos = ray.origin;

        for (float d = 0f; d < maxDistance; d += stepDist)
        {
            pos = ray.GetPoint(d);

            // Check if within terrain bounds
            float lx = pos.x - transform.position.x;
            float lz = pos.z - transform.position.z;
            if (lx < -extentX || lx > extentX || lz < -extentZ || lz > extentZ)
                continue;

            float terrainY = GetRoundedHeight(pos);
            if (pos.y <= terrainY)
            {
                hitPoint = new Vector3(pos.x, terrainY, pos.z);
                return true;
            }
        }

        return false;
    }

    // ── Save / Load ──────────────────────────────────────────────────────

    /// <summary>
    /// Checks for a save file on disk. If present, loads it and fires OnSaveLoaded.
    /// If absent, fires OnSaveCreated so a subscriber can run generation and then call WriteSave().
    /// On IO / parse errors, fires OnSaveFailed with a reason string.
    /// </summary>
    public void CheckSaveFile()
    {
        Debug.Log($"TerrainDataStore: checking for save at {SaveFilePath} (exists={SaveFileExists}).");

        if (!SaveFileExists)
        {
            OnSaveCreated?.Invoke();
            return;
        }

        try
        {
            string json = File.ReadAllText(SaveFilePath);
            SaveData data = JsonUtility.FromJson<SaveData>(json);
            if (data == null)
            {
                OnSaveFailed?.Invoke("Save file parsed to null.");
                return;
            }
            Debug.Log($"TerrainDataStore: parsed save ({json.Length} chars, grid {data.gridWidth}x{data.gridHeight}, cells={(data.cells != null ? data.cells.Length : 0)}).");
            ApplySaveData(data);
            OnSaveLoaded?.Invoke();
        }
        catch (System.Exception e)
        {
            OnSaveFailed?.Invoke(e.Message);
        }
    }

    /// <summary>
    /// Serializes the current grid, settings, and flag positions to disk.
    /// </summary>
    public void WriteSave()
    {
        if (grid == null)
        {
            Debug.LogWarning("TerrainDataStore.WriteSave: grid is null — nothing to save.");
            return;
        }

        try
        {
            SaveData data = BuildSaveData();
            string json = JsonUtility.ToJson(data);
            File.WriteAllText(SaveFilePath, json);
            Debug.Log($"TerrainDataStore: wrote save ({json.Length} chars, {data.gridWidth}x{data.gridHeight} cells) to {SaveFilePath}.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"TerrainDataStore.WriteSave failed: {e}");
            OnSaveFailed?.Invoke(e.Message);
        }
    }

    SaveData BuildSaveData()
    {
        SaveData data = new SaveData
        {
            extentX = extentX,
            extentZ = extentZ,
            step = step,
            roundStep = roundStep,
            seed = seed,
            noiseScale = noiseScale,
            noiseOffset = noiseOffset,
            heightMultiplier = heightMultiplier,
            gridWidth = GridWidth,
            gridHeight = GridHeight,
            hasStart = _startCell.HasValue,
            startX = _startCell?.x ?? 0,
            startZ = _startCell?.y ?? 0,
            hasEnd = _endCell.HasValue,
            endX = _endCell?.x ?? 0,
            endZ = _endCell?.y ?? 0,
        };

        if (grid != null)
        {
            data.cells = new CellDataDto[GridWidth * GridHeight];
            for (int x = 0; x < GridWidth; x++)
            {
                for (int z = 0; z < GridHeight; z++)
                {
                    CellData c = grid[x, z];
                    data.cells[x * GridHeight + z] = new CellDataDto
                    {
                        rawHeight = c.rawHeight,
                        roundedHeight = c.roundedHeight,
                        slopeOutgoing = c.slopeOutgoing,
                        biomeName = c.biome != null ? c.biome.biomeName : null,
                    };
                }
            }
        }

        return data;
    }

    void ApplySaveData(SaveData data)
    {
        // Restore settings
        extentX = data.extentX;
        extentZ = data.extentZ;
        step = data.step;
        roundStep = data.roundStep;
        seed = data.seed;
        noiseScale = data.noiseScale;
        noiseOffset = data.noiseOffset;
        heightMultiplier = data.heightMultiplier;

        // Restore flags
        _startCell = data.hasStart ? new Vector2Int(data.startX, data.startZ) : (Vector2Int?)null;
        _endCell   = data.hasEnd   ? new Vector2Int(data.endX,   data.endZ)   : (Vector2Int?)null;

        // Restore grid
        if (data.cells != null && data.gridWidth > 0 && data.gridHeight > 0)
        {
            Dictionary<string, BiomeSO> lookup = BuildBiomeLookup();
            CellData[,] restored = new CellData[data.gridWidth, data.gridHeight];

            for (int x = 0; x < data.gridWidth; x++)
            {
                for (int z = 0; z < data.gridHeight; z++)
                {
                    CellDataDto dto = data.cells[x * data.gridHeight + z];
                    BiomeSO biome = null;
                    if (dto != null && !string.IsNullOrEmpty(dto.biomeName))
                        lookup.TryGetValue(dto.biomeName, out biome);

                    restored[x, z] = new CellData
                    {
                        rawHeight = dto?.rawHeight ?? 0f,
                        roundedHeight = dto?.roundedHeight ?? 0f,
                        slopeOutgoing = dto?.slopeOutgoing,
                        biome = biome,
                    };

                    RegisterBiome(biome);
                }
            }

            SetGrid(restored);
        }
    }

    /// <summary>
    /// Builds a name -> BiomeSO map from every BiomeSO asset under Resources/Biomes.
    /// Move your BiomeSO assets into Assets/Resources/Biomes/ so they can be loaded
    /// in builds.
    /// </summary>
    Dictionary<string, BiomeSO> BuildBiomeLookup()
    {
        Dictionary<string, BiomeSO> map = new Dictionary<string, BiomeSO>();
        foreach (BiomeSO b in Resources.LoadAll<BiomeSO>("Biomes"))
        {
            if (b != null && !string.IsNullOrEmpty(b.biomeName))
                map[b.biomeName] = b;
        }
        return map;
    }
}
