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

    [Header("Proximity Shortcuts")]
    [Tooltip("Horizontal (XZ) distance: flag the pair when the two cells are closer than this.")]
    public float proximityWorldDistance = 3f;
    [Tooltip("Steps to skip before checking begins — the first checked cell is this many steps ahead (e.g. 8 = skip the next 7, start at the 8th).")]
    public int proximityLookaheadSteps = 8;
    [Tooltip("How many consecutive cells to check from the lookahead onward (e.g. 5 = check i, j, k, l, m).")]
    public int proximityWindowSteps = 5;
    [Tooltip("Replace the detour cells between each pair with a straight 8-direction segment (the unit walks the result).")]
    public bool applyShortcuts = true;

    [Header("Corner Bevel")]
    [Tooltip("Minimum turn angle (degrees) for a vertex to count as a sharp corner. The 16-direction grid bends by ~22-27° at its gentlest, so keep this above that to skip stair-step jitter and only catch real corners.")]
    public float cornerAngleThresholdDeg = 60f;
    [Tooltip("Bevel each detected sharp corner: step a few cells back and forward along the path, then replace the corner in between with a curve. NOTE: the bevel is geometric and ignores obstacles/blocked biomes/steep slopes.")]
    public bool useBeveledPathForUnit = true;
    [Tooltip("How many path cells to step outward from each sharp corner along each side before bridging. Larger = a bigger bite taken out of the corner.")]
    [Range(1, 20)]
    public int bevelStepsPerSide = 4;
    [Tooltip("How many straight segments the bevel curve is split into. 1 = a single straight chamfer (no rounding). Higher = a progressively smoother/rounder corner.")]
    [Range(1, 16)]
    public int bevelSegments = 1;

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

        // The post-proximity-shortcut path, snapshotted before beveling. Stable
        // source for corner detection + the bevel so repeated bevel passes never
        // compound; also the fallback when the bevel is disabled or unusable.
        public List<Vector2Int> basePath = new List<Vector2Int>();
    }

    readonly Dictionary<UnitTypeSO, GeneratedRoute> _routes = new Dictionary<UnitTypeSO, GeneratedRoute>();
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

        ApplyShortcuts();

        // Snapshot the post-shortcut path as the stable bevel source before any
        // beveling mutates route.path.
        foreach (var kv in _routes)
            kv.Value.basePath = new List<Vector2Int>(kv.Value.path);

        ApplyBevel();
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
                out _,
                unitSize: mover.unitSize,
                unitType: mover.unitType);

            bool failed = newPath.Count <= 1;
            if (failed)
                Debug.LogWarning($"AStarPathGeneration: recompute found no path from {from} to {goal}.");

            // Estimate uses obstacle-free A* from the current position to the goal.
            // The catch-up A* the mover runs at runtime reroutes around obstacles when
            // it can, so its walked time approximates this cost.
            float estimateSeconds = float.PositiveInfinity;
            if (!failed)
            {
                AStarPathfinder.FindPath(
                    terrainDataStore.grid,
                    terrainDataStore.GridWidth,
                    terrainDataStore.GridHeight,
                    from, goal,
                    out float estimateCost,
                    unitSize: mover.unitSize,
                    unitType: mover.unitType,
                    ignoreObstacles: true);
                estimateSeconds = AStarPathfinder.CostToSeconds(estimateCost, terrainDataStore.step, mover.moveSpeed);
            }

            UnitPathPlan plan = new UnitPathPlan
            {
                unitId           = mover.unitId,
                startCell        = from,
                goalCell         = goal,
                path             = new List<Vector2Int>(newPath),
                estimatedSeconds = estimateSeconds,
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
            start, route.requestedWaypoints, end, type, unitSize, out _);

        combined = RemoveLoops(combined);

        if (combined.Count > 1)
        {
            route.path = combined;
            // Estimate uses the obstacle-free A* cost through the same waypoints. The
            // real unit's catch-up A* reroutes around obstacles when avoidable, so its
            // walked time approximates this number rather than the obstacle-aware cost
            // of the bevel-mutated path.
            BuildRoutePath(start, route.requestedWaypoints, end, type, unitSize,
                           out float estimateCost, ignoreObstacles: true);
            route.totalCost = estimateCost;
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
            out _,
            unitSize: unitSize,
            unitType: type);

        route.path   = direct;
        route.failed = direct.Count <= 1;
        if (route.failed)
        {
            route.totalCost = 0f;
            Debug.LogWarning($"AStarPathGeneration: no path found from {start} to {end} for '{type?.typeName}'.");
        }
        else
        {
            AStarPathfinder.FindPath(
                terrainDataStore.grid,
                terrainDataStore.GridWidth,
                terrainDataStore.GridHeight,
                start, end,
                out float directEstimateCost,
                unitSize: unitSize,
                unitType: type,
                ignoreObstacles: true);
            route.totalCost = directEstimateCost;
        }
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
    /// When <paramref name="ignoreObstacles"/> is true the legs ignore the obstacle
    /// cost layer entirely (biome and slope only), so the returned totalCost is
    /// the obstacle-free A* cost used to derive estimatedSeconds.
    /// </summary>
    List<Vector2Int> BuildRoutePath(Vector2Int startCell, IReadOnlyList<Vector2Int> avenueWaypoints,
                                    Vector2Int finalGoal, UnitTypeSO unitType, int unitSize, out float totalCost,
                                    bool ignoreObstacles = false)
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
                unitType: unitType,
                ignoreObstacles: ignoreObstacles);

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
    /// Walks the cleaned path with an anchor k. It scans a window of
    /// <see cref="proximityWindowSteps"/> cells starting
    /// <see cref="proximityLookaheadSteps"/> steps ahead and takes the LAST (farthest)
    /// cell within <see cref="proximityWorldDistance"/> of k in the horizontal plane,
    /// then jumps the anchor forward to it — collapsing as much of the detour as
    /// possible. Each detected pair's interior cells are replaced by a straight
    /// 8-direction (Bresenham) segment — that becomes the path the unit walks.
    /// Gated by <see cref="applyShortcuts"/>.
    /// </summary>
    void ApplyShortcuts()
    {
        if (terrainDataStore == null) return;
        if (!applyShortcuts) return;
        if (proximityLookaheadSteps < 1 || proximityWindowSteps < 1) return;

        float sqrThreshold = proximityWorldDistance * proximityWorldDistance;

        foreach (var kv in _routes)
        {
            List<Vector2Int> path = kv.Value.path;
            if (path == null || path.Count <= proximityLookaheadSteps) continue;

            // World position per cell, computed once. Only XZ matters for proximity.
            var world = new Vector3[path.Count];
            for (int i = 0; i < path.Count; i++)
                world[i] = terrainDataStore.GridToWorld(path[i]);

            // Forward-jumping scan. Collects pair index ranges to splice.
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
                    pairsIdx.Add((k, foundJ));

                // Hit: jump the anchor forward to the matched cell (the farthest in the
                // window). foundJ > k always (lookahead ≥ 1), so the anchor strictly
                // advances — no infinite loop.
                k = foundJ >= 0 ? foundJ : k + 1;
            }

            if (pairsIdx.Count > 0)
                kv.Value.path = BuildShortcutPath(path, pairsIdx);
        }
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

    /// <summary>
    /// Returns the indices into <paramref name="path"/> of every interior vertex
    /// where the step direction turns by at least <paramref name="minAngleDeg"/>
    /// degrees. Endpoints are never corners. Inside a run of constant direction the
    /// per-step delta is identical, so the deviation only has to be measured between
    /// the step arriving at the vertex and the step leaving it.
    /// </summary>
    public static List<int> FindCornerIndices(List<Vector2Int> path, float minAngleDeg)
    {
        var corners = new List<int>();
        if (path == null || path.Count < 3) return corners;

        for (int i = 1; i < path.Count - 1; i++)
        {
            Vector2Int inDelta  = path[i]     - path[i - 1];
            Vector2Int outDelta = path[i + 1] - path[i];
            if (inDelta == outDelta) continue;  // straight through this cell

            Vector2 a = ((Vector2)inDelta).normalized;
            Vector2 b = ((Vector2)outDelta).normalized;
            float dot = Mathf.Clamp(Vector2.Dot(a, b), -1f, 1f);
            float deviation = Mathf.Acos(dot) * Mathf.Rad2Deg;
            if (deviation >= minAngleDeg)
                corners.Add(i);
        }
        return corners;
    }

    /// <summary>
    /// Builds each route's beveled curve from its stable
    /// <see cref="GeneratedRoute.basePath"/>, rasterizes it back onto the grid as
    /// 8-connected steps, and — when <see cref="useBeveledPathForUnit"/> — makes
    /// that the route's walked <see cref="GeneratedRoute.path"/>. Idempotent: always
    /// derives from basePath, never from an already-beveled path.
    /// </summary>
    void ApplyBevel()
    {
        if (terrainDataStore == null) return;

        foreach (var kv in _routes)
        {
            GeneratedRoute route = kv.Value;
            List<Vector2Int> src = (route.basePath != null && route.basePath.Count > 0)
                ? route.basePath : route.path;
            if (src == null || src.Count < 2) continue;

            List<int> corners        = FindCornerIndices(src, cornerAngleThresholdDeg);
            List<Vector3> curve      = BuildBeveledCurve(src, corners);
            List<Vector2Int> beveled = RasterizeCurveToGrid(curve);

            // Geometric bevel — if rasterizing collapsed it, fall back to the base
            // path so the unit always has a valid route to walk.
            bool usable = beveled.Count > 1;
            route.path = (useBeveledPathForUnit && usable)
                ? beveled
                : new List<Vector2Int>(src);
        }
    }

    /// <summary>
    /// Snaps a world XZ curve back to grid cells: each point is rounded to its
    /// cell and consecutive cells are joined with an 8-connected Bresenham line
    /// (<see cref="StraightLine8"/>), so every step is one of the 16
    /// <see cref="CellData.Directions"/> the mover understands. Self-intersections
    /// the bevel may introduce are removed with <see cref="RemoveLoops"/>.
    /// </summary>
    List<Vector2Int> RasterizeCurveToGrid(List<Vector3> curve)
    {
        var cells = new List<Vector2Int>();
        if (curve == null || curve.Count == 0) return cells;

        Vector2Int prev = ClampToGrid(terrainDataStore.WorldToGrid(curve[0]));
        cells.Add(prev);
        for (int i = 1; i < curve.Count; i++)
        {
            Vector2Int cur = ClampToGrid(terrainDataStore.WorldToGrid(curve[i]));
            if (cur == prev) continue;

            List<Vector2Int> seg = StraightLine8(prev, cur);
            for (int s = 1; s < seg.Count; s++) cells.Add(seg[s]);
            prev = cur;
        }
        return RemoveLoops(cells);
    }

    /// <summary>
    /// Returns <paramref name="path"/> as an XZ world polyline with every sharp
    /// <paramref name="corners"/> beveled. For each corner at index c the cells
    /// from c-<see cref="bevelStepsPerSide"/> to c+<see cref="bevelStepsPerSide"/>
    /// are dropped and replaced by a quadratic Bézier with the two side points as
    /// endpoints and the corner cell as the control point, sampled into
    /// <see cref="bevelSegments"/> straight segments. The reach is clamped so a
    /// bevel never crosses the previous bevel, the neighbouring corner, or the
    /// path ends; a corner whose window collapses to nothing is left untouched.
    /// Y is whatever <see cref="CellWorld"/> returns and is ignored by
    /// <see cref="RasterizeCurveToGrid"/> (XZ only).
    /// </summary>
    List<Vector3> BuildBeveledCurve(List<Vector2Int> path, List<int> corners)
    {
        int n = path.Count;
        var result = new List<Vector3>();

        if (corners.Count == 0)
        {
            for (int j = 0; j < n; j++) result.Add(CellWorld(path[j]));
            return result;
        }

        int w = Mathf.Max(1, bevelStepsPerSide);
        int cursor = 0;

        for (int ci = 0; ci < corners.Count; ci++)
        {
            int c = corners[ci];

            // Reach w each side, but never cross the previous bevel (cursor), the
            // next sharp corner, or the path ends.
            int nextLimit = (ci + 1 < corners.Count) ? corners[ci + 1] : n - 1;
            int leftIdx   = Mathf.Max(c - w, cursor);
            int rightIdx  = Mathf.Min(c + w, nextLimit);

            // Window collapsed (corners too close): leave this corner as-is.
            if (rightIdx - leftIdx < 2) continue;

            // Copy untouched cells up to and including the left side point S1.
            for (int k = cursor; k <= leftIdx; k++)
                result.Add(CellWorld(path[k]));

            // Quadratic Bézier in XZ: S1 → (corner = control) → S2, sampled into
            // bevelSegments straight pieces.
            Vector3 p0 = CellWorld(path[leftIdx]);
            Vector3 p1 = CellWorld(path[c]);
            Vector3 p2 = CellWorld(path[rightIdx]);

            int segs = Mathf.Max(1, bevelSegments);
            // p0 == path[leftIdx] is already the last copied cell, so emit the
            // samples for t = 1/segs .. 1 (the t = 1 sample is exactly S2).
            for (int seg = 1; seg <= segs; seg++)
            {
                float t = (float)seg / segs;
                float u = 1f - t;
                float bx = u * u * p0.x + 2f * u * t * p1.x + t * t * p2.x;
                float bz = u * u * p0.z + 2f * u * t * p1.z + t * t * p2.z;
                result.Add(new Vector3(bx, 0f, bz));
            }

            cursor = rightIdx + 1;
        }

        for (int idx = cursor; idx < n; idx++)
            result.Add(CellWorld(path[idx]));

        return result;
    }

    Vector3 CellWorld(Vector2Int cell) => terrainDataStore.GridToWorld(cell);

    Vector2Int ClampToGrid(Vector2Int g) => new Vector2Int(
        Mathf.Clamp(g.x, 0, terrainDataStore.GridWidth  - 1),
        Mathf.Clamp(g.y, 0, terrainDataStore.GridHeight - 1));

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
