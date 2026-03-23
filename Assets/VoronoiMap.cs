using UnityEngine;

[RequireComponent(typeof(MapRenderer))]
public class VoronoiMap : MonoBehaviour
{
    [Header("Grid")]
    public int width  = 400;
    public int height = 400;

    [Header("Voronoi")]
    public int unitsPerRegion = 32;   // one seed point per this many cells
    public int seed           = 42;

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

        int regionCount = Mathf.Max(1, (width * height) / unitsPerRegion);

        var seeds     = new Vector2Int[regionCount];
        var seedTypes = new TerrainType[regionCount];
        var types     = new[] { TerrainType.Grass, TerrainType.Dirt, TerrainType.Sand };

        for (int i = 0; i < regionCount; i++)
        {
            seeds[i]     = new Vector2Int(Random.Range(0, width), Random.Range(0, height));
            seedTypes[i] = types[Random.Range(0, types.Length)];
        }

        for (int x = 0; x < width; x++)
        for (int y = 0; y < height; y++)
        {
            int   closest = 0;
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