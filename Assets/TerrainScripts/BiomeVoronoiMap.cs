using UnityEngine;

/// <summary>
/// Option A — Voronoi map using military terrain types.
/// Terrain type per region is chosen based on the height at the seed position:
///   - High elevation  → snow/rocky types
///   - Low/flat areas  → dirt/mud types
///   - Everything else → grass-dominated with occasional forest/rocky
/// </summary>
public class BiomeVoronoiMap : MonoBehaviour
{
    [Header("Config")]
    public TerrainConfigHolder configHolder;
    private PlaneConfig config => configHolder?.config;

    [Header("Height Source")]
    [Tooltip("Used to sample elevation at each Voronoi seed. Assign the PerlinNoisePlane in the scene.")]
    public PerlinNoisePlane noisePlane;

    [Header("Voronoi")]
    public int regionsX = 20;
    public int regionsZ = 20;

    [Header("Height Thresholds (normalized 0-1)")]
    [Tooltip("Noise value above which snow types can appear.")]
    [Range(0f, 1f)] public float snowMinHeight = 0.7f;
    [Tooltip("Noise value below which dirt/mud types can appear.")]
    [Range(0f, 1f)] public float dirtMaxHeight = 0.35f;

    [HideInInspector] public MilitaryTerrainCell[,] grid;

    public event System.Action OnGenerated;

    MapRenderer mapRenderer;
    PerlinNoisePlane _subscribedTo;

    void OnEnable()  => Subscribe();
    void OnDisable() => Unsubscribe();

    void Start()
    {
        Subscribe();
        if (noisePlane != null && noisePlane.NoiseValues != null && noisePlane.NoiseValues.Length > 0)
            Generate();
    }

    void Subscribe()
    {
        if (_subscribedTo == noisePlane) return;
        Unsubscribe();
        if (noisePlane != null) { noisePlane.OnGenerated += Generate; _subscribedTo = noisePlane; }
    }

    void Unsubscribe()
    {
        if (_subscribedTo != null) { _subscribedTo.OnGenerated -= Generate; _subscribedTo = null; }
    }

    public void Generate()
    {
        if (mapRenderer == null) mapRenderer = GetComponent<MapRenderer>();
        if (configHolder == null) { Debug.LogError("BiomeVoronoiMap: no TerrainConfigHolder assigned."); return; }
        if (config == null)       { Debug.LogError("BiomeVoronoiMap: TerrainConfigHolder has no PlaneConfig."); return; }

        int width  = Mathf.RoundToInt(config.extentX * 2f);
        int height = Mathf.RoundToInt(config.extentZ * 2f);
        grid = new MilitaryTerrainCell[width, height];
        GenerateVoronoi(width, height);
        mapRenderer?.Render(grid, width, height);
        OnGenerated?.Invoke();
    }

    public bool InBounds(int x, int y)
    {
        if (grid == null) return false;
        return x >= 0 && x < grid.GetLength(0) && y >= 0 && y < grid.GetLength(1);
    }

    public Vector2Int WorldToGrid(Vector3 world)
    {
        int w = grid.GetLength(0), h = grid.GetLength(1);
        Vector3 local = world - transform.position;
        int gx = Mathf.FloorToInt(local.x + w * 0.5f);
        int gz = Mathf.FloorToInt(local.z + h * 0.5f);
        return new Vector2Int(Mathf.Clamp(gx, 0, w - 1), Mathf.Clamp(gz, 0, h - 1));
    }

    public Vector3 GridToWorld(Vector2Int cell)
    {
        int w = grid.GetLength(0), h = grid.GetLength(1);
        float wx = cell.x - w * 0.5f + 0.5f;
        float wz = cell.y - h * 0.5f + 0.5f;
        return transform.position + new Vector3(wx, 0f, wz);
    }

    void GenerateVoronoi(int width, int height)
    {
        Random.InitState(config.seed);

        // Pre-compute actual min/max of the noise map so thresholds work
        // against the real height range rather than raw Perlin values
        float noiseMin = 1f, noiseMax = 0f;
        if (noisePlane != null && noisePlane.NoiseValues != null)
        {
            foreach (float v in noisePlane.NoiseValues)
            {
                if (v < noiseMin) noiseMin = v;
                if (v > noiseMax) noiseMax = v;
            }
        }

        int nx          = Mathf.Max(1, regionsX);
        int nz          = Mathf.Max(1, regionsZ);
        int regionCount = nx * nz;
        int cellW       = Mathf.Max(1, width  / nx);
        int cellH       = Mathf.Max(1, height / nz);

        var seeds     = new Vector2Int[regionCount];
        var seedTypes = new MilitaryTerrainType[regionCount];

        for (int rx = 0; rx < nx; rx++)
        for (int rz = 0; rz < nz; rz++)
        {
            int i  = rx * nz + rz;
            int sx = Mathf.Clamp(rx * cellW + Random.Range(0, cellW), 0, width  - 1);
            int sz = Mathf.Clamp(rz * cellH + Random.Range(0, cellH), 0, height - 1);
            seeds[i]     = new Vector2Int(sx, sz);
            seedTypes[i] = PickTerrainType(sx, sz, width, height, noiseMin, noiseMax);
        }

        for (int x = 0; x < width; x++)
        for (int z = 0; z < height; z++)
        {
            int   closest  = 0;
            float bestDist = float.MaxValue;

            for (int i = 0; i < regionCount; i++)
            {
                float d = Vector2.Distance(new Vector2(x, z), seeds[i]);
                if (d < bestDist) { bestDist = d; closest = i; }
            }

            grid[x, z] = MilitaryTerrainCell.Make(seedTypes[closest]);
        }
    }

    MilitaryTerrainType PickTerrainType(int gridX, int gridZ, int width, int height, float noiseMin, float noiseMax)
    {
        float h = SampleHeight(gridX, gridZ, width, height, noiseMin, noiseMax);

        if (h >= snowMinHeight)
        {
            float t = Mathf.Clamp01((h - snowMinHeight) / (1f - snowMinHeight));
            return WeightedPick(new[]
            {
                (MilitaryTerrainType.SnowyHill,   L(30, 55, t)),
                (MilitaryTerrainType.Snow,         L(15, 25, t)),
                (MilitaryTerrainType.RockyTerrain, L(30, 10, t)),
                (MilitaryTerrainType.DenseForest,  L(25,  5, t)),
            });
        }

        if (h <= dirtMaxHeight)
        {
            // t=0 at threshold → t=1 at min height
            // At t=0: Plains 20%, Mud 40%, Rock 40%
            // At t=1: Plains 5%,  Mud 70%, Rock 25%
            float t = Mathf.Clamp01((dirtMaxHeight - h) / Mathf.Max(dirtMaxHeight, 0.001f));
            return WeightedPick(new[]
            {
                (MilitaryTerrainType.GrassyPlain,  L(25,  5, t)),
                (MilitaryTerrainType.Mud,           L(45, 70, t)),
                (MilitaryTerrainType.RockyTerrain,  L(30, 20, t)),
            });
        }

        // Mid elevation — grass dominant
        return WeightedPick(new[]
        {
            (MilitaryTerrainType.Grass,        6),
            (MilitaryTerrainType.GrassyPlain,  4),
            (MilitaryTerrainType.DenseForest,  2),
            (MilitaryTerrainType.RockyTerrain, 1),
        });
    }

    static int L(float a, float b, float t) => Mathf.Max(0, Mathf.RoundToInt(Mathf.Lerp(a, b, t)));

    // Returns the noise value normalized to [0-1] relative to the actual min/max of the map.
    // Falls back to 0.5 if no noise plane is assigned.
    float SampleHeight(int gridX, int gridZ, int width, int height, float noiseMin, float noiseMax)
    {
        if (noisePlane == null || noisePlane.NoiseValues == null) return 0.5f;

        float worldX = gridX - width  * 0.5f + 0.5f;
        float worldZ = gridZ - height * 0.5f + 0.5f;
        int xi = Mathf.Clamp(Mathf.RoundToInt((worldX + config.extentX) / 0.5f), 0, noisePlane.VertsX - 1);
        int zi = Mathf.Clamp(Mathf.RoundToInt((worldZ + config.extentZ) / 0.5f), 0, noisePlane.VertsZ - 1);

        float raw   = noisePlane.GetValue(xi, zi);
        float range = noiseMax - noiseMin;
        return range > 0.001f ? (raw - noiseMin) / range : 0.5f;
    }

    static MilitaryTerrainType WeightedPick((MilitaryTerrainType type, int weight)[] options)
    {
        int total = 0;
        foreach (var o in options) total += o.weight;
        if (total <= 0) return options[0].type;
        int roll = Random.Range(0, total);
        int acc  = 0;
        foreach (var o in options)
        {
            acc += o.weight;
            if (roll < acc) return o.type;
        }
        return options[0].type;
    }
}
