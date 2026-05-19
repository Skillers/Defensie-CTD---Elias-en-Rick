using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Behavioural knobs for a <see cref="UnitGhost"/>, owned by <see cref="UnitSpawner"/>
/// and pushed into <see cref="UnitMover"/> at spawn time via
/// <see cref="UnitMover.ApplyGhostSettings"/>. Visual settings (colour, orb size,
/// hover, stem) live on the prefab's <see cref="UnitGhost"/> component instead.
/// </summary>
[System.Serializable]
public class GhostSettings
{
    [Tooltip("Spawn a floating ghost orb that walks the main A* line at the unit's nominal moveSpeed. The unit can never push it closer than minDistance, and it slows / stops if it gets too far ahead.")]
    public bool      enabled          = true;
    [Tooltip("Optional. Prefab with a UnitGhost component for custom visuals (colour / size / hover / stem). When null, one is spawned at runtime with the UnitGhost defaults.")]
    public UnitGhost prefab;
    [Tooltip("Minimum leash distance (world units, planar). If the unit closes inside this, the ghost is snapped forward along its path until it sits this far away again. Never moves backwards.")]
    public float     minDistance      = 2f;
    [Tooltip("When the orb gets more than this far ahead of the unit, it starts to slow down.")]
    public float     slowDistance     = 8f;
    [Tooltip("When the orb gets more than this far ahead of the unit, it stops entirely until the unit catches up.")]
    public float     stopDistance     = 20f;
    [Tooltip("Safety cap on how many leash-snap iterations may run per frame — protects against degenerate path geometry.")]
    public int       maxStepsPerFrame = 8;
}

/// <summary>
/// A floating orb that walks a precomputed cell path (the unit's main A* line).
/// Owns its own visuals: a sphere primitive and a thin vertical LineRenderer
/// stem that drops from the orb to the line so its position on the line is
/// readable.
///
/// Behaviour is driven externally — call <see cref="Initialize"/> with the path
/// once, then call <see cref="Advance"/> each frame with the world-space distance
/// the ghost should move. <see cref="UnitMover"/> is the typical driver; it
/// computes the per-frame speed and queries <see cref="GetFootPosition"/> for
/// distance comparisons and catch-up A* goals.
/// </summary>
public class UnitGhost : MonoBehaviour
{
    [Header("Visual")]
    public Color color       = new Color(0.25f, 0.55f, 1f, 1f);
    [Tooltip("Diameter of the floating orb.")]
    public float orbSize     = 1.2f;
    [Tooltip("How high the orb floats above the main line.")]
    public float hoverHeight = 4f;
    [Tooltip("Width of the vertical stem drawn from the orb down to the line.")]
    public float stemWidth   = 0.12f;

    public bool IsFinished => _finished;
    public bool HasPath    => _path != null && _path.Count >= 2;

    TerrainDataStore _tds;
    List<Vector2Int> _path;
    float _pathLineLift;
    int   _segmentIndex;
    float _segmentT;
    bool  _finished;

    GameObject   _orb;
    LineRenderer _stem;

    /// <summary>
    /// Pin the ghost to the start of <paramref name="path"/> and build its visuals.
    /// <paramref name="pathLineLift"/> is the same lift the main path line uses,
    /// so the stem lands exactly on it.
    /// </summary>
    public void Initialize(TerrainDataStore tds, List<Vector2Int> path, float pathLineLift)
    {
        _tds          = tds;
        _path         = path;
        _pathLineLift = pathLineLift;
        _segmentIndex = 0;
        _segmentT     = 0f;
        _finished     = (path == null || path.Count < 2);

        BuildVisuals();
        UpdateVisuals();
    }

    /// <summary>Advance the ghost <paramref name="distance"/> world units along the path.</summary>
    public void Advance(float distance)
    {
        if (_finished || _tds == null || _path == null) return;

        while (distance > 0f && !_finished && _segmentIndex + 1 < _path.Count)
        {
            Vector3 a = CellFoot(_path[_segmentIndex]);
            Vector3 b = CellFoot(_path[_segmentIndex + 1]);
            float segLen = Vector3.Distance(a, b);
            if (segLen <= 0f) { _segmentIndex++; _segmentT = 0f; continue; }

            float remaining = (1f - _segmentT) * segLen;
            if (distance < remaining)
            {
                _segmentT += distance / segLen;
                distance = 0f;
            }
            else
            {
                distance -= remaining;
                _segmentIndex++;
                _segmentT = 0f;
                if (_segmentIndex + 1 >= _path.Count)
                {
                    _segmentIndex = _path.Count - 2;
                    _segmentT     = 1f;
                    _finished     = true;
                }
            }
        }

        UpdateVisuals();
    }

    /// <summary>
    /// World-space position of the ghost on the main line (at <see cref="_pathLineLift"/>
    /// — i.e. where its stem touches). Use this for distance checks and catch-up
    /// A* goals; the orb itself floats `hoverHeight` above this.
    /// </summary>
    public Vector3 GetFootPosition()
    {
        if (_tds == null || _path == null || _path.Count == 0) return transform.position;
        if (_path.Count == 1 || _segmentIndex + 1 >= _path.Count) return CellFoot(_path[_path.Count - 1]);
        Vector3 a = CellFoot(_path[_segmentIndex]);
        Vector3 b = CellFoot(_path[_segmentIndex + 1]);
        return Vector3.Lerp(a, b, _segmentT);
    }

    Vector3 CellFoot(Vector2Int cell)
    {
        Vector3 p = _tds.GridToWorld(cell);
        p.y = _tds.GetRoundedHeight(cell.x, cell.y) + _pathLineLift;
        return p;
    }

    void BuildVisuals()
    {
        if (_orb == null)
        {
            _orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _orb.name = "Orb";
            _orb.transform.SetParent(transform, false);

            var col = _orb.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var orbShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var orbMat = new Material(orbShader);
            orbMat.color = color;
            if (orbMat.HasProperty("_BaseColor")) orbMat.SetColor("_BaseColor", color);
            if (orbMat.HasProperty("_EmissionColor"))
            {
                orbMat.EnableKeyword("_EMISSION");
                orbMat.SetColor("_EmissionColor", color * 1.5f);
            }
            _orb.GetComponent<MeshRenderer>().sharedMaterial = orbMat;
        }
        _orb.transform.localScale = Vector3.one * orbSize;

        if (_stem == null)
        {
            var stemGO = new GameObject("Stem");
            stemGO.transform.SetParent(transform, false);
            _stem = stemGO.AddComponent<LineRenderer>();
            _stem.useWorldSpace = true;
            _stem.positionCount = 2;

            string[] stemCandidates =
            {
                "Universal Render Pipeline/Particles/Unlit",
                "Sprites/Default",
                "Legacy Shaders/Particles/Alpha Blended",
                "Unlit/Color",
            };
            Shader stemShader = null;
            foreach (var n in stemCandidates)
            {
                stemShader = Shader.Find(n);
                if (stemShader != null) break;
            }
            var stemMat = stemShader != null ? new Material(stemShader) : new Material(Shader.Find("Standard"));
            stemMat.color    = color;
            _stem.material   = stemMat;
            _stem.startColor = color;
            _stem.endColor   = color;
        }
        _stem.startWidth = stemWidth;
        _stem.endWidth   = stemWidth;
    }

    void UpdateVisuals()
    {
        if (_orb == null) return;
        Vector3 foot   = GetFootPosition();
        Vector3 orbPos = foot + Vector3.up * hoverHeight;
        _orb.transform.position = orbPos;
        if (_stem != null)
        {
            _stem.SetPosition(0, orbPos);
            _stem.SetPosition(1, foot);
        }
    }
}
