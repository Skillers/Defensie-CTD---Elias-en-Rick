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

    [HideInInspector] public CellData[,] grid;

    public int GridWidth  { get; private set; }
    public int GridHeight { get; private set; }

    public event System.Action OnGridReady;

    /// <summary>Assign a fully baked grid and notify listeners.</summary>
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

    // ── Queries ──────────────────────────────────────────────────────────

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

    // ── Grid / World conversion ──────────────────────────────────────────

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

    /// <summary>Returns the rounded height at the given grid coordinate.</summary>
    public float GetHeight(int gx, int gz)
    {
        if (grid == null) return 0f;
        int x = Mathf.Clamp(gx, 0, GridWidth  - 1);
        int z = Mathf.Clamp(gz, 0, GridHeight - 1);
        return grid[x, z].roundedHeight;
    }

    /// <summary>Returns the rounded height at a world position.</summary>
    public float GetHeight(Vector3 worldPos)
    {
        Vector2Int g = WorldToGrid(worldPos);
        return GetHeight(g.x, g.y);
    }

    /// <summary>
    /// Raycast against the terrain heightmap. Steps the ray across the XZ grid
    /// and finds where the ray drops below the terrain surface.
    /// Returns true if hit, with hitPoint on the terrain surface.
    /// </summary>
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

            float terrainY = GetHeight(pos);
            if (pos.y <= terrainY)
            {
                hitPoint = new Vector3(pos.x, terrainY, pos.z);
                return true;
            }
        }

        return false;
    }
}
