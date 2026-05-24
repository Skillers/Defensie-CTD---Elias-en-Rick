using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Visual overlay that highlights every grid cell currently registered to an
/// obstacle. Rebuilt purely on change: subscribes to
/// <see cref="TerrainDataStore.OnObstacleRegistered"/> and
/// <see cref="TerrainDataStore.OnObstacleUnregistered"/>, so there is no per-frame
/// work and the overlay always matches the cell map the pathfinder reads.
/// Cells are coloured by their <see cref="ObstacleSO.defaultEffect"/> (no unit
/// type context, so this shows the obstacle's intrinsic effect).
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class ObstacleOverlay : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Source of the grid and the obstacle change events. Auto-found if left empty.")]
    public TerrainDataStore terrainDataStore;

    [Header("Visual")]
    [Tooltip("Extra height above each cell's terrain surface at which the quad is drawn.")]
    public float liftAboveTerrain = 0.05f;

    [Header("Colours by ObstacleSO.defaultEffect")]
    public Color blockColor   = new Color(1f,   0.1f, 0.1f, 0.55f);
    public Color slowColor    = new Color(1f,   0.6f, 0.0f, 0.45f);
    public Color disruptColor = new Color(0.8f, 0.3f, 0.9f, 0.45f);
    public Color turnColor    = new Color(0.2f, 0.8f, 0.2f, 0.45f);
    public Color noneColor    = new Color(0.2f, 0.5f, 1f,   0.35f);

    // Tracked so a rebuild can iterate exactly the obstacles currently registered.
    // Populated/depopulated by the events, never by polling.
    readonly HashSet<PlacedObstacle> _obstacles = new HashSet<PlacedObstacle>();

    Mesh         _mesh;
    MeshFilter   _meshFilter;
    MeshRenderer _meshRenderer;

    void Awake()
    {
        if (terrainDataStore == null) terrainDataStore = FindFirstObjectByType<TerrainDataStore>();

        _meshFilter   = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();

        _mesh = new Mesh { name = "ObstacleOverlay" };
        _mesh.MarkDynamic();
        _meshFilter.mesh = _mesh;

        EnsureMaterial();
    }

    void OnEnable()
    {
        if (terrainDataStore == null) return;
        terrainDataStore.OnObstacleRegistered   += HandleRegistered;
        terrainDataStore.OnObstacleUnregistered += HandleUnregistered;
        Rebuild();
    }

    void OnDisable()
    {
        if (terrainDataStore == null) return;
        terrainDataStore.OnObstacleRegistered   -= HandleRegistered;
        terrainDataStore.OnObstacleUnregistered -= HandleUnregistered;
    }

    void HandleRegistered(PlacedObstacle po)
    {
        if (po != null) _obstacles.Add(po);
        Rebuild();
    }

    void HandleUnregistered(PlacedObstacle po)
    {
        if (po != null) _obstacles.Remove(po);
        Rebuild();
    }

    void Rebuild()
    {
        _mesh.Clear();
        if (terrainDataStore == null || terrainDataStore.grid == null) return;

        int cellCount = 0;
        foreach (PlacedObstacle po in _obstacles)
            if (po != null && po.affectedCells != null) cellCount += po.affectedCells.Count;
        if (cellCount == 0) return;

        var verts = new Vector3[cellCount * 4];
        var cols  = new Color[cellCount * 4];
        var tris  = new int[cellCount * 6];

        float halfStep = terrainDataStore.step * 0.5f;
        int v = 0, t = 0;

        foreach (PlacedObstacle po in _obstacles)
        {
            if (po == null || po.affectedCells == null) continue;
            Color c = ResolveColor(po);

            foreach (Vector2Int cell in po.affectedCells)
            {
                Vector3 center = terrainDataStore.GridToWorld(cell);
                float y = terrainDataStore.GetRoundedHeight(cell.x, cell.y) + liftAboveTerrain;

                verts[v + 0] = new Vector3(center.x - halfStep, y, center.z - halfStep);
                verts[v + 1] = new Vector3(center.x - halfStep, y, center.z + halfStep);
                verts[v + 2] = new Vector3(center.x + halfStep, y, center.z + halfStep);
                verts[v + 3] = new Vector3(center.x + halfStep, y, center.z - halfStep);

                cols[v + 0] = c; cols[v + 1] = c; cols[v + 2] = c; cols[v + 3] = c;

                tris[t + 0] = v + 0; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v + 0; tris[t + 4] = v + 2; tris[t + 5] = v + 3;

                v += 4;
                t += 6;
            }
        }

        if (verts.Length > 65535) _mesh.indexFormat = IndexFormat.UInt32;
        _mesh.vertices  = verts;
        _mesh.colors    = cols;
        _mesh.triangles = tris;
        _mesh.RecalculateBounds();
    }

    Color ResolveColor(PlacedObstacle po)
    {
        if (po.obstacleSo == null) return noneColor;
        switch (po.obstacleSo.defaultEffect.effect)
        {
            case CellEffect.Block:   return blockColor;
            case CellEffect.Slow:    return slowColor;
            case CellEffect.Disrupt: return disruptColor;
            case CellEffect.Turn:    return turnColor;
            default:                     return noneColor;
        }
    }

    void EnsureMaterial()
    {
        if (_meshRenderer.sharedMaterial != null) return;

        // The path lines in this project use the same candidate set. URP Particles/Unlit
        // and the legacy particle/sprite shaders all respect vertex colour, which is how
        // we tint each cell.
        string[] candidates =
        {
            "Universal Render Pipeline/Particles/Unlit",
            "Legacy Shaders/Particles/Alpha Blended",
            "Sprites/Default",
        };
        Shader shader = null;
        foreach (string name in candidates)
        {
            shader = Shader.Find(name);
            if (shader != null) break;
        }

        var mat = new Material(shader != null ? shader : Shader.Find("Standard"));
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
        mat.color = Color.white;
        _meshRenderer.sharedMaterial = mat;
    }
}
