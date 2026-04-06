using UnityEngine;

/// <summary>
/// Central data store for per-cell terrain information.
/// Holds a pre-computed CellData grid with height, slope, and biome per cell.
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

    /// <summary>The pre-computed cell grid. Set via SetGrid then call BakeData.</summary>
    [HideInInspector] public CellData[,] grid;

    public int GridWidth  { get; private set; }
    public int GridHeight { get; private set; }

    public event System.Action OnGridReady;

    private const float RoundStep = 0.25f;
    private const float NoiseStep = 0.5f;

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
            BakeData();
    }

    /// <summary>Assign a grid (with biomes already set) then bake height/slope data into it.</summary>
    public void SetGrid(CellData[,] cellGrid)
    {
        grid = cellGrid;
        if (grid != null)
        {
            GridWidth  = grid.GetLength(0);
            GridHeight = grid.GetLength(1);
            BakeData();
        }
    }

    /// <summary>Fills rawHeight, roundedHeight, and slopeOutgoing for every cell from the noise/slope sources.</summary>
    public void BakeData()
    {
        if (grid == null || config == null) return;

        GridWidth  = grid.GetLength(0);
        GridHeight = grid.GetLength(1);

        // Bake heights
        for (int x = 0; x < GridWidth; x++)
        for (int z = 0; z < GridHeight; z++)
        {
            float raw = SampleRawHeight(x, z);
            grid[x, z].rawHeight     = raw;
            grid[x, z].roundedHeight = Mathf.Round(raw / RoundStep) * RoundStep;
        }

        // Bake outgoing slopes (needs heights filled first)
        for (int x = 0; x < GridWidth; x++)
        for (int z = 0; z < GridHeight; z++)
        {
            grid[x, z].slopeOutgoing = new float[8];
            float h = grid[x, z].rawHeight;

            for (int d = 0; d < 8; d++)
            {
                int nx = x + CellData.Directions[d].x;
                int nz = z + CellData.Directions[d].y;

                if (nx < 0 || nx >= GridWidth || nz < 0 || nz >= GridHeight)
                {
                    grid[x, z].slopeOutgoing[d] = 0f;
                    continue;
                }

                float nh = grid[nx, nz].rawHeight;
                bool isDiagonal = CellData.Directions[d].x != 0 && CellData.Directions[d].y != 0;
                float dist = isDiagonal ? 1.41421356f : 1f; // cells are 1 unit apart
                grid[x, z].slopeOutgoing[d] = Mathf.Atan2(nh - h, dist) * Mathf.Rad2Deg;
            }
        }

        OnGridReady?.Invoke();
    }

    float SampleRawHeight(int gridX, int gridZ)
    {
        if (noisePlane == null || noisePlane.NoiseValues == null || config == null)
            return 0f;

        float worldX = gridX - GridWidth  * 0.5f + 0.5f;
        float worldZ = gridZ - GridHeight * 0.5f + 0.5f;
        int xi = Mathf.Clamp(Mathf.RoundToInt((worldX + config.extentX) / NoiseStep), 0, noisePlane.VertsX - 1);
        int zi = Mathf.Clamp(Mathf.RoundToInt((worldZ + config.extentZ) / NoiseStep), 0, noisePlane.VertsZ - 1);
        return noisePlane.GetValue(xi, zi) * config.heightMultiplier;
    }

    // ── Queries ──────────────────────────────────────────────────────────

    /// <summary>Returns the CellData at the given world position.</summary>
    public CellData GetData(Vector3 worldPos)
    {
        Vector2Int g = WorldToGrid(worldPos);
        return GetData(g.x, g.y);
    }

    /// <summary>Returns the CellData at the given grid coordinate.</summary>
    public CellData GetData(int gridX, int gridZ)
    {
        if (grid == null) return default;
        int x = Mathf.Clamp(gridX, 0, GridWidth  - 1);
        int z = Mathf.Clamp(gridZ, 0, GridHeight - 1);
        return grid[x, z];
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
