using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws the precomputed A* path as a single LineRenderer during the prep phase,
/// before <see cref="UnitSpawner"/> instantiates a unit. Once the unit spawns, its
/// <see cref="UnitGhost"/> takes over drawing the live path line and this preview
/// is hidden via <see cref="Hide"/>. Visuals mirror UnitGhost.pathLine defaults so
/// the handover doesn't look like a colour/width change.
/// </summary>
public class PathPreviewRenderer : MonoBehaviour
{
    [Header("Visual")]
    [Tooltip("Colour of the preview path line. Defaults match UnitGhost.pathColor so the handover to the live ghost line is seamless.")]
    public Color color = new Color(0.45f, 0.8f, 1f, 1f);
    [Tooltip("Width of the preview path line. Defaults match UnitGhost.pathLineWidth.")]
    public float width = 0.4f;
    [Tooltip("Height above terrain at which the line is drawn. Mirror UnitMover.pathLineLift so the line sits at the same height the ghost will draw it.")]
    public float lift  = 0.1f;

    LineRenderer _line;

    /// <summary>Renders <paramref name="path"/> as a polyline lifted off the terrain. A path of fewer than 2 cells clears the line.</summary>
    public void Show(TerrainDataStore tds, List<Vector2Int> path)
    {
        EnsureLine();
        if (tds == null || path == null || path.Count < 2)
        {
            _line.positionCount = 0;
            return;
        }
        _line.positionCount = path.Count;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 wp = tds.GridToWorld(path[i]);
            wp.y = tds.GetRoundedHeight(path[i].x, path[i].y) + lift;
            _line.SetPosition(i, wp);
        }
    }

    /// <summary>Clears the line. Safe to call before <see cref="Show"/>.</summary>
    public void Hide()
    {
        if (_line != null) _line.positionCount = 0;
    }

    void EnsureLine()
    {
        if (_line != null) return;
        _line = gameObject.AddComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.startWidth    = width;
        _line.endWidth      = width;
        _line.positionCount = 0;

        string[] candidates =
        {
            "Universal Render Pipeline/Particles/Unlit",
            "Sprites/Default",
            "Legacy Shaders/Particles/Alpha Blended",
            "Unlit/Color",
        };
        Shader shader = null;
        foreach (var n in candidates)
        {
            shader = Shader.Find(n);
            if (shader != null) break;
        }
        var mat = shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
        mat.color        = color;
        _line.material   = mat;
        _line.startColor = color;
        _line.endColor   = color;
    }
}
