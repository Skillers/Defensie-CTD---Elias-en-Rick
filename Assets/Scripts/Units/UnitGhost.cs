using System.Collections.Generic;
using UnityEngine;

/// <summary>Behavioural knobs for a <see cref="UnitGhost"/>. Visual settings live on the prefab's UnitGhost component.</summary>
[System.Serializable]
public class GhostSettings
{
    [Tooltip("Spawn a ghost orb that walks the main A* line ahead of the unit.")]
    public bool      enabled          = true;
    [Tooltip("Optional prefab with a UnitGhost component for custom visuals.")]
    public UnitGhost prefab;
    [Tooltip("Minimum leash distance (world units, planar). The unit can never push the ghost closer than this.")]
    public float     minDistance      = 2f;
    [Tooltip("The ghost slows down beyond this distance ahead of the unit.")]
    public float     slowDistance     = 8f;
    [Tooltip("The ghost stops beyond this distance ahead of the unit.")]
    public float     stopDistance     = 20f;
    [Tooltip("Cap on leash-snap iterations per frame.")]
    public int       maxStepsPerFrame = 8;
}

/// <summary>
/// Floating orb that walks a precomputed cell path ahead of the unit.
/// Driven externally: <see cref="Initialize"/> once, then <see cref="Advance"/> per frame.
/// </summary>
public class UnitGhost : MonoBehaviour
{
    [Header("Visual")]
    public Color color       = new Color(0.25f, 0.55f, 1f, 1f);
    [Tooltip("Diameter of the orb.")]
    public float orbSize     = 1.2f;
    [Tooltip("Height of the orb above the path line.")]
    public float hoverHeight = 4f;
    [Tooltip("Width of the stem from the orb down to the line.")]
    public float stemWidth   = 0.12f;
    [Tooltip("Show the orb and stem. Visual only.")]
    public bool  orbVisible  = true;

    [Header("Path Line")]
    public Color pathColor     = new Color(0.45f, 0.8f, 1f, 1f);
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

    /// <summary>Pins the ghost to the start of the path and builds its visuals. Safe to call again on re-route.</summary>
    public void Initialize(TerrainDataStore tds, UnitTypeSO unitType, List<Vector2Int> path, float pathLineLift)
    {
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

    void Update()
    {
        SyncOrbVisibility();
    }

    void SyncOrbVisibility()
    {
        if (_orb != null && _orb.activeSelf != orbVisible)
            _orb.SetActive(orbVisible);
        if (_stem != null && _stem.gameObject.activeSelf != orbVisible)
            _stem.gameObject.SetActive(orbVisible);
    }

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

    /// <summary>Bumps the ghost past any blocked cell it sits on and refreshes visuals.</summary>
    void StepOffBlocked()
    {
        if (_finished || _path == null || _path.Count < 2) return;
        BumpPastBlocked();
        UpdateVisuals();
    }

    /// <summary>Walks forward while the current cell is blocked. Caller repaints; capped at path length.</summary>
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

        // Also blocked when the next step is slope-blocked for this unit type.
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

        // Blocked cells may push the ghost past its normal leash; that is intended.
        BumpPastBlocked();

        UpdateVisuals();
    }

    /// <summary>World position where the stem touches the line. The orb floats hoverHeight above this.</summary>
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

        // Apply visibility now so the orb doesn't flash for one frame.
        SyncOrbVisibility();
    }

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
