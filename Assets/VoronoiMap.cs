using UnityEngine;

[RequireComponent(typeof(MapRenderer))]
public class VoronoiMap : MonoBehaviour
{
    [Header("Grid")]
    public int width  = 400;
    public int height = 400;

    [Header("Voronoi")]
    public int regionsX = 20;
    public int regionsZ = 20;
    public int seed     = 42;

    [HideInInspector] public TerrainCell[,] grid;

    MapRenderer mapRenderer;

    void Awake()
    {
        mapRenderer = GetComponent<MapRenderer>();
        Generate();
    }

    public void Generate()
    {
        grid = new TerrainCell[width, height];
        GenerateVoronoi();
        mapRenderer.Render(grid, width, height);
    }

    public bool InBounds(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;

    public Vector2Int WorldToGrid(Vector3 world)
    {
        Vector3 local = world - transform.position;
        int gx = Mathf.FloorToInt(local.x + width  * 0.5f);
        int gz = Mathf.FloorToInt(local.z + height * 0.5f);
        return new Vector2Int(Mathf.Clamp(gx, 0, width - 1), Mathf.Clamp(gz, 0, height - 1));
    }

    public Vector3 GridToWorld(Vector2Int cell)
    {
        float wx = cell.x - width  * 0.5f + 0.5f;
        float wz = cell.y - height * 0.5f + 0.5f;
        return transform.position + new Vector3(wx, 0f, wz);
    }

    void GenerateVoronoi()
    {
        Random.InitState(seed);

        int nx         = Mathf.Max(1, regionsX);
        int nz         = Mathf.Max(1, regionsZ);
        int regionCount = nx * nz;
        int cellW      = Mathf.Max(1, width  / nx);
        int cellH      = Mathf.Max(1, height / nz);

        var seeds     = new Vector2Int[regionCount];
        var seedTypes = new TerrainType[regionCount];
        var types     = new[] { TerrainType.Grass, TerrainType.Dirt, TerrainType.Sand };

        // One seed placed randomly within each grid cell
        for (int rx = 0; rx < nx; rx++)
        for (int ry = 0; ry < nz; ry++)
        {
            int i    = rx * nz + ry;
            int sx   = rx * cellW + Random.Range(0, cellW);
            int sy   = ry * cellH + Random.Range(0, cellH);
            seeds[i]     = new Vector2Int(Mathf.Clamp(sx, 0, width - 1), Mathf.Clamp(sy, 0, height - 1));
            seedTypes[i] = types[Random.Range(0, types.Length)];
        }

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            int   closest  = 0;
            float bestDist = float.MaxValue;

                for (int i = 0; i < regionCount; i++)
            {
                float d = Vector2.Distance(new Vector2(x, y), seeds[i]);
                if (d < bestDist) { bestDist = d; closest = i; }
            }

            grid[x, y] = TerrainCell.Make(seedTypes[closest]);
        }
    }
}