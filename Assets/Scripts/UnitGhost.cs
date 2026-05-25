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

    [Header("Path Line")]
    [Tooltip("Colour of the line showing the full precomputed path the ghost is walking.")]
    public Color pathColor     = new Color(0.45f, 0.8f, 1f, 1f);
    [Tooltip("Width of the ghost's path line.")]
    public float pathLineWidth = 0.4f;

    public bool IsFinished => _finished;
    public bool HasPath    => _path != null && _path.Count >= 2;

    TerrainDataStore _tds;
    UnitTypeSO       _unitType;
    List<Vector2Int> _path;
    float _pathLineLift;
    int   _segmentIndex;
    float _segmentT;
    bool  _finished;
    bool  _subscribed;

    GameObject   _orb;
    LineRenderer _stem;
    LineRenderer _pathLine;

    /// <summary>
    /// Pin the ghost to the start of <paramref name="path"/> and build its visuals.
    /// <paramref name="pathLineLift"/> is the same lift the main path line uses,
    /// so the stem lands exactly on it. <paramref name="unitType"/> is used to
    /// resolve obstacle effects when checking whether the ghost is on a Block cell.
    /// </summary>
    public void Initialize(TerrainDataStore tds, UnitTypeSO unitType, List<Vector2Int> path, float pathLineLift)
    {
        // Resubscribe cleanly if Initialize is called more than once (e.g. on
        // re-route): drop the previous tds binding before swapping.
        Unsubscribe();

        _tds          = tds;
        _unitType     = unitType;
        _path         = path;
        _pathLineLift = pathLineLift;
        _segmentIndex = 0;
        _segmentT     = 0f;
        _finished     = (path == null || path.Count < 2);

        BuildVisuals();
        DrawPathLine();
        UpdateVisuals();

        Subscribe();
        StepOffBlocked();
    }

    void OnDestroy() => Unsubscribe();

    void Subscribe()
    {
        if (_subscribed || _tds == null) return;
        _tds.OnObstacleRegistered += HandleObstacleRegistered;
        _subscribed = true;
    }

    void Unsubscribe()
    {
        if (!_subscribed || _tds == null) return;
        _tds.OnObstacleRegistered -= HandleObstacleRegistered;
        _subscribed = false;
    }

    void HandleObstacleRegistered(PlacedObstacle po) => StepOffBlocked();

    /// <summary>
    /// Event-driven entry point. Bumps the ghost past any blocked cells it currently
    /// sits on and refreshes visuals. Safe to call when nothing's blocked (no-op).
    /// </summary>
    void StepOffBlocked()
    {
        if (_finished || _path == null || _path.Count < 2) return;
        BumpPastBlocked();
        UpdateVisuals();
    }

    /// <summary>
    /// Walks the ghost forward waypoint-by-waypoint while the current cell is
    /// blocked for this unit type (obstacle Block, biome Block, or a slope-blocked
    /// step forward). Caller is responsible for calling <see cref="UpdateVisuals"/>
    /// after — done that way so <see cref="Advance"/> only repaints once per frame.
    /// Capped at path length so a fully-blocked path can't infinite-loop.
    /// </summary>
    void BumpPastBlocked()
    {
        if (_finished || _path == null || _path.Count < 2) return;

        int safety = _path.Count;
        while (safety-- > 0 && !_finished && IsCurrentCellBlocked())
            AdvanceWaypoint();
    }

    void AdvanceWaypoint()
    {
        _segmentIndex++;
        _segmentT = 0f;
        if (_segmentIndex + 1 >= _path.Count)
        {
            _segmentIndex = _path.Count - 2;
            _segmentT     = 1f;
            _finished     = true;
        }
    }

    bool IsCurrentCellBlocked()
    {
        if (_tds == null || _tds.grid == null) return false;
        if (_path == null || _path.Count < 2) return false;

        // Current cell under the foot: obstacle Block or biome Block?
        Vector2Int g = _tds.WorldToGrid(GetFootPosition());
        if (_tds.InBounds(g.x, g.y))
        {
            CellData here = _tds.grid[g.x, g.y];

            if (here.obstacle != null && here.obstacle.obstacleSo != null
                && here.obstacle.obstacleSo.ResolveEffect(_unitType).effect == CellEffect.Block)
                return true;

            if (here.radiusObstacles != null)
            {
                for (int i = 0; i < here.radiusObstacles.Count; i++)
                {
                    PlacedObstacle src = here.radiusObstacles[i];
                    if (src == null || src.obstacleSo == null) continue;
                    if (src.obstacleSo.ResolveRadiusEffect(_unitType).effect == CellEffect.Block)
                        return true;
                }
            }

            if (here.biome != null
                && here.biome.ResolveEffect(_unitType).effect == CellEffect.Block)
                return true;
        }

        // Slope: is the next step (from the current waypoint to the one after)
        // slope-blocked for this unit type?
        if (_segmentIndex >= 0 && _segmentIndex + 1 < _path.Count)
        {
            Vector2Int fromGrid = _path[_segmentIndex];
            if (_tds.InBounds(fromGrid.x, fromGrid.y))
            {
                CellData fromCell = _tds.grid[fromGrid.x, fromGrid.y];
                Vector2Int delta  = _path[_segmentIndex + 1] - fromGrid;
                AStarPathfinder.ResolveSlopeMultiplier(fromCell, delta, _unitType, out bool slopeBlocked);
                if (slopeBlocked) return true;
            }
        }

        return false;
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
                AdvanceWaypoint();
            }
        }

        // After the requested distance is consumed, free-walk forward through any
        // cells the ghost ended up on that are blocked for this unit type (obstacle
        // Block, biome Block, or a blocked slope step forward). Per Rick: this is
        // allowed to push the ghost past its normal leash.
        BumpPastBlocked();

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

        if (_pathLine == null)
        {
            var pathGO = new GameObject("PathLine");
            pathGO.transform.SetParent(transform, false);
            _pathLine = pathGO.AddComponent<LineRenderer>();
            _pathLine.useWorldSpace = true;
            _pathLine.positionCount = 0;

            string[] pathCandidates =
            {
                "Universal Render Pipeline/Particles/Unlit",
                "Sprites/Default",
                "Legacy Shaders/Particles/Alpha Blended",
                "Unlit/Color",
            };
            Shader pathShader = null;
            foreach (var n in pathCandidates)
            {
                pathShader = Shader.Find(n);
                if (pathShader != null) break;
            }
            var pathMat = pathShader != null ? new Material(pathShader) : new Material(Shader.Find("Standard"));
            pathMat.color       = pathColor;
            _pathLine.material   = pathMat;
            _pathLine.startColor = pathColor;
            _pathLine.endColor   = pathColor;
        }
        _pathLine.startWidth = pathLineWidth;
        _pathLine.endWidth   = pathLineWidth;
    }

    /// <summary>
    /// Redraws the full precomputed path. Called from Initialize whenever the
    /// ghost is given a (possibly new) path; the line never changes mid-walk.
    /// </summary>
    void DrawPathLine()
    {
        if (_pathLine == null || _tds == null || _path == null || _path.Count == 0)
        {
            if (_pathLine != null) _pathLine.positionCount = 0;
            return;
        }

        _pathLine.positionCount = _path.Count;
        for (int i = 0; i < _path.Count; i++)
            _pathLine.SetPosition(i, CellFoot(_path[i]));
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
