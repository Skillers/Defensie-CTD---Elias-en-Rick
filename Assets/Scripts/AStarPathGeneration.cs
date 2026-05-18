using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Scene-level path maker. Owns all A* path generation that used to live on the
/// unit (<see cref="UnitMover"/>): it asks <see cref="UnitSpawner"/> which units
/// will spawn, then builds one route per unit type — picking a random avenue and
/// concatenating the start → avenue waypoints → end legs — and registers the
/// resulting <see cref="UnitPathPlan"/> with <see cref="MissionSession"/>.
///
/// The unit is now a pure follower: it receives a precomputed path via
/// <see cref="UnitMover.FollowPath"/> and never runs A* itself.
///
/// Generation is per type and lazy: the first <see cref="GetRoute"/> /
/// <see cref="BuildAndRegisterPlan"/> call after the terrain + avenues are loaded
/// triggers <see cref="GenerateAll"/>, so there is no event-ordering coupling with
/// the spawner.
/// </summary>
public class AStarPathGeneration : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Source of the grid, start/end flags and world<->grid conversion. Auto-found if left empty.")]
    public TerrainDataStore terrainDataStore;
    [Tooltip("Supplies the unit types that will spawn. Auto-found if left empty.")]
    public UnitSpawner unitSpawner;
    [Tooltip("Optional. If set and the save has avenues, each type's route is built through a randomly chosen avenue.")]
    public AvenueRuntimeStore avenueStore;

    [Header("Debug Visual")]
    [Tooltip("Draw the raw pre-loop-cleanup route so it can be compared against the unit's cleaned line.")]
    public bool drawRawPath = true;
    [Tooltip("Colour of the raw (pre-RemoveLoops) route line.")]
    public Color rawPathColor = new Color(1f, 0.25f, 0.2f, 1f);
    [Tooltip("Width of the raw route line.")]
    public float rawPathLineWidth = 0.5f;
    [Tooltip("Height above terrain for the raw line. Keep below UnitMover.pathLineLift so the cleaned line stays on top where they overlap.")]
    public float rawPathLineLift = 0.05f;

    [Tooltip("Mark cells on the cleaned path that come near the path elsewhere — a shortcut candidate.")]
    public bool drawProximityMarkers = true;
    [Tooltip("Horizontal (XZ) distance: flag the pair when the two cells are closer than this.")]
    public float proximityWorldDistance = 3f;
    [Tooltip("Steps to skip before checking begins — the first checked cell is this many steps ahead (e.g. 8 = skip the next 7, start at the 8th).")]
    public int proximityLookaheadSteps = 8;
    [Tooltip("How many consecutive cells to check from the lookahead onward (e.g. 5 = check i, j, k, l, m).")]
    public int proximityWindowSteps = 5;
    [Tooltip("Colour of the proximity marker spheres.")]
    public Color proximityMarkerColor = new Color(0.2f, 0.6f, 1f, 1f);
    [Tooltip("Diameter of the marker spheres in world units.")]
    public float proximityMarkerSize = 1f;
    [Tooltip("Height above terrain at which the marker spheres are centred.")]
    public float proximityMarkerLift = 0.15f;
    [Tooltip("Colour of the line drawn between each detected pair of spheres.")]
    public Color proximityPairLineColor = Color.yellow;
    [Tooltip("Width of the line between a detected pair.")]
    public float proximityPairLineWidth = 0.3f;
    [Tooltip("Replace the detour cells between each pair with a straight 8-direction segment (the unit walks the result).")]
    public bool applyShortcuts = true;
    [Tooltip("Colour of the final straightened (shortcut) path line.")]
    public Color shortcutLineColor = new Color(0.6f, 0.2f, 1f, 1f);
    [Tooltip("Width of the final shortcut path line.")]
    public float shortcutLineWidth = 0.5f;
    [Tooltip("Height above terrain for the final shortcut line. Keep highest so it draws on top.")]
    public float shortcutLineLift = 0.2f;

    /// <summary>
    /// One generated route, keyed by unit type. <see cref="path"/> is the full
    /// concatenated walk (or a direct start→end fallback if any avenue leg failed).
    /// </summary>
    public class GeneratedRoute
    {
        public UnitTypeSO unitType;
        public Vector2Int startCell;
        public Vector2Int goalCell;
        public List<Vector2Int> path = new List<Vector2Int>();
        public List<Vector2Int> requestedWaypoints = new List<Vector2Int>();
        public string avenueTitle = string.Empty;
        public float totalCost;
        public bool failed;

        // Footprint + visual speed read off the type's prefab UnitMover, used for
        // the A* fit check and the upfront time estimate.
        public int unitSize = 5;
        public float moveSpeed = 6f;

        // The concatenated route before RemoveLoops — kept only for the debug line.
        public List<Vector2Int> rawPath = new List<Vector2Int>();
    }

    readonly Dictionary<UnitTypeSO, GeneratedRoute> _routes = new Dictionary<UnitTypeSO, GeneratedRoute>();
    readonly List<GameObject> _rawLineObjects = new List<GameObject>();
    readonly List<GameObject> _proximityMarkers = new List<GameObject>();
    readonly List<(Vector3 a, Vector3 b)> _proximityPairs = new List<(Vector3, Vector3)>();
    readonly List<GameObject> _proximityPairLines = new List<GameObject>();
    readonly List<GameObject> _shortcutLineObjects = new List<GameObject>();
    bool _generated;

    void Awake()
    {
        if (terrainDataStore == null) terrainDataStore = FindFirstObjectByType<TerrainDataStore>();
        if (unitSpawner == null)      unitSpawner      = FindFirstObjectByType<UnitSpawner>();
        if (avenueStore == null)      avenueStore      = FindFirstObjectByType<AvenueRuntimeStore>();
    }

    /// <summary>
    /// Returns the route generated for <paramref name="type"/>, generating all
    /// routes on first use. Null if generation cannot run or the type is unknown.
    /// </summary>
    public GeneratedRoute GetRoute(UnitTypeSO type)
    {
        if (!_generated) GenerateAll();
        return type != null && _routes.TryGetValue(type, out var r) ? r : null;
    }

    /// <summary>
    /// Builds one route per distinct unit type that <see cref="UnitSpawner"/> will
    /// spawn. Safe to call again — clears and rebuilds. Requires the terrain grid
    /// and start/end flags to be loaded.
    /// </summary>
    public void GenerateAll()
    {
        _generated = true;
        _routes.Clear();

        if (terrainDataStore == null || terrainDataStore.grid == null)
        {
            Debug.LogWarning("AStarPathGeneration: TerrainDataStore missing or grid not loaded — no routes generated.");
            return;
        }
        if (!terrainDataStore.StartCell.HasValue || !terrainDataStore.EndCell.HasValue)
        {
            Debug.LogWarning("AStarPathGeneration: save has no start and/or end flag — no routes generated.");
            return;
        }
        if (unitSpawner == null)
        {
            Debug.LogWarning("AStarPathGeneration: no UnitSpawner — no routes generated.");
            return;
        }

        Vector2Int start = terrainDataStore.StartCell.Value;
        Vector2Int end   = terrainDataStore.EndCell.Value;

        foreach (var req in unitSpawner.GetSpawnRequests())
        {
            if (req.unitType == null || _routes.ContainsKey(req.unitType)) continue;

            int   unitSize  = 5;
            float moveSpeed = 6f;
            if (req.prefab != null)
            {
                UnitMover proto = req.prefab.GetComponent<UnitMover>();
                if (proto != null)
                {
                    unitSize  = proto.unitSize;
                    moveSpeed = proto.moveSpeed;
                }
            }

            _routes[req.unitType] = BuildRoute(req.unitType, start, end, unitSize, moveSpeed);
        }

        DrawRawPaths();
        ApplyProximityShortcuts();
    }

    /// <summary>
    /// Creates a <see cref="UnitPathPlan"/> for <paramref name="unitId"/> from the
    /// stored route for <paramref name="type"/>, registers it with
    /// <see cref="MissionSession"/> (creating the session if needed) and returns it.
    /// Returns null if no route exists for the type.
    /// </summary>
    public UnitPathPlan BuildAndRegisterPlan(int unitId, UnitTypeSO type)
    {
        GeneratedRoute route = GetRoute(type);
        if (route == null) return null;

        UnitPathPlan plan = new UnitPathPlan
        {
            unitId             = unitId,
            startCell          = route.startCell,
            goalCell           = route.goalCell,
            path               = new List<Vector2Int>(route.path),
            requestedWaypoints = new List<Vector2Int>(route.requestedWaypoints),
            estimatedSeconds   = route.failed
                ? float.PositiveInfinity
                : AStarPathfinder.CostToSeconds(route.totalCost, terrainDataStore.step, route.moveSpeed),
            failed             = route.failed,
            recordedAt         = Time.time,
        };

        RegisterPlan(plan);
        return plan;
    }

    /// <summary>
    /// Re-routes every live unit after the world changed (e.g. an obstacle was
    /// placed). Mirrors the old per-unit behaviour: a fresh <em>direct</em> A*
    /// from each unit's current cell to its goal (the avenue is not re-walked),
    /// re-registered latest-only and handed back to the mover.
    /// </summary>
    public void RecomputeForLiveUnits()
    {
        if (terrainDataStore == null || terrainDataStore.grid == null) return;

        foreach (var mover in FindObjectsByType<UnitMover>(FindObjectsSortMode.None))
        {
            if (!mover.HasPath) continue;

            Vector2Int from = terrainDataStore.WorldToGrid(mover.transform.position);
            Vector2Int goal = mover.GoalCell;

            List<Vector2Int> newPath = AStarPathfinder.FindPath(
                terrainDataStore.grid,
                terrainDataStore.GridWidth,
                terrainDataStore.GridHeight,
                from, goal,
                out float totalCost,
                unitSize: mover.unitSize,
                unitType: mover.unitType);

            bool failed = newPath.Count <= 1;
            if (failed)
                Debug.LogWarning($"AStarPathGeneration: recompute found no path from {from} to {goal}.");

            UnitPathPlan plan = new UnitPathPlan
            {
                unitId           = mover.unitId,
                startCell        = from,
                goalCell         = goal,
                path             = new List<Vector2Int>(newPath),
                estimatedSeconds = failed
                    ? float.PositiveInfinity
                    : AStarPathfinder.CostToSeconds(totalCost, terrainDataStore.step, mover.moveSpeed),
                failed           = failed,
                recordedAt       = Time.time,
            };

            RegisterPlan(plan);
            mover.FollowPath(terrainDataStore, mover.unitType, goal, newPath, plan);
        }
    }

    GeneratedRoute BuildRoute(UnitTypeSO type, Vector2Int start, Vector2Int end, int unitSize, float moveSpeed)
    {
        var route = new GeneratedRoute
        {
            unitType  = type,
            startCell = start,
            goalCell  = end,
            unitSize  = unitSize,
            moveSpeed = moveSpeed,
        };

        AvenueData avenue = PickRandomAvenue();
        if (avenue != null)
        {
            route.avenueTitle        = avenue.title ?? string.Empty;
            route.requestedWaypoints = new List<Vector2Int>(avenue.waypoints);
        }

        List<Vector2Int> combined = BuildRoutePath(
            start, route.requestedWaypoints, end, type, unitSize, out float routeCost);

        route.rawPath = new List<Vector2Int>(combined);
        combined = RemoveLoops(combined);

        if (combined.Count > 1)
        {
            route.path      = combined;
            route.totalCost = routeCost;
            route.failed    = false;
            return route;
        }

        // Route through the avenue failed (or there was no avenue) — fall back to a
        // direct start→end path, matching the old GoToRoute fallback.
        if (route.requestedWaypoints.Count > 0)
            Debug.LogWarning($"AStarPathGeneration: route through {route.requestedWaypoints.Count} avenue waypoint(s) failed for '{type?.typeName}'; falling back to direct path.");

        List<Vector2Int> direct = AStarPathfinder.FindPath(
            terrainDataStore.grid,
            terrainDataStore.GridWidth,
            terrainDataStore.GridHeight,
            start, end,
            out float directCost,
            unitSize: unitSize,
            unitType: type);

        route.path      = direct;
        route.totalCost = directCost;
        route.failed    = direct.Count <= 1;
        if (route.failed)
            Debug.LogWarning($"AStarPathGeneration: no path found from {start} to {end} for '{type?.typeName}'.");
        return route;
    }

    /// <summary>
    /// Removes loops from <paramref name="path"/>. When a cell repeats, the first
    /// occurrence is kept and everything after it up to and including the repeat is
    /// dropped; applied across the whole path so the result has no repeated cells.
    ///
    /// Contiguity is preserved: when cell X (kept at result index f) is revisited at
    /// path position p, the next cell kept is path[p+1], and X→path[p+1] is exactly
    /// the original adjacent step path[p]→path[p+1] (path[p] == X), so it stays a
    /// valid grid-adjacent move.
    /// </summary>
    static List<Vector2Int> RemoveLoops(List<Vector2Int> path)
    {
        if (path == null || path.Count < 2) return path;

        var result        = new List<Vector2Int>(path.Count);
        var indexInResult = new Dictionary<Vector2Int, int>(path.Count);

        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int cell = path[i];

            if (indexInResult.TryGetValue(cell, out int firstIdx))
            {
                // Loop hit: drop everything added after the first occurrence and skip
                // this repeat. RemoveAt from the tail is O(1) (last element each time).
                for (int k = result.Count - 1; k > firstIdx; k--)
                {
                    indexInResult.Remove(result[k]);
                    result.RemoveAt(k);
                }
            }
            else
            {
                indexInResult[cell] = result.Count;
                result.Add(cell);
            }
        }

        return result;
    }

    /// <summary>
    /// Runs A* once per leg (start → each waypoint → finalGoal) and concatenates
    /// the segments into one path. Returns an empty list if any leg has no path.
    /// Moved verbatim from the old UnitMover.BuildRoutePath.
    /// </summary>
    List<Vector2Int> BuildRoutePath(Vector2Int startCell, IReadOnlyList<Vector2Int> avenueWaypoints,
                                    Vector2Int finalGoal, UnitTypeSO unitType, int unitSize, out float totalCost)
    {
        List<Vector2Int> combined = new List<Vector2Int>();
        Vector2Int from = startCell;
        totalCost = 0f;

        int stops = avenueWaypoints?.Count ?? 0;
        for (int i = 0; i <= stops; i++)
        {
            Vector2Int to = i < stops ? avenueWaypoints[i] : finalGoal;

            List<Vector2Int> segment = AStarPathfinder.FindPath(
                terrainDataStore.grid,
                terrainDataStore.GridWidth,
                terrainDataStore.GridHeight,
                from, to,
                out float legCost,
                unitSize: unitSize,
                unitType: unitType);

            // A* returns an empty list when no path exists — treat the whole route as failed.
            if (segment.Count == 0)
            {
                totalCost = 0f;
                return new List<Vector2Int>();
            }

            totalCost += legCost;

            // First leg: keep every cell. Later legs: skip the first cell which duplicates the
            // previous leg's last cell, otherwise the unit pauses on the join.
            int startIdx = combined.Count == 0 ? 0 : 1;
            for (int s = startIdx; s < segment.Count; s++)
                combined.Add(segment[s]);

            from = to;
        }

        return combined;
    }

    AvenueData PickRandomAvenue()
    {
        if (avenueStore == null) return null;
        var list = avenueStore.Avenues;
        if (list == null || list.Count == 0) return null;
        return list[Random.Range(0, list.Count)];
    }

    /// <summary>
    /// Draws the raw, pre-<see cref="RemoveLoops"/> route per type as a coloured
    /// line so it can be compared against the unit's cleaned line. Rebuilt on every
    /// call so a regenerate never stacks stale lines.
    /// </summary>
    void DrawRawPaths()
    {
        for (int i = 0; i < _rawLineObjects.Count; i++)
            if (_rawLineObjects[i] != null) Destroy(_rawLineObjects[i]);
        _rawLineObjects.Clear();

        if (!drawRawPath || terrainDataStore == null) return;

        foreach (var kv in _routes)
        {
            GeneratedRoute route = kv.Value;
            // rawPath is only captured for the avenue-route branch (the one that
            // gets de-looped). A direct fallback has no "before" to compare.
            if (route.rawPath == null || route.rawPath.Count < 2) continue;

            string label = route.unitType != null ? route.unitType.typeName : "Unit";
            _rawLineObjects.Add(BuildCellLine($"RawPathLine ({label})", route.rawPath,
                rawPathColor, rawPathLineWidth, rawPathLineLift));
        }
    }

    GameObject BuildCellLine(string objectName, List<Vector2Int> cells, Color color, float width, float lift)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.startWidth    = width;
        lr.endWidth      = width;

        string[] candidates =
        {
            "Universal Render Pipeline/Particles/Unlit",
            "Sprites/Default",
            "Legacy Shaders/Particles/Alpha Blended",
            "Unlit/Color",
        };
        Shader shader = null;
        foreach (var name in candidates)
        {
            shader = Shader.Find(name);
            if (shader != null) break;
        }

        var mat = shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
        mat.color     = color;
        lr.material   = mat;
        lr.startColor = color;
        lr.endColor   = color;

        lr.positionCount = cells.Count;
        for (int i = 0; i < cells.Count; i++)
        {
            Vector3 wp = terrainDataStore.GridToWorld(cells[i]);
            wp.y = terrainDataStore.GetRoundedHeight(cells[i].x, cells[i].y) + lift;
            lr.SetPosition(i, wp);
        }
        return go;
    }

    /// <summary>
    /// Walks the cleaned path with an anchor k. It scans a window of
    /// <see cref="proximityWindowSteps"/> cells starting
    /// <see cref="proximityLookaheadSteps"/> steps ahead, scans the whole window
    /// and takes the LAST (farthest) cell within <see cref="proximityWorldDistance"/>
    /// of k in the horizontal (XZ) plane. Both k and that cell get a marker, and
    /// the anchor jumps forward to it — collapsing as much of the detour as
    /// possible. No hit → anchor steps by one.
    ///
    /// When <see cref="applyShortcuts"/>, each detected pair's interior cells are
    /// then replaced by a straight 8-direction (Bresenham) segment — that becomes
    /// the path the unit walks — and the realized path is drawn on top in purple.
    /// All debug viz (spheres, pair lines, purple line) is gated by
    /// <see cref="drawProximityMarkers"/>. Rebuilt on every call.
    /// </summary>
    void ApplyProximityShortcuts()
    {
        for (int i = 0; i < _proximityMarkers.Count; i++)
            if (_proximityMarkers[i] != null) Destroy(_proximityMarkers[i]);
        for (int i = 0; i < _proximityPairLines.Count; i++)
            if (_proximityPairLines[i] != null) Destroy(_proximityPairLines[i]);
        for (int i = 0; i < _shortcutLineObjects.Count; i++)
            if (_shortcutLineObjects[i] != null) Destroy(_shortcutLineObjects[i]);
        _proximityMarkers.Clear();
        _proximityPairLines.Clear();
        _shortcutLineObjects.Clear();
        _proximityPairs.Clear();

        if (terrainDataStore == null) return;
        if (!drawProximityMarkers && !applyShortcuts) return;
        if (proximityLookaheadSteps < 1 || proximityWindowSteps < 1) return;

        Material mat = MakeMarkerMaterial(proximityMarkerColor);
        float sqrThreshold = proximityWorldDistance * proximityWorldDistance;

        foreach (var kv in _routes)
        {
            List<Vector2Int> path = kv.Value.path;
            if (path == null || path.Count <= proximityLookaheadSteps) continue;

            // World position per cell, computed once.
            var world = new Vector3[path.Count];
            for (int i = 0; i < path.Count; i++)
            {
                Vector3 w = terrainDataStore.GridToWorld(path[i]);
                w.y = terrainDataStore.GetRoundedHeight(path[i].x, path[i].y);
                world[i] = w;
            }

            // Forward-jumping scan. Always collects the pair index ranges to splice;
            // only collects spheres + pair endpoints when debug viz is on.
            var marked   = new HashSet<int>();
            var pairsIdx = new List<(int a, int b)>();
            int k = 0;
            while (true)
            {
                int from = k + proximityLookaheadSteps;
                if (from >= path.Count) break;  // no window left for this or any later k
                int to = Mathf.Min(from + proximityWindowSteps, path.Count);  // exclusive

                int foundJ = -1;
                for (int j = from; j < to; j++)
                {
                    float dx = world[k].x - world[j].x;
                    float dz = world[k].z - world[j].z;
                    if (dx * dx + dz * dz < sqrThreshold)
                        foundJ = j;  // keep scanning — overwrite so the LAST (farthest) match wins
                }

                if (foundJ >= 0)
                {
                    pairsIdx.Add((k, foundJ));

                    if (drawProximityMarkers)
                    {
                        marked.Add(k);
                        marked.Add(foundJ);

                        Vector3 pa = world[k];      pa.y += proximityMarkerLift;
                        Vector3 pb = world[foundJ]; pb.y += proximityMarkerLift;
                        _proximityPairs.Add((pa, pb));
                    }
                }

                // Hit: jump the anchor forward to the matched cell (the farthest in the
                // window). foundJ > k always (lookahead ≥ 1), so the anchor strictly
                // advances — no infinite loop.
                k = foundJ >= 0 ? foundJ : k + 1;
            }

            string label = kv.Value.unitType != null ? kv.Value.unitType.typeName : "Unit";

            if (drawProximityMarkers)
                foreach (int idx in marked)
                    _proximityMarkers.Add(MakeMarker($"ProximityMarker ({label})", world[idx], mat));

            // Make the shortcuts real: replace each pair's interior with a straight
            // 8-direction segment. This is the path the unit will actually walk.
            if (applyShortcuts && pairsIdx.Count > 0)
                kv.Value.path = BuildShortcutPath(path, pairsIdx);

            // The final realized path, drawn on top in purple.
            if (drawProximityMarkers)
                _shortcutLineObjects.Add(BuildCellLine($"ShortcutPathLine ({label})",
                    kv.Value.path, shortcutLineColor, shortcutLineWidth, shortcutLineLift));
        }

        if (drawProximityMarkers)
            for (int i = 0; i < _proximityPairs.Count; i++)
                _proximityPairLines.Add(BuildPairLine(_proximityPairs[i].a, _proximityPairs[i].b));
    }

    /// <summary>
    /// Splices the detours out of <paramref name="original"/>: for each (a,b) pair
    /// (sorted ascending, non-overlapping) the cells strictly between a and b are
    /// replaced by a straight 8-direction line from original[a] to original[b].
    /// Cells outside any pair are kept as-is.
    /// </summary>
    static List<Vector2Int> BuildShortcutPath(List<Vector2Int> original, List<(int a, int b)> pairs)
    {
        var result = new List<Vector2Int>(original.Count);
        int cursor = 0;

        foreach (var (a, b) in pairs)
        {
            for (int t = cursor; t <= a; t++)
                result.Add(original[t]);

            List<Vector2Int> seg = StraightLine8(original[a], original[b]);
            // seg[0] == original[a], which is already the last cell in result.
            int segStart = (result.Count > 0 && result[result.Count - 1] == seg[0]) ? 1 : 0;
            for (int s = segStart; s < seg.Count; s++)
                result.Add(seg[s]);

            cursor = b + 1;
        }

        for (int t = cursor; t < original.Count; t++)
            result.Add(original[t]);

        return result;
    }

    /// <summary>
    /// Bresenham line between two grid cells, restricted to the 8 grid directions
    /// (a diagonal step when both axes advance in the same iteration). Inclusive of
    /// both endpoints — as straight as an 8-connected grid allows.
    /// </summary>
    static List<Vector2Int> StraightLine8(Vector2Int a, Vector2Int b)
    {
        var line = new List<Vector2Int>();

        int x0 = a.x, y0 = a.y;
        int x1 = b.x, y1 = b.y;
        int dx = Mathf.Abs(x1 - x0), dy = Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1;
        int sy = y0 < y1 ? 1 : -1;
        int err = dx - dy;

        while (true)
        {
            line.Add(new Vector2Int(x0, y0));
            if (x0 == x1 && y0 == y1) break;

            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x0 += sx; }
            if (e2 <  dx) { err += dx; y0 += sy; }
        }

        return line;
    }

    GameObject BuildPairLine(Vector3 a, Vector3 b)
    {
        var go = new GameObject("ProximityPairLine");
        go.transform.SetParent(transform, false);

        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.startWidth    = proximityPairLineWidth;
        lr.endWidth      = proximityPairLineWidth;
        lr.positionCount = 2;
        lr.SetPosition(0, a);
        lr.SetPosition(1, b);

        string[] candidates =
        {
            "Universal Render Pipeline/Particles/Unlit",
            "Sprites/Default",
            "Legacy Shaders/Particles/Alpha Blended",
            "Unlit/Color",
        };
        Shader shader = null;
        foreach (var name in candidates)
        {
            shader = Shader.Find(name);
            if (shader != null) break;
        }

        var lineMat = shader != null ? new Material(shader) : new Material(Shader.Find("Standard"));
        lineMat.color = proximityPairLineColor;
        lr.material   = lineMat;
        lr.startColor = proximityPairLineColor;
        lr.endColor   = proximityPairLineColor;
        return go;
    }

    GameObject MakeMarker(string objectName, Vector3 worldPos, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = objectName;
        go.transform.SetParent(transform, false);

        Vector3 p = worldPos;
        p.y += proximityMarkerLift;
        go.transform.position   = p;
        go.transform.localScale = Vector3.one * proximityMarkerSize;

        Destroy(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    Material MakeMarkerMaterial(Color color)
    {
        string[] candidates =
        {
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Unlit/Color",
        };
        Shader shader = null;
        foreach (var name in candidates)
        {
            shader = Shader.Find(name);
            if (shader != null) break;
        }

        var mat = new Material(shader != null ? shader : Shader.Find("Standard"));
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        mat.color = color;
        return mat;
    }

    void RegisterPlan(UnitPathPlan plan)
    {
        // The scene owns the session lifecycle now: this is the only place a MissionSession is created.
        if (MissionSession.Instance == null)
            new GameObject("MissionSession").AddComponent<MissionSession>();

        if (terrainDataStore != null)
            MissionSession.Instance.saveFileName = terrainDataStore.SaveFileName;

        MissionSession.Instance.RegisterPlan(plan);
    }
}
