using UnityEngine;

/// <summary>
/// Builds a single combined mesh on the XZ plane — one quad per cell, vertex colors used for terrain color.
/// Requires a MeshFilter, MeshRenderer, and a shader that reads vertex colors.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class MapRenderer : MonoBehaviour
{
    MeshFilter   mf;
    MeshRenderer mr;
    Mesh         mesh;

    int mapWidth, mapHeight;

    Color[] colors;

    void Awake()
    {
        mf = GetComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();
    }

    public void Render(CellData[,] grid, int width, int height)
    {
        Color[] cellColors = new Color[width * height];
        for (int x = 0; x < width; x++)
        for (int z = 0; z < height; z++)
        {
            var cell = grid[x, z];
            cellColors[x * height + z] = cell.biome != null ? cell.biome.color : Color.white;
        }

        RenderColors(cellColors, width, height);
    }

    void RenderColors(Color[] cellColors, int width, int height)
    {
        if (mf == null) mf = GetComponent<MeshFilter>();
        if (mr == null) mr = GetComponent<MeshRenderer>();
        mr.material = CreateVertexColorMaterial();

        mapWidth  = width;
        mapHeight = height;

        int cellCount = width * height;

        Vector3[] verts   = new Vector3[cellCount * 4];
        int[]     tris    = new int    [cellCount * 6];
        colors            = new Color  [cellCount * 4];

        int vi = 0;
        int ti = 0;

        for (int x = 0; x < width; x++)
        for (int z = 0; z < height; z++)
        {
            float wx = x - width  * 0.5f;
            float wz = z - height * 0.5f;

            verts[vi + 0] = new Vector3(wx,       0, wz      );
            verts[vi + 1] = new Vector3(wx + 1f,  0, wz      );
            verts[vi + 2] = new Vector3(wx + 1f,  0, wz + 1f );
            verts[vi + 3] = new Vector3(wx,        0, wz + 1f );

            Color c = cellColors[x * height + z];
            colors[vi + 0] = c;
            colors[vi + 1] = c;
            colors[vi + 2] = c;
            colors[vi + 3] = c;

            tris[ti + 0] = vi + 0;
            tris[ti + 1] = vi + 2;
            tris[ti + 2] = vi + 1;
            tris[ti + 3] = vi + 0;
            tris[ti + 4] = vi + 3;
            tris[ti + 5] = vi + 2;

            vi += 4;
            ti += 6;
        }

        mesh = new Mesh { name = "TerrainMap" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices  = verts;
        mesh.triangles = tris;
        mesh.colors    = colors;
        mesh.RecalculateNormals();

        mf.mesh = mesh;
    }

    /// <summary>Update a single cell's color without rebuilding the whole mesh.</summary>
    public void RefreshCell(CellData cell, int x, int z)
    {
        if (mesh == null || colors == null) return;

        int vi = CellVertexIndex(x, z);
        Color c = cell.biome != null ? cell.biome.color : Color.white;
        colors[vi + 0] = c;
        colors[vi + 1] = c;
        colors[vi + 2] = c;
        colors[vi + 3] = c;

        mesh.colors = colors;
    }

    int CellVertexIndex(int x, int z)
    {
        return (x * mapHeight + z) * 4;
    }

    static Material CreateVertexColorMaterial()
    {
        string[] candidates =
        {
            "Universal Render Pipeline/Particles/Unlit",
            "Sprites/Default",
            "Legacy Shaders/Particles/Alpha Blended",
            "Particles/Standard Unlit",
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
