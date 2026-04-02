using UnityEngine;

/// <summary>
/// Builds a single combined mesh on the XZ plane — one quad per cell, vertex colors used for terrain color.
/// Requires a MeshFilter, MeshRenderer, and a shader that reads vertex colors (e.g. Particles/Standard Unlit or a custom one).
/// We use a simple approach: one sub-mesh per terrain color is NOT needed — we bake color into vertex colors.
/// Attach this to the Map GameObject directly (no child Quad needed).
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class MapRenderer : MonoBehaviour
{
    MeshFilter   mf;
    MeshRenderer mr;
    Mesh         mesh;

    int mapWidth, mapHeight;

    // Vertex color arrays — kept so we can update single cells cheaply
    Color[] colors;

    void Awake()
    {
        mf = GetComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();
    }

    public void Render(TerrainCell[,] grid, int width, int height)
    {
        Color[] cellColors = new Color[width * height];
        for (int x = 0; x < width; x++)
        for (int z = 0; z < height; z++)
            cellColors[x * height + z] = grid[x, z].color;

        RenderColors(cellColors, width, height);
    }

    public void Render(MilitaryTerrainCell[,] grid, int width, int height)
    {
        Color[] cellColors = new Color[width * height];
        for (int x = 0; x < width; x++)
        for (int z = 0; z < height; z++)
            cellColors[x * height + z] = grid[x, z].color;

        RenderColors(cellColors, width, height);
    }

    void RenderColors(Color[] cellColors, int width, int height)
    {
        // Lazy-init in case Awake order on the same GameObject was wrong
        if (mf == null) mf = GetComponent<MeshFilter>();
        if (mr == null) mr = GetComponent<MeshRenderer>();
        mr.material = CreateVertexColorMaterial();

        mapWidth  = width;
        mapHeight = height;

        int cellCount = width * height;

        // Each cell = 4 verts, 2 triangles (6 indices)
        Vector3[] verts   = new Vector3[cellCount * 4];
        int[]     tris    = new int    [cellCount * 6];
        colors            = new Color  [cellCount * 4];

        int vi = 0; // vertex index
        int ti = 0; // triangle index

        for (int x = 0; x < width; x++)
        for (int z = 0; z < height; z++)
        {
            // Cell bottom-left corner in world space (centred on origin)
            float wx = x - width  * 0.5f;
            float wz = z - height * 0.5f;

            // 4 corners of the quad (XZ plane, Y=0)
            verts[vi + 0] = new Vector3(wx,       0, wz      );
            verts[vi + 1] = new Vector3(wx + 1f,  0, wz      );
            verts[vi + 2] = new Vector3(wx + 1f,  0, wz + 1f );
            verts[vi + 3] = new Vector3(wx,        0, wz + 1f );

            Color c = cellColors[x * height + z];
            colors[vi + 0] = c;
            colors[vi + 1] = c;
            colors[vi + 2] = c;
            colors[vi + 3] = c;

            // Two triangles
            tris[ti + 0] = vi + 0;
            tris[ti + 1] = vi + 2;
            tris[ti + 2] = vi + 1;
            tris[ti + 3] = vi + 0;
            tris[ti + 4] = vi + 3;
            tris[ti + 5] = vi + 2;

            vi += 4;
            ti += 6;
        }

        mesh = new Mesh { name = "VoronoiMap" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // supports > 65k verts
        mesh.vertices  = verts;
        mesh.triangles = tris;
        mesh.colors    = colors;
        mesh.RecalculateNormals();

        mf.mesh = mesh;
    }

    /// <summary>Update a single cell's color without rebuilding the whole mesh.</summary>
    public void RefreshCell(TerrainCell cell, int x, int z)
    {
        if (mesh == null || colors == null) return;

        int vi = CellVertexIndex(x, z);
        Color c = cell.color;
        colors[vi + 0] = c;
        colors[vi + 1] = c;
        colors[vi + 2] = c;
        colors[vi + 3] = c;

        mesh.colors = colors;
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    int CellVertexIndex(int x, int z)
    {
        // Must match the iteration order in Render()
        return (x * mapHeight + z) * 4;
    }

    static Material CreateVertexColorMaterial()
    {
        // Try shaders in order: URP → Built-in
        string[] candidates =
        {
            "Universal Render Pipeline/Particles/Unlit",  // URP
            "Sprites/Default",                             // Built-in (reads vertex colors)
            "Legacy Shaders/Particles/Alpha Blended",      // Built-in legacy
            "Particles/Standard Unlit",                    // older Built-in
        };

        foreach (var name in candidates)
        {
            Shader s = Shader.Find(name);
            if (s != null)
            {
                Debug.Log($"MapRenderer: using shader '{name}'");
                return new Material(s);
            }
        }

        Debug.LogError("MapRenderer: no vertex-color shader found. " +
                       "Create a material that reads vertex colors and assign it manually.");
        return new Material(Shader.Find("Standard"));
    }
}