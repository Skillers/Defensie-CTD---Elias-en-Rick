using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class MarchingCubesTerrain : MonoBehaviour
{
    public TerrainDataStore terrainDataStore;

    // Cached generation state
    Dictionary<Vector3Int, GameObject> _chunks = new Dictionary<Vector3Int, GameObject>();
    float[] _density;
    int     _vertsX, _gridY, _vertsZ;
    float   _minY, _extX, _extZ, _stp, _rs;
    Material _sharedMat;

    // Async chunk rebuild queue
    readonly HashSet<Vector3Int> _rebuildQueue = new HashSet<Vector3Int>();
    [Tooltip("Max milliseconds spent rebuilding queued chunks per frame.")]
    public float msPerFrame = 8f;

    // Generation is driven by MapGenerator — no auto-subscribe or auto-start.

    [ContextMenu("Regenerate")]
    public void Generate()
    {
        if (terrainDataStore == null) { Debug.LogError("MarchingCubesTerrain: no TerrainDataStore assigned."); return; }
        if (terrainDataStore.grid == null) { Debug.LogWarning("MarchingCubesTerrain: waiting for grid data."); return; }

        DestroyChunks();

        _vertsX = terrainDataStore.GridWidth;
        _vertsZ = terrainDataStore.GridHeight;
        _extX   = terrainDataStore.extentX;
        _extZ   = terrainDataStore.extentZ;
        _stp    = terrainDataStore.step;
        _rs     = terrainDataStore.roundStep;
        float heightMult = terrainDataStore.heightMultiplier;

        // Bake rounded heights
        for (int x = 0; x < _vertsX; x++)
        for (int z = 0; z < _vertsZ; z++)
            terrainDataStore.grid[x, z].roundedHeight = Mathf.Round(terrainDataStore.grid[x, z].rawHeight / _rs) * _rs;

        _minY  = -_stp;
        float maxY = Mathf.Round(heightMult / _rs) * _rs + _stp;
        _gridY = Mathf.Max(2, Mathf.RoundToInt((maxY - _minY) / _stp) + 1);

        BuildDensityField();

        // Always use the vertex color shader
        var vcShader = Shader.Find("Custom/VertexColor");
        if (vcShader != null)
        {
            _sharedMat = new Material(vcShader);
            Debug.Log("MarchingCubesTerrain: using Custom/VertexColor shader.");
        }
        else
        {
            Debug.LogError("MarchingCubesTerrain: Custom/VertexColor shader NOT FOUND — colors won't show!");
            var fallback = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _sharedMat = new Material(fallback);
        }
        GetComponent<MeshRenderer>().sharedMaterial = _sharedMat;

        // Build all chunks
        int chunk = MarchingTables.ChunkSize;
        for (int cz = 0; cz < _vertsZ - 1; cz += chunk)
        for (int cy = 0; cy < _gridY  - 1; cy += chunk)
        for (int cx = 0; cx < _vertsX - 1; cx += chunk)
            BuildChunk(new Vector3Int(cx, cy, cz));
    }

    /// <summary>
    /// Rebuild only the chunks that overlap the given grid region.
    /// Call after modifying rawHeight in the grid.
    /// </summary>
    public void RebuildRegion(int gxMin, int gzMin, int gxMax, int gzMax)
    {
        if (_density == null) return;

        // Re-bake rounded heights for affected cells (with 1-cell border)
        int xStart = Mathf.Max(0, gxMin - 1);
        int zStart = Mathf.Max(0, gzMin - 1);
        int xEnd   = Mathf.Min(_vertsX - 1, gxMax + 1);
        int zEnd   = Mathf.Min(_vertsZ - 1, gzMax + 1);

        for (int x = xStart; x <= xEnd; x++)
        for (int z = zStart; z <= zEnd; z++)
        {
            terrainDataStore.grid[x, z].roundedHeight =
                Mathf.Round(terrainDataStore.grid[x, z].rawHeight / _rs) * _rs;

            // Update density column
            for (int y = 0; y < _gridY; y++)
            {
                float worldY = _minY + y * _stp;
                _density[x + _vertsX * (y + _gridY * z)] =
                    terrainDataStore.grid[x, z].roundedHeight - worldY;
            }
        }

        // Find which chunks overlap the region
        int chunk = MarchingTables.ChunkSize;
        int cxMin = (xStart / chunk) * chunk;
        int czMin = (zStart / chunk) * chunk;
        int cxMax = (xEnd   / chunk) * chunk;
        int czMax = (zEnd   / chunk) * chunk;

        for (int cz = czMin; cz <= czMax; cz += chunk)
        for (int cx = cxMin; cx <= cxMax; cx += chunk)
        for (int cy = 0; cy < _gridY - 1; cy += chunk)
            _rebuildQueue.Add(new Vector3Int(cx, cy, cz));
    }

    /// <summary>
    /// Recolor only the vertex colors of chunks overlapping the region.
    /// Much cheaper than rebuilding — use for biome painting.
    /// </summary>
    public void RecolorRegion(int gxMin, int gzMin, int gxMax, int gzMax)
    {
        int chunk = MarchingTables.ChunkSize;
        int cxMin = (Mathf.Max(0, gxMin) / chunk) * chunk;
        int czMin = (Mathf.Max(0, gzMin) / chunk) * chunk;
        int cxMax = (Mathf.Min(_vertsX - 1, gxMax) / chunk) * chunk;
        int czMax = (Mathf.Min(_vertsZ - 1, gzMax) / chunk) * chunk;

        for (int cz = czMin; cz <= czMax; cz += chunk)
        for (int cx = cxMin; cx <= cxMax; cx += chunk)
        for (int cy = 0; cy < _gridY - 1; cy += chunk)
        {
            var key = new Vector3Int(cx, cy, cz);
            if (!_chunks.TryGetValue(key, out var chunkGO)) continue;
            if (chunkGO == null) continue;

            var mesh = chunkGO.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;

            ApplyBiomeColors(mesh, _extX, _extZ);
        }
    }

    void LateUpdate()
    {
        if (_rebuildQueue.Count == 0) return;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var iter = new List<Vector3Int>(_rebuildQueue);

        foreach (var key in iter)
        {
            BuildChunk(key);
            _rebuildQueue.Remove(key);

            if (sw.Elapsed.TotalMilliseconds >= msPerFrame)
                break;
        }
    }

    void BuildDensityField()
    {
        _density = new float[_vertsX * _gridY * _vertsZ];
        for (int z = 0; z < _vertsZ; z++)
        for (int y = 0; y < _gridY;  y++)
        for (int x = 0; x < _vertsX; x++)
        {
            float rounded = terrainDataStore.grid[x, z].roundedHeight;
            float worldY  = _minY + y * _stp;
            _density[x + _vertsX * (y + _gridY * z)] = rounded - worldY;
        }
    }

    void BuildChunk(Vector3Int key)
    {
        // Destroy existing chunk at this key
        if (_chunks.TryGetValue(key, out var old))
        {
            if (Application.isPlaying) Destroy(old);
            else DestroyImmediate(old);
            _chunks.Remove(key);
        }

        int chunk = MarchingTables.ChunkSize;
        int cx = key.x, cy = key.y, cz = key.z;

        var verts = new List<Vector3>();
        var tris  = new List<int>();

        int xEnd = Mathf.Min(cx + chunk, _vertsX - 1);
        int yEnd = Mathf.Min(cy + chunk, _gridY  - 1);
        int zEnd = Mathf.Min(cz + chunk, _vertsZ - 1);

        for (int z = cz; z < zEnd; z++)
        for (int y = cy; y < yEnd; y++)
        for (int x = cx; x < xEnd; x++)
        {
            float wx = -_extX + x * _stp;
            float wy =  _minY + y * _stp;
            float wz = -_extZ + z * _stp;

            var corners = new Vector3[8]
            {
                new Vector3(wx,        wy,        wz       ),
                new Vector3(wx + _stp, wy,        wz       ),
                new Vector3(wx + _stp, wy,        wz + _stp),
                new Vector3(wx,        wy,        wz + _stp),
                new Vector3(wx,        wy + _stp, wz       ),
                new Vector3(wx + _stp, wy + _stp, wz       ),
                new Vector3(wx + _stp, wy + _stp, wz + _stp),
                new Vector3(wx,        wy + _stp, wz + _stp),
            };

            var cube = new float[8]
            {
                _density[ x    + _vertsX * ( y    + _gridY *  z   )],
                _density[(x+1) + _vertsX * ( y    + _gridY *  z   )],
                _density[(x+1) + _vertsX * ( y    + _gridY * (z+1))],
                _density[ x    + _vertsX * ( y    + _gridY * (z+1))],
                _density[ x    + _vertsX * ((y+1) + _gridY *  z   )],
                _density[(x+1) + _vertsX * ((y+1) + _gridY *  z   )],
                _density[(x+1) + _vertsX * ((y+1) + _gridY * (z+1))],
                _density[ x    + _vertsX * ((y+1) + _gridY * (z+1))],
            };

            MarchCube(cube, corners, verts, tris);
        }

        if (verts.Count == 0) return;

        var mesh = new Mesh { name = $"MCChunk_{cx}_{cy}_{cz}" };
        mesh.MarkDynamic();
        if (verts.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();

        ApplyBiomeColors(mesh, _extX, _extZ);

        var chunkGO = new GameObject(mesh.name);
        chunkGO.transform.SetParent(transform, false);
        chunkGO.AddComponent<MeshFilter>().sharedMesh = mesh;
        chunkGO.AddComponent<MeshRenderer>().sharedMaterial = _sharedMat;

        var mc = chunkGO.AddComponent<MeshCollider>();
        mc.cookingOptions = MeshColliderCookingOptions.CookForFasterSimulation
                          | MeshColliderCookingOptions.WeldColocatedVertices
                          | MeshColliderCookingOptions.UseFastMidphase;
        mc.sharedMesh = mesh;

        _chunks[key] = chunkGO;
    }

    void DestroyChunks()
    {
        foreach (var kvp in _chunks)
        {
            if (kvp.Value != null)
            {
                if (Application.isPlaying) Destroy(kvp.Value);
                else DestroyImmediate(kvp.Value);
            }
        }
        _chunks.Clear();

        // Also clean any orphan children
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
    }

    private void ApplyBiomeColors(Mesh mesh, float extX, float extZ)
    {
        if (terrainDataStore == null || terrainDataStore.grid == null)
        {
            Debug.LogWarning("ApplyBiomeColors: no grid data.");
            return;
        }

        var biomeGrid = terrainDataStore.grid;
        var vertices  = mesh.vertices;
        var colors    = new Color[vertices.Length];
        int gridW     = biomeGrid.GetLength(0);
        int gridH     = biomeGrid.GetLength(1);

        int nullCount = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            int gx = Mathf.Clamp(Mathf.RoundToInt((vertices[i].x + extX) / terrainDataStore.step), 0, gridW - 1);
            int gz = Mathf.Clamp(Mathf.RoundToInt((vertices[i].z + extZ) / terrainDataStore.step), 0, gridH - 1);
            var cell = biomeGrid[gx, gz];
            if (cell.biome == null) nullCount++;
            colors[i] = cell.biome != null ? cell.biome.color : Color.magenta; // magenta = missing biome
        }

        mesh.colors = colors;

        if (nullCount > 0)
            Debug.LogWarning($"ApplyBiomeColors: {nullCount}/{vertices.Length} verts have no biome (showing magenta).");
    }

    // -------------------------------------------------------------------------
    // Core marching cubes for a single cube cell
    // -------------------------------------------------------------------------
    private static void MarchCube(float[] cube, Vector3[] c, List<Vector3> verts, List<int> tris)
    {
        int idx = 0;
        for (int i = 0; i < 8; i++)
            if (cube[i] > 0f) idx |= (1 << i);

        int edgeMask = MarchingTables.EdgeTable[idx];
        if (edgeMask == 0) return;

        var ev = new Vector3[12];
        for (int e = 0; e < 12; e++)
        {
            if ((edgeMask & (1 << e)) == 0) continue;
            int a = MarchingTables.EdgeVertices[e, 0];
            int b = MarchingTables.EdgeVertices[e, 1];
            ev[e] = Mid(c[a], c[b], cube[a], cube[b]);
        }

        for (int i = 0; MarchingTables.TriTable[idx, i] != -1; i += 3)
        {
            Vector3 v0 = ev[MarchingTables.TriTable[idx, i    ]];
            Vector3 v1 = ev[MarchingTables.TriTable[idx, i + 1]];
            Vector3 v2 = ev[MarchingTables.TriTable[idx, i + 2]];

            // Skip degenerate triangles
            if (Vector3.Cross(v1 - v0, v2 - v0).sqrMagnitude < 1e-10f) continue;

            int b = verts.Count;
            verts.Add(v0); verts.Add(v1); verts.Add(v2);
            tris.Add(b); tris.Add(b + 1); tris.Add(b + 2);
        }
    }

    private static Vector3 Mid(Vector3 a, Vector3 b, float va, float vb)
    {
        if (Mathf.Abs(vb - va) < 1e-6f) return (a + b) * 0.5f;
        return Vector3.Lerp(a, b, va / (va - vb));
    }
}

// Marching cubes lookup tables (edge and triangle tables)
// Standard 256-case tables — density > 0 = solid, density <= 0 = air
public static class MarchingTables
{
    /// <summary>Grid cells per chunk along each axis for chunked mesh generation.</summary>
    public const int ChunkSize = 16;

    // Which of the 12 edges are intersected for each cube config (256 entries)
    public static readonly int[] EdgeTable = {
        0x000,0x109,0x203,0x30a,0x406,0x50f,0x605,0x70c,0x80c,0x905,0xa0f,0xb06,0xc0a,0xd03,0xe09,0xf00,
        0x190,0x099,0x393,0x29a,0x596,0x49f,0x795,0x69c,0x99c,0x895,0xb9f,0xa96,0xd9a,0xc93,0xf99,0xe90,
        0x230,0x339,0x033,0x13a,0x636,0x73f,0x435,0x53c,0xa3c,0xb35,0x83f,0x936,0xe3a,0xf33,0xc39,0xd30,
        0x3a0,0x2a9,0x1a3,0x0aa,0x7a6,0x6af,0x5a5,0x4ac,0xbac,0xaa5,0x9af,0x8a6,0xfaa,0xea3,0xda9,0xca0,
        0x460,0x569,0x663,0x76a,0x066,0x16f,0x265,0x36c,0xc6c,0xd65,0xe6f,0xf66,0x86a,0x963,0xa69,0xb60,
        0x5f0,0x4f9,0x7f3,0x6fa,0x1f6,0x0ff,0x3f5,0x2fc,0xdfc,0xcf5,0xfff,0xef6,0x9fa,0x8f3,0xbf9,0xaf0,
        0x650,0x759,0x453,0x55a,0x256,0x35f,0x055,0x15c,0xe5c,0xf55,0xc5f,0xd56,0xa5a,0xb53,0x859,0x950,
        0x7c0,0x6c9,0x5c3,0x4ca,0x3c6,0x2cf,0x1c5,0x0cc,0xfcc,0xec5,0xdcf,0xcc6,0xbca,0xac3,0x9c9,0x8c0,
        0x8c0,0x9c9,0xac3,0xbca,0xcc6,0xdcf,0xec5,0xfcc,0x0cc,0x1c5,0x2cf,0x3c6,0x4ca,0x5c3,0x6c9,0x7c0,
        0x950,0x859,0xb53,0xa5a,0xd56,0xc5f,0xf55,0xe5c,0x15c,0x055,0x35f,0x256,0x55a,0x453,0x759,0x650,
        0xaf0,0xbf9,0x8f3,0x9fa,0xef6,0xfff,0xcf5,0xdfc,0x2fc,0x3f5,0x0ff,0x1f6,0x6fa,0x7f3,0x4f9,0x5f0,
        0xb60,0xa69,0x963,0x86a,0xf66,0xe6f,0xd65,0xc6c,0x36c,0x265,0x16f,0x066,0x76a,0x663,0x569,0x460,
        0xca0,0xda9,0xea3,0xfaa,0x8a6,0x9af,0xaa5,0xbac,0x4ac,0x5a5,0x6af,0x7a6,0x0aa,0x1a3,0x2a9,0x3a0,
        0xd30,0xc39,0xf33,0xe3a,0x936,0x83f,0xb35,0xa3c,0x53c,0x435,0x73f,0x636,0x13a,0x033,0x339,0x230,
        0xe90,0xf99,0xc93,0xd9a,0xa96,0xb9f,0x895,0x99c,0x69c,0x795,0x49f,0x596,0x29a,0x393,0x099,0x190,
        0xf00,0xe09,0xd03,0xc0a,0xb06,0xa0f,0x905,0x80c,0x70c,0x605,0x50f,0x406,0x30a,0x203,0x109,0x000
    };

    // Triangle table: for each cube config, list of edge triplets (-1 = end of list)
    public static readonly int[,] TriTable = {
        {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {0,8,3,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {0,1,9,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {1,8,3,9,8,1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {1,2,10,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {0,8,3,1,2,10,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {9,2,10,0,2,9,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {2,8,3,2,10,8,10,9,8,-1,-1,-1,-1,-1,-1,-1},
        {3,11,2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {0,11,2,8,11,0,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {1,9,0,2,3,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {1,11,2,1,9,11,9,8,11,-1,-1,-1,-1,-1,-1,-1},
        {3,10,1,11,10,3,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {0,10,1,0,8,10,8,11,10,-1,-1,-1,-1,-1,-1,-1},
        {3,9,0,3,11,9,11,10,9,-1,-1,-1,-1,-1,-1,-1},
        {9,8,10,10,8,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {4,7,8,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {4,3,0,7,3,4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {0,1,9,8,4,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {4,1,9,4,7,1,7,3,1,-1,-1,-1,-1,-1,-1,-1},
        {1,2,10,8,4,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {3,4,7,3,0,4,1,2,10,-1,-1,-1,-1,-1,-1,-1},
        {9,2,10,9,0,2,8,4,7,-1,-1,-1,-1,-1,-1,-1},
        {2,10,9,2,9,7,2,7,3,7,9,4,-1,-1,-1,-1},
        {8,4,7,3,11,2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {11,4,7,11,2,4,2,0,4,-1,-1,-1,-1,-1,-1,-1},
        {9,0,1,8,4,7,2,3,11,-1,-1,-1,-1,-1,-1,-1},
        {4,7,11,9,4,11,9,11,2,9,2,1,-1,-1,-1,-1},
        {3,10,1,3,11,10,7,8,4,-1,-1,-1,-1,-1,-1,-1},
        {1,11,10,1,4,11,1,0,4,7,11,4,-1,-1,-1,-1},
        {4,7,8,9,0,11,9,11,10,11,0,3,-1,-1,-1,-1},
        {4,7,11,4,11,9,9,11,10,-1,-1,-1,-1,-1,-1,-1},
        {9,5,4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {9,5,4,0,8,3,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {0,5,4,1,5,0,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {8,5,4,8,3,5,3,1,5,-1,-1,-1,-1,-1,-1,-1},
        {1,2,10,9,5,4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {3,0,8,1,2,10,4,9,5,-1,-1,-1,-1,-1,-1,-1},
        {5,2,10,5,4,2,4,0,2,-1,-1,-1,-1,-1,-1,-1},
        {2,10,5,3,2,5,3,5,4,3,4,8,-1,-1,-1,-1},
        {9,5,4,2,3,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {0,11,2,0,8,11,4,9,5,-1,-1,-1,-1,-1,-1,-1},
        {0,5,4,0,1,5,2,3,11,-1,-1,-1,-1,-1,-1,-1},
        {2,1,5,2,5,8,2,8,11,4,8,5,-1,-1,-1,-1},
        {10,3,11,10,1,3,9,5,4,-1,-1,-1,-1,-1,-1,-1},
        {4,9,5,0,8,1,8,10,1,8,11,10,-1,-1,-1,-1},
        {5,4,0,5,0,11,5,11,10,11,0,3,-1,-1,-1,-1},
        {5,4,8,5,8,10,10,8,11,-1,-1,-1,-1,-1,-1,-1},
        {9,7,8,5,7,9,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {9,3,0,9,5,3,5,7,3,-1,-1,-1,-1,-1,-1,-1},
        {0,7,8,0,1,7,1,5,7,-1,-1,-1,-1,-1,-1,-1},
        {1,5,3,3,5,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {9,7,8,9,5,7,10,1,2,-1,-1,-1,-1,-1,-1,-1},
        {10,1,2,9,5,0,5,3,0,5,7,3,-1,-1,-1,-1},
        {8,0,2,8,2,5,8,5,7,10,5,2,-1,-1,-1,-1},
        {2,10,5,2,5,3,3,5,7,-1,-1,-1,-1,-1,-1,-1},
        {7,9,5,7,8,9,3,11,2,-1,-1,-1,-1,-1,-1,-1},
        {9,5,7,9,7,2,9,2,0,2,7,11,-1,-1,-1,-1},
        {2,3,11,0,1,8,1,7,8,1,5,7,-1,-1,-1,-1},
        {11,2,1,11,1,7,7,1,5,-1,-1,-1,-1,-1,-1,-1},
        {9,5,8,8,5,7,10,1,3,10,3,11,-1,-1,-1,-1},
        {5,7,0,5,0,9,7,11,0,1,0,10,11,10,0,-1},
        {11,10,0,11,0,3,10,5,0,8,0,7,5,7,0,-1},
        {11,10,5,7,11,5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {10,6,5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {0,8,3,5,10,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {9,0,1,5,10,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {1,8,3,1,9,8,5,10,6,-1,-1,-1,-1,-1,-1,-1},
        {1,6,5,2,6,1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {1,6,5,1,2,6,3,0,8,-1,-1,-1,-1,-1,-1,-1},
        {9,6,5,9,0,6,0,2,6,-1,-1,-1,-1,-1,-1,-1},
        {5,9,8,5,8,2,5,2,6,3,2,8,-1,-1,-1,-1},
        {2,3,11,10,6,5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {11,0,8,11,2,0,10,6,5,-1,-1,-1,-1,-1,-1,-1},
        {0,1,9,2,3,11,5,10,6,-1,-1,-1,-1,-1,-1,-1},
        {5,10,6,1,9,2,9,11,2,9,8,11,-1,-1,-1,-1},
        {6,3,11,6,5,3,5,1,3,-1,-1,-1,-1,-1,-1,-1},
        {0,8,11,0,11,5,0,5,1,5,11,6,-1,-1,-1,-1},
        {3,11,6,0,3,6,0,6,5,0,5,9,-1,-1,-1,-1},
        {6,5,9,6,9,11,11,9,8,-1,-1,-1,-1,-1,-1,-1},
        {5,10,6,4,7,8,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {4,3,0,4,7,3,6,5,10,-1,-1,-1,-1,-1,-1,-1},
        {1,9,0,5,10,6,8,4,7,-1,-1,-1,-1,-1,-1,-1},
        {10,6,5,1,9,7,1,7,3,7,9,4,-1,-1,-1,-1},
        {6,1,2,6,5,1,4,7,8,-1,-1,-1,-1,-1,-1,-1},
        {1,2,5,5,2,6,3,0,4,3,4,7,-1,-1,-1,-1},
        {8,4,7,9,0,5,0,6,5,0,2,6,-1,-1,-1,-1},
        {7,3,9,7,9,4,3,2,9,5,9,6,2,6,9,-1},
        {3,11,2,7,8,4,10,6,5,-1,-1,-1,-1,-1,-1,-1},
        {5,10,6,4,7,2,4,2,0,2,7,11,-1,-1,-1,-1},
        {0,1,9,4,7,8,2,3,11,5,10,6,-1,-1,-1,-1},
        {9,2,1,9,11,2,9,4,11,7,11,4,5,10,6,-1},
        {8,4,7,3,11,5,3,5,1,5,11,6,-1,-1,-1,-1},
        {5,1,11,5,11,6,1,0,11,7,11,4,0,4,11,-1},
        {0,5,9,0,6,5,0,3,6,11,6,3,8,4,7,-1},
        {6,5,9,6,9,11,4,7,9,7,11,9,-1,-1,-1,-1},
        {10,4,9,6,4,10,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {4,10,6,4,9,10,0,8,3,-1,-1,-1,-1,-1,-1,-1},
        {10,0,1,10,6,0,6,4,0,-1,-1,-1,-1,-1,-1,-1},
        {8,3,1,8,1,6,8,6,4,6,1,10,-1,-1,-1,-1},
        {1,4,9,1,2,4,2,6,4,-1,-1,-1,-1,-1,-1,-1},
        {3,0,8,1,2,9,2,4,9,2,6,4,-1,-1,-1,-1},
        {0,2,4,4,2,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {8,3,2,8,2,4,4,2,6,-1,-1,-1,-1,-1,-1,-1},
        {10,4,9,10,6,4,11,2,3,-1,-1,-1,-1,-1,-1,-1},
        {0,8,2,2,8,11,4,9,10,4,10,6,-1,-1,-1,-1},
        {3,11,2,0,1,6,0,6,4,6,1,10,-1,-1,-1,-1},
        {6,4,1,6,1,10,4,8,1,2,1,11,8,11,1,-1},
        {9,6,4,9,3,6,9,1,3,11,6,3,-1,-1,-1,-1},
        {8,11,1,8,1,0,11,6,1,9,1,4,6,4,1,-1},
        {3,11,6,3,6,0,0,6,4,-1,-1,-1,-1,-1,-1,-1},
        {6,4,8,11,6,8,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {7,10,6,7,8,10,8,9,10,-1,-1,-1,-1,-1,-1,-1},
        {0,7,3,0,10,7,0,9,10,6,7,10,-1,-1,-1,-1},
        {10,6,7,1,10,7,1,7,8,1,8,0,-1,-1,-1,-1},
        {10,6,7,10,7,1,1,7,3,-1,-1,-1,-1,-1,-1,-1},
        {1,2,6,1,6,8,1,8,9,8,6,7,-1,-1,-1,-1},
        {2,6,9,2,9,1,6,7,9,0,9,3,7,3,9,-1},
        {7,8,0,7,0,6,6,0,2,-1,-1,-1,-1,-1,-1,-1},
        {7,3,2,6,7,2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {2,3,11,10,6,8,10,8,9,8,6,7,-1,-1,-1,-1},
        {2,0,7,2,7,11,0,9,7,6,7,10,9,10,7,-1},
        {1,8,0,1,7,8,1,10,7,6,7,10,2,3,11,-1},
        {11,2,1,11,1,7,10,6,1,6,7,1,-1,-1,-1,-1},
        {8,9,6,8,6,7,9,1,6,11,6,3,1,3,6,-1},
        {0,9,1,11,6,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {7,8,0,7,0,6,3,11,0,11,6,0,-1,-1,-1,-1},
        {7,11,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {7,6,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {3,0,8,11,7,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {0,1,9,11,7,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {8,1,9,8,3,1,11,7,6,-1,-1,-1,-1,-1,-1,-1},
        {10,1,2,6,11,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {1,2,10,3,0,8,6,11,7,-1,-1,-1,-1,-1,-1,-1},
        {2,9,0,2,10,9,6,11,7,-1,-1,-1,-1,-1,-1,-1},
        {6,11,7,2,10,3,10,8,3,10,9,8,-1,-1,-1,-1},
        {7,2,3,6,2,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {7,0,8,7,6,0,6,2,0,-1,-1,-1,-1,-1,-1,-1},
        {2,7,6,2,3,7,0,1,9,-1,-1,-1,-1,-1,-1,-1},
        {1,6,2,1,8,6,1,9,8,8,7,6,-1,-1,-1,-1},
        {10,7,6,10,1,7,1,3,7,-1,-1,-1,-1,-1,-1,-1},
        {10,7,6,1,7,10,1,8,7,1,0,8,-1,-1,-1,-1},
        {0,3,7,0,7,10,0,10,9,6,10,7,-1,-1,-1,-1},
        {7,6,10,7,10,8,8,10,9,-1,-1,-1,-1,-1,-1,-1},
        {6,8,4,11,8,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {3,6,11,3,0,6,0,4,6,-1,-1,-1,-1,-1,-1,-1},
        {8,6,11,8,4,6,9,0,1,-1,-1,-1,-1,-1,-1,-1},
        {9,4,6,9,6,3,9,3,1,11,3,6,-1,-1,-1,-1},
        {6,8,4,6,11,8,2,10,1,-1,-1,-1,-1,-1,-1,-1},
        {1,2,10,3,0,11,0,6,11,0,4,6,-1,-1,-1,-1},
        {4,11,8,4,6,11,0,2,9,2,10,9,-1,-1,-1,-1},
        {10,9,3,10,3,2,9,4,3,11,3,6,4,6,3,-1},
        {8,2,3,8,4,2,4,6,2,-1,-1,-1,-1,-1,-1,-1},
        {0,4,2,4,6,2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {1,9,0,2,3,4,2,4,6,4,3,8,-1,-1,-1,-1},
        {1,9,4,1,4,2,2,4,6,-1,-1,-1,-1,-1,-1,-1},
        {8,1,3,8,6,1,8,4,6,6,10,1,-1,-1,-1,-1},
        {10,1,0,10,0,6,6,0,4,-1,-1,-1,-1,-1,-1,-1},
        {4,6,3,4,3,8,6,10,3,0,3,9,10,9,3,-1},
        {10,9,4,6,10,4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {4,9,5,7,6,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {0,8,3,4,9,5,11,7,6,-1,-1,-1,-1,-1,-1,-1},
        {5,0,1,5,4,0,7,6,11,-1,-1,-1,-1,-1,-1,-1},
        {11,7,6,8,3,4,3,5,4,3,1,5,-1,-1,-1,-1},
        {9,5,4,10,1,2,7,6,11,-1,-1,-1,-1,-1,-1,-1},
        {6,11,7,1,2,10,0,8,3,4,9,5,-1,-1,-1,-1},
        {7,6,11,5,4,10,4,2,10,4,0,2,-1,-1,-1,-1},
        {3,4,8,3,5,4,3,2,5,10,5,2,11,7,6,-1},
        {7,2,3,7,6,2,5,4,9,-1,-1,-1,-1,-1,-1,-1},
        {9,5,4,0,8,6,0,6,2,6,8,7,-1,-1,-1,-1},
        {3,6,2,3,7,6,1,5,0,5,4,0,-1,-1,-1,-1},
        {6,2,8,6,8,7,2,1,8,4,8,5,1,5,8,-1},
        {9,5,4,10,1,6,1,7,6,1,3,7,-1,-1,-1,-1},
        {1,6,10,1,7,6,1,0,7,8,7,0,9,5,4,-1},
        {4,0,10,4,10,5,0,3,10,6,10,7,3,7,10,-1},
        {7,6,10,7,10,8,5,4,10,4,8,10,-1,-1,-1,-1},
        {6,9,5,6,11,9,11,8,9,-1,-1,-1,-1,-1,-1,-1},
        {3,6,11,0,6,3,0,5,6,0,9,5,-1,-1,-1,-1},
        {0,11,8,0,5,11,0,1,5,5,6,11,-1,-1,-1,-1},
        {6,11,3,6,3,5,5,3,1,-1,-1,-1,-1,-1,-1,-1},
        {1,2,10,9,5,11,9,11,8,11,5,6,-1,-1,-1,-1},
        {0,11,3,0,6,11,0,9,6,5,6,9,1,2,10,-1},
        {11,8,5,11,5,6,8,0,5,10,5,2,0,2,5,-1},
        {6,11,3,6,3,5,2,10,3,10,5,3,-1,-1,-1,-1},
        {5,8,9,5,2,8,5,6,2,3,8,2,-1,-1,-1,-1},
        {9,5,6,9,6,0,0,6,2,-1,-1,-1,-1,-1,-1,-1},
        {1,5,8,1,8,0,5,6,8,3,8,2,6,2,8,-1},
        {1,5,6,2,1,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {1,3,6,1,6,10,3,8,6,5,6,9,8,9,6,-1},
        {10,1,0,10,0,6,9,5,0,5,6,0,-1,-1,-1,-1},
        {0,3,8,5,6,10,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {10,5,6,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {11,5,10,7,5,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {11,5,10,11,7,5,8,3,0,-1,-1,-1,-1,-1,-1,-1},
        {5,11,7,5,10,11,1,9,0,-1,-1,-1,-1,-1,-1,-1},
        {10,7,5,10,11,7,9,8,1,8,3,1,-1,-1,-1,-1},
        {11,1,2,11,7,1,7,5,1,-1,-1,-1,-1,-1,-1,-1},
        {0,8,3,1,2,7,1,7,5,7,2,11,-1,-1,-1,-1},
        {9,7,5,9,2,7,9,0,2,2,11,7,-1,-1,-1,-1},
        {7,5,2,7,2,11,5,9,2,3,2,8,9,8,2,-1},
        {2,5,10,2,3,5,3,7,5,-1,-1,-1,-1,-1,-1,-1},
        {8,2,0,8,5,2,8,7,5,10,2,5,-1,-1,-1,-1},
        {9,0,1,2,3,10,3,5,10,3,7,5,-1,-1,-1,-1},
        {9,8,7,9,7,5,1,2,10,-1,-1,-1,-1,-1,-1,-1},
        {1,3,5,5,3,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {0,8,7,0,7,1,1,7,5,-1,-1,-1,-1,-1,-1,-1},
        {9,0,3,9,3,5,5,3,7,-1,-1,-1,-1,-1,-1,-1},
        {9,8,7,5,9,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {5,8,4,5,10,8,10,11,8,-1,-1,-1,-1,-1,-1,-1},
        {5,0,4,5,11,0,5,10,11,11,3,0,-1,-1,-1,-1},
        {0,1,9,8,4,10,8,10,11,10,4,5,-1,-1,-1,-1},
        {10,11,4,10,4,5,11,3,4,9,4,1,3,1,4,-1},
        {2,5,1,2,8,5,2,11,8,4,5,8,-1,-1,-1,-1},
        {0,4,11,0,11,3,4,5,11,2,11,1,5,1,11,-1},
        {0,2,5,0,5,9,2,11,5,4,5,8,11,8,5,-1},
        {9,4,5,2,11,3,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {2,5,10,3,5,2,3,4,5,3,8,4,-1,-1,-1,-1},
        {5,10,2,5,2,4,4,2,0,-1,-1,-1,-1,-1,-1,-1},
        {3,10,2,3,5,10,3,8,5,4,5,8,0,1,9,-1},
        {5,10,2,5,2,4,1,9,2,9,4,2,-1,-1,-1,-1},
        {8,4,5,8,5,3,3,5,1,-1,-1,-1,-1,-1,-1,-1},
        {0,4,5,1,0,5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {8,4,5,8,5,3,9,0,5,0,3,5,-1,-1,-1,-1},
        {9,4,5,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {4,11,7,4,9,11,9,10,11,-1,-1,-1,-1,-1,-1,-1},
        {0,8,3,4,9,7,9,11,7,9,10,11,-1,-1,-1,-1},
        {1,10,11,1,11,4,1,4,0,7,4,11,-1,-1,-1,-1},
        {3,1,4,3,4,8,1,10,4,7,4,11,10,11,4,-1},
        {4,11,7,9,11,4,9,2,11,9,1,2,-1,-1,-1,-1},
        {9,7,4,9,11,7,9,1,11,2,11,1,0,8,3,-1},
        {11,7,4,11,4,2,2,4,0,-1,-1,-1,-1,-1,-1,-1},
        {11,7,4,11,4,2,8,3,4,3,2,4,-1,-1,-1,-1},
        {2,9,10,2,7,9,2,3,7,7,4,9,-1,-1,-1,-1},
        {9,10,7,9,7,4,10,2,7,8,7,0,2,0,7,-1},
        {3,7,10,3,10,2,7,4,10,1,10,0,4,0,10,-1},
        {1,10,2,8,7,4,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {4,9,1,4,1,7,7,1,3,-1,-1,-1,-1,-1,-1,-1},
        {4,9,1,4,1,7,0,8,1,8,7,1,-1,-1,-1,-1},
        {4,0,3,7,4,3,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {4,8,7,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {9,10,8,10,11,8,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {3,0,9,3,9,11,11,9,10,-1,-1,-1,-1,-1,-1,-1},
        {0,1,10,0,10,8,8,10,11,-1,-1,-1,-1,-1,-1,-1},
        {3,1,10,11,3,10,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {1,2,11,1,11,9,9,11,8,-1,-1,-1,-1,-1,-1,-1},
        {3,0,9,3,9,11,1,2,9,2,11,9,-1,-1,-1,-1},
        {0,2,11,8,0,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {3,2,11,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {2,3,8,2,8,10,10,8,9,-1,-1,-1,-1,-1,-1,-1},
        {9,10,2,0,9,2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {2,3,8,2,8,10,0,1,8,1,10,8,-1,-1,-1,-1},
        {1,10,2,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {1,3,8,9,1,8,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {0,9,1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {0,3,8,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
        {-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1,-1},
    };

    // Which two corner vertices each of the 12 cube edges connects
    public static readonly int[,] EdgeVertices = {
        {0,1},{1,2},{2,3},{3,0},
        {4,5},{5,6},{6,7},{7,4},
        {0,4},{1,5},{2,6},{3,7}
    };

    // Unit cube corner offsets
    public static readonly Vector3[] Corners = {
        new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1), new Vector3(0,0,1),
        new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(0,1,1)
    };
}
