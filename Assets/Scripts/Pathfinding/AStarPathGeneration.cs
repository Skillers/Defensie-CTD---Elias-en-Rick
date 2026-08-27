using System.Collections.Generic;
using UnityEngine;

/// <summary>Builds one A* route per unit type (start → avenue waypoints → end) and registers the plans with <see cref="MissionSession"/>.</summary>
public class AStarPathGeneration : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Grid, flags and world<->grid conversion. Auto-found if empty.")]
    public TerrainDataStore terrainDataStore;
    [Tooltip("Supplies the unit types that will spawn. Auto-found if empty.")]
    public UnitSpawner unitSpawner;
    [Tooltip("Optional source of avenues to route through.")]
    public AvenueRuntimeStore avenueStore;

    [Header("Proximity Shortcuts")]
    [Tooltip("Max XZ distance between two cells to count as a shortcut pair.")]
    public float proximityWorldDistance = 3f;
    [Tooltip("Steps ahead of the anchor where the check window starts.")]
    public int proximityLookaheadSteps = 8;
    [Tooltip("Consecutive cells checked per window.")]
    public int proximityWindowSteps = 5;
    [Tooltip("Replace detected detours with straight segments.")]
    public bool applyShortcuts = true;

    [Header("Corner Bevel")]
    [Tooltip("Minimum turn angle (degrees) to count as a sharp corner. Keep above ~27 so grid stair-steps don't count.")]
    public float cornerAngleThresholdDeg = 60f;
    [Tooltip("Round sharp corners with a bevel curve. Geometric: ignores obstacles and slopes.")]
    public bool useBeveledPathForUnit = true;
    [Tooltip("Cells stepped outward from a corner on each side before bridging.")]
    [Range(1, 20)]
    public int bevelStepsPerSide = 4;
    [Tooltip("Straight segments per bevel curve. 1 = flat chamfer.")]
    [Range(1, 16)]
    public int bevelSegments = 1;

    /// <summary>One generated route for a unit type.</summary>
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

        // Read off the type's prefab UnitMover.
        public int unitSize = 5;
        public float moveSpeed = 6f;

        // Post-shortcut path; the bevel always derives from this so repeated passes don't compound.
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

    /// <summary>Route for the given type, generating all routes on first use. Null if unknown.</summary>
    public GeneratedRoute GetRoute(UnitTypeSO type)
    {
        if (!_generated) GenerateAll();
        return type != null && _routes.TryGetValue(type, out var r) ? r : null;
    }

    /// <summary>Builds one route per unit type the spawner will spawn. Clears and rebuilds on repeat calls.</summary>
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

        // Snapshot the post-shortcut path before beveling mutates route.path.
        foreach (var kv in _routes)
            kv.Value.basePath = new List<Vector2Int>(kv.Value.path);

        ApplyBevel();
    }

    /// <summary>Creates and registers a plan for the unit from its type's route. Null if no route exists.</summary>
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

    /// <summary>Re-routes every live unit directly to its goal after the world changed. Avenues are not re-walked.</summary>
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

            // Estimate is obstacle-free: the mover reroutes around obstacles at runtime, so walked time tracks this cost.
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
            // Obstacle-free cost through the same waypoints, used for the time estimate.
            BuildRoutePath(start, route.requestedWaypoints, end, type, unitSize,
                           out float estimateCost, ignoreObstacles: true);
            route.totalCost = estimateCost;
            route.failed    = false;
            return route;
        }

        // Avenue route failed (or no avenue): fall back to a direct start→end path.
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

    /// <summary>Removes loops: on a revisit, everything after the cell's first occurrence is dropped.</summary>
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
                // Tail RemoveAt is O(1).
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

    /// <summary>Concatenated A* legs start → waypoints → goal. Empty if any leg fails.</summary>
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

            // Empty segment = no path: fail the whole route.
            if (segment.Count == 0)
            {
                totalCost = 0f;
                return new List<Vector2Int>();
            }

            totalCost += legCost;

            // Later legs skip their first cell; it duplicates the previous leg's last cell.
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

    /// <summary>Collapses detours: where the path comes back near itself, the cells between are replaced with a straight segment.</summary>
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

            var world = new Vector3[path.Count];
            for (int i = 0; i < path.Count; i++)
                world[i] = terrainDataStore.GridToWorld(path[i]);

            var pairsIdx = new List<(int a, int b)>();
            int k = 0;
            while (true)
            {
                int from = k + proximityLookaheadSteps;
                if (from >= path.Count) break;
                int to = Mathf.Min(from + proximityWindowSteps, path.Count);  // exclusive

                int foundJ = -1;
                for (int j = from; j < to; j++)
                {
                    float dx = world[k].x - world[j].x;
                    float dz = world[k].z - world[j].z;
                    if (dx * dx + dz * dz < sqrThreshold)
                        foundJ = j;  // last (farthest) match wins
                }

                if (foundJ >= 0)
                    pairsIdx.Add((k, foundJ));

                // foundJ > k always (lookahead >= 1), so the anchor strictly advances.
                k = foundJ >= 0 ? foundJ : k + 1;
            }

            if (pairsIdx.Count > 0)
                kv.Value.path = BuildShortcutPath(path, pairsIdx);
        }
    }

    /// <summary>Replaces the cells between each (a,b) pair with a straight 8-direction line.</summary>
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

    /// <summary>Bresenham line between two cells using the 8 grid directions, endpoints inclusive.</summary>
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

    /// <summary>Indices of interior vertices where the step direction turns by at least minAngleDeg.</summary>
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

    /// <summary>Rebuilds each route's path from basePath with sharp corners beveled.</summary>
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

            // Fall back to the base path if rasterizing collapsed the bevel.
            bool usable = beveled.Count > 1;
            route.path = (useBeveledPathForUnit && usable)
                ? beveled
                : new List<Vector2Int>(src);
        }
    }

    /// <summary>Snaps a world XZ curve back to 8-connected grid steps and removes loops.</summary>
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

    /// <summary>XZ world polyline of the path with each sharp corner replaced by a sampled quadratic Bézier.</summary>
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

            // Never cross the previous bevel, the next corner, or the path ends.
            int nextLimit = (ci + 1 < corners.Count) ? corners[ci + 1] : n - 1;
            int leftIdx   = Mathf.Max(c - w, cursor);
            int rightIdx  = Mathf.Min(c + w, nextLimit);

            // Window collapsed (corners too close): leave this corner as-is.
            if (rightIdx - leftIdx < 2) continue;

            for (int k = cursor; k <= leftIdx; k++)
                result.Add(CellWorld(path[k]));

            // Quadratic Bézier in XZ with the corner cell as control point.
            Vector3 p0 = CellWorld(path[leftIdx]);
            Vector3 p1 = CellWorld(path[c]);
            Vector3 p2 = CellWorld(path[rightIdx]);

            int segs = Mathf.Max(1, bevelSegments);
            // p0 is already the last copied cell, so start sampling at t = 1/segs.
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
        // Sole creation point for MissionSession.
        if (MissionSession.Instance == null)
            new GameObject("MissionSession").AddComponent<MissionSession>();

        if (terrainDataStore != null)
            MissionSession.Instance.saveFileName = terrainDataStore.SaveFileName;

        MissionSession.Instance.RegisterPlan(plan);
    }
}
