using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SlopeMap : MonoBehaviour
{
    public TerrainDataStore terrainDataStore;

    // Per-node slope magnitude in degrees [0..90] — used for visualisation
    public float[] SlopeAngles { get; private set; }
    // Raw heights — used for directional slope queries
    public float[] Heights     { get; private set; }
    public int     VertsX      { get; private set; }
    public int     VertsZ      { get; private set; }

    // Magnitude only — how steep is this node (no direction)
    public float GetSlope(int x, int z) => SlopeAngles[z * VertsX + x];

    // Signed slope in degrees from node (x1,z1) → (x2,z2)
    // Positive = uphill, Negative = downhill
    public float GetDirectionalSlope(int x1, int z1, int x2, int z2)
    {
        float h1       = Heights[z1 * VertsX + x1];
        float h2       = Heights[z2 * VertsX + x2];
        float dx       = (x2 - x1) * terrainDataStore.step;
        float dz       = (z2 - z1) * terrainDataStore.step;
        float distance = Mathf.Sqrt(dx * dx + dz * dz);
        return Mathf.Atan2(h2 - h1, distance) * Mathf.Rad2Deg;
    }

    // Generation is driven by MapGenerator — no auto-subscribe or auto-start.

    /// <summary>
    /// Compute slopes from the given grid and bake directional slopes into each cell.
    /// Call this before handing the grid to TerrainDataStore.
    /// </summary>
    public void ComputeAndBake(CellData[,] grid, int width, int height)
    {
        if (terrainDataStore == null) return;

        VertsX = width;
        VertsZ = height;

        Heights     = BuildHeightsFromGrid(grid);
        SlopeAngles = ComputeSlopes(Heights);

        BakeSlopesIntoGrid(grid);
    }

    /// <summary>Build the visual mesh (call after TerrainDataStore has the grid).</summary>
    public void Generate()
    {
        if (terrainDataStore == null || terrainDataStore.grid == null) return;

        VertsX = terrainDataStore.GridWidth;
        VertsZ = terrainDataStore.GridHeight;

        if (Heights == null) Heights = BuildHeightsFromGrid(terrainDataStore.grid);
        if (SlopeAngles == null) SlopeAngles = ComputeSlopes(Heights);

        BuildMesh();
        ApplySlopeTexture(SlopeAngles);
    }

    private void BakeSlopesIntoGrid(CellData[,] grid)
    {
        float stp = terrainDataStore.step;

        for (int x = 0; x < VertsX; x++)
        for (int z = 0; z < VertsZ; z++)
        {
            grid[x, z].slopeOutgoing = new float[8];
            float h = grid[x, z].rawHeight;

            for (int d = 0; d < 8; d++)
            {
                int nx = x + CellData.Directions[d].x;
                int nz = z + CellData.Directions[d].y;

                if (nx < 0 || nx >= VertsX || nz < 0 || nz >= VertsZ)
                {
                    grid[x, z].slopeOutgoing[d] = 0f;
                    continue;
                }

                float nh = grid[nx, nz].rawHeight;
                bool isDiagonal = CellData.Directions[d].x != 0 && CellData.Directions[d].y != 0;
                float dist = isDiagonal ? stp * 1.41421356f : stp;
                grid[x, z].slopeOutgoing[d] = Mathf.Atan2(nh - h, dist) * Mathf.Rad2Deg;
            }
        }
    }

    private float[] BuildHeightsFromGrid(CellData[,] grid)
    {
        var heights = new float[VertsX * VertsZ];

        for (int z = 0; z < VertsZ; z++)
        for (int x = 0; x < VertsX; x++)
            heights[z * VertsX + x] = grid[x, z].rawHeight;

        return heights;
    }

    // Central-difference gradient → slope angle in degrees per node
    private float[] ComputeSlopes(float[] heights)
    {
        var slopes = new float[VertsX * VertsZ];

        for (int z = 0; z < VertsZ; z++)
        for (int x = 0; x < VertsX; x++)
        {
            float h     = heights[z * VertsX + x];
            float left  = x > 0          ? heights[ z      * VertsX + (x - 1)] : h;
            float right = x < VertsX - 1 ? heights[ z      * VertsX + (x + 1)] : h;
            float down  = z > 0          ? heights[(z - 1) * VertsX +  x      ] : h;
            float up    = z < VertsZ - 1 ? heights[(z + 1) * VertsX +  x      ] : h;

            float dX = (right - left) / (2f * terrainDataStore.step);
            float dZ = (up    - down) / (2f * terrainDataStore.step);

            slopes[z * VertsX + x] = Mathf.Atan(Mathf.Sqrt(dX * dX + dZ * dZ)) * Mathf.Rad2Deg;
        }
        return slopes;
    }

    private void BuildMesh()
    {
        int stepsX = VertsX - 1;
        int stepsZ = VertsZ - 1;

        var vertices  = new Vector3[VertsX * VertsZ];
        var uvs       = new Vector2[VertsX * VertsZ];

        for (int z = 0; z < VertsZ; z++)
        for (int x = 0; x < VertsX; x++)
        {
            int i = z * VertsX + x;
            vertices[i] = new Vector3(-terrainDataStore.extentX + x * terrainDataStore.step, 0f, -terrainDataStore.extentZ + z * terrainDataStore.step);
            uvs[i]      = new Vector2((float)x / stepsX, (float)z / stepsZ);
        }

        var triangles = new int[stepsX * stepsZ * 6];
        int t = 0;
        for (int z = 0; z < stepsZ; z++)
        for (int x = 0; x < stepsX; x++)
        {
            int bl = z * VertsX + x;
            triangles[t++] = bl;
            triangles[t++] = bl + VertsX;
            triangles[t++] = bl + 1;
            triangles[t++] = bl + 1;
            triangles[t++] = bl + VertsX;
            triangles[t++] = bl + VertsX + 1;
        }

        var mesh = new Mesh { name = "SlopeMap" };
        if (VertsX * VertsZ > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices  = vertices;
        mesh.uv        = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        GetComponent<MeshFilter>().mesh = mesh;

        var mr     = GetComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (mr.sharedMaterial == null)
            mr.sharedMaterial = new Material(shader);
    }

    private void ApplySlopeTexture(float[] slopes)
    {
        float maxAngle = 0f;
        foreach (float s in slopes)
            if (s > maxAngle) maxAngle = s;
        if (maxAngle < 0.001f) maxAngle = 1f;

        var tex = new Texture2D(VertsX, VertsZ, TextureFormat.RGB24, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp
        };

        var pixels = new Color[slopes.Length];
        for (int i = 0; i < slopes.Length; i++)
        {
            float t = Mathf.Clamp01(slopes[i] / maxAngle);
            pixels[i] = Color.Lerp(Color.white, Color.red, t);
        }

        tex.SetPixels(pixels);
        tex.Apply();

        GetComponent<MeshRenderer>().sharedMaterial.mainTexture = tex;
    }
}
