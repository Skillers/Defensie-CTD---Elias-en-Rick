using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Walks a unit along an A* path between two grid cells.
/// Configure with <see cref="Initialize"/> (typical) or by setting the inspector
/// fields and calling <see cref="GoTo"/> manually.
/// Speed is divided by the biome cost AND the unit's slope multiplier of the cell
/// being stepped from, so visual movement matches A*'s path cost.
/// Y is snapped to the terrain's rounded height every frame so the unit follows hills.
/// </summary>
public class UnitMover : MonoBehaviour
{
    [Header("References")]
    public TerrainDataStore terrainDataStore;

    [Header("Unit")]
    [Tooltip("Resolves per-biome movement costs and slope rules.")]
    public UnitTypeSO unitType;
    [Tooltip("Square footprint in cells passed to A*'s CanFit check.")]
    public int unitSize = 5;
    [Tooltip("Set by UnitSpawner. Used as the key when registering this unit's plan in MissionSession.")]
    [HideInInspector] public int unitId;

    [Header("Movement")]
    public float moveSpeed   = 6f;
    public float turnSpeed   = 90f;
    [Tooltip("Extra height added to the terrain surface when sticking the unit to the ground.")]
    public float groundOffset = 0f;

    [Header("Path Visual")]
    public Color pathColor     = Color.yellow;
    public float pathLineWidth = 0.5f;
    [Tooltip("Extra height above terrain at which the path line is drawn.")]
    public float pathLineLift  = 0.1f;

    [HideInInspector] public Vector3 moveDirection = Vector3.forward;

    Vector3 squadDirection = Vector3.forward;

    List<Vector2Int> path = new List<Vector2Int>();
    int    waypointIndex  = 0;
    bool   moving         = false;
    bool   initialized    = false;

    Vector2Int currentCell;
    Vector3    currentTarget;
    LineRenderer pathLine;

    Vector2Int currentGoal;
    bool       hasGoal;

    UnitPathPlan _activePlan;
    float        _planStartTime;

    /// <summary>
    /// Configure the mover and start moving toward <paramref name="goalCell"/>.
    /// Spawner-friendly entry point.
    /// </summary>
    public void Initialize(TerrainDataStore tds, Vector2Int goalCell, UnitTypeSO type)
    {
        terrainDataStore = tds;
        unitType         = type;
        initialized      = true;

        SetupLineRenderer();
        SnapToTerrain();
        GoTo(goalCell);
    }

    /// <summary>
    /// Computes a fresh path from the unit's current cell to <paramref name="goalCell"/>
    /// and starts walking. Safe to call repeatedly.
    /// </summary>
    public void GoTo(Vector2Int goalCell)
    {
        if (terrainDataStore == null || terrainDataStore.grid == null)
        {
            Debug.LogWarning("UnitMover.GoTo: TerrainDataStore is missing or grid not loaded.");
            return;
        }

        currentGoal = goalCell;
        hasGoal     = true;

        Vector2Int startCell = terrainDataStore.WorldToGrid(transform.position);
        List<Vector2Int> newPath = AStarPathfinder.FindPath(
            terrainDataStore.grid,
            terrainDataStore.GridWidth,
            terrainDataStore.GridHeight,
            startCell,
            goalCell,
            out float totalCost,
            unitSize: unitSize,
            unitType: unitType
        );

        bool failed = newPath.Count <= 1;
        if (failed)
            Debug.LogWarning($"UnitMover: no path found from {startCell} to {goalCell} (or already at goal).");

        RegisterPlan(startCell, goalCell, newPath, null, totalCost, failed);

        StartFollowingPath(newPath);
    }

    /// <summary>
    /// Configure the mover and walk a route: from the unit's current cell, through
    /// each avenue waypoint in order, ending at <paramref name="finalGoal"/>. A* runs
    /// once per leg and the segments are concatenated into one path that's drawn upfront.
    /// Falls back to a direct path to <paramref name="finalGoal"/> if any leg fails.
    /// </summary>
    public void InitializeWithRoute(TerrainDataStore tds, IReadOnlyList<Vector2Int> avenueWaypoints, Vector2Int finalGoal, UnitTypeSO type)
    {
        terrainDataStore = tds;
        unitType         = type;
        initialized      = true;

        SetupLineRenderer();
        SnapToTerrain();
        GoToRoute(avenueWaypoints, finalGoal);
    }

    /// <summary>
    /// Build a concatenated A* path through the avenue waypoints to <paramref name="finalGoal"/>
    /// and start walking it. If any leg has no path, falls back to a direct route.
    /// </summary>
    public void GoToRoute(IReadOnlyList<Vector2Int> avenueWaypoints, Vector2Int finalGoal)
    {
        if (terrainDataStore == null || terrainDataStore.grid == null)
        {
            Debug.LogWarning("UnitMover.GoToRoute: TerrainDataStore is missing or grid not loaded.");
            return;
        }

        currentGoal = finalGoal;
        hasGoal     = true;

        Vector2Int startCell = terrainDataStore.WorldToGrid(transform.position);
        List<Vector2Int> combined = BuildRoutePath(startCell, avenueWaypoints, finalGoal, out float totalCost);

        if (combined.Count <= 1)
        {
            Debug.LogWarning($"UnitMover.GoToRoute: route through {avenueWaypoints?.Count ?? 0} avenue waypoint(s) failed; falling back to direct path to {finalGoal}.");
            // Register the route attempt as failed so the session reflects what was tried.
            RegisterPlan(startCell, finalGoal, combined, avenueWaypoints, 0f, failed: true);
            GoTo(finalGoal);
            return;
        }

        RegisterPlan(startCell, finalGoal, combined, avenueWaypoints, totalCost, failed: false);

        StartFollowingPath(combined);
    }

    List<Vector2Int> BuildRoutePath(Vector2Int startCell, IReadOnlyList<Vector2Int> avenueWaypoints, Vector2Int finalGoal, out float totalCost)
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
                unitType: unitType
            );

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

    void StartFollowingPath(List<Vector2Int> newPath)
    {
        path = newPath;

        if (path.Count > 1)
        {
            currentCell   = path[0];
            waypointIndex = 1;
            currentTarget = terrainDataStore.GridToWorld(path[waypointIndex]);
            moving        = true;

            if (_activePlan != null)
            {
                _planStartTime = Time.time;
                _activePlan.actualPath.Clear();
                _activePlan.actualPath.Add(path[0]);
            }
        }
        else
        {
            moving = false;
        }

        DrawPath();
    }

    /// <summary>
    /// Re-runs A* toward the last goal supplied to <see cref="GoTo"/> or <see cref="Initialize"/>.
    /// Call this after the world changes (obstacle added, biome cost changed, etc.).
    /// </summary>
    public void RecomputePath()
    {
        if (!hasGoal) return;
        GoTo(currentGoal);
    }

    /// <summary>Compat shim for older callers — same as <see cref="RecomputePath"/>.</summary>
    public void RequestPath() => RecomputePath();

    void Start()
    {
        if (initialized) return;
        if (pathLine == null) SetupLineRenderer();
    }

    void SetupLineRenderer()
    {
        if (pathLine != null) return;

        pathLine = gameObject.AddComponent<LineRenderer>();
        pathLine.useWorldSpace  = true;
        pathLine.startWidth     = pathLineWidth;
        pathLine.endWidth       = pathLineWidth;
        pathLine.positionCount  = 0;

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
        mat.color           = pathColor;
        pathLine.material   = mat;
        pathLine.startColor = pathColor;
        pathLine.endColor   = pathColor;
    }

    void DrawPath()
    {
        if (pathLine == null) return;
        pathLine.positionCount = path.Count;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 wp = terrainDataStore.GridToWorld(path[i]);
            wp.y = terrainDataStore.GetRoundedHeight(path[i].x, path[i].y) + pathLineLift;
            pathLine.SetPosition(i, wp);
        }
    }

    void Update()
    {
        if (!moving) return;

        // Rotation runs once per frame using the current bearing — visual only,
        // doesn't gate movement.
        Vector3 toTarget = currentTarget - transform.position;
        toTarget.y = 0f;
        Vector3 desiredDir = toTarget.sqrMagnitude > 0f ? toTarget.normalized : moveDirection;
        moveDirection = desiredDir;

        squadDirection = Vector3.RotateTowards(
            squadDirection,
            desiredDir,
            turnSpeed * Mathf.Deg2Rad * Time.deltaTime,
            0f
        );
        transform.rotation = Quaternion.LookRotation(squadDirection);

        // Consume the frame's time budget across as many waypoints as the speed allows.
        // Carrying the remainder between waypoints prevents per-step rounding from
        // accumulating into a measurable drift between actualSeconds and estimatedSeconds.
        float remainingTime = Time.deltaTime;
        while (remainingTime > 0f && moving)
        {
            Vector3 delta = currentTarget - transform.position;
            delta.y = 0f;
            float dist = delta.magnitude;

            float effectiveSpeed = ResolveStepSpeed();
            if (effectiveSpeed <= 0f) break;  // blocked / zero cost — can't make progress this frame

            float canMove = effectiveSpeed * remainingTime;

            if (canMove < dist)
            {
                // Partial step inside the current cell — consume the whole frame.
                Vector3 dir = dist > 0f ? delta / dist : Vector3.zero;
                transform.position += dir * canMove;
                SnapToTerrain();
                remainingTime = 0f;
            }
            else
            {
                // Reached this waypoint with time to spare. Snap exactly to it, charge only
                // the time it actually took, then loop to spend the rest on the next cell.
                transform.position = currentTarget;
                SnapToTerrain();
                currentCell = path[waypointIndex];

                if (_activePlan != null) _activePlan.actualPath.Add(path[waypointIndex]);

                remainingTime -= dist / effectiveSpeed;
                waypointIndex++;

                if (waypointIndex >= path.Count)
                {
                    moving = false;
                    pathLine.positionCount = 0;

                    if (_activePlan != null)
                    {
                        _activePlan.actualSeconds = Time.time - _planStartTime;
                        _activePlan.completed     = true;
                        _activePlan               = null;
                    }
                    return;
                }

                currentTarget = terrainDataStore.GridToWorld(path[waypointIndex]);
            }
        }
    }

    /// <summary>
    /// Speed for the current step = moveSpeed / (biomeCost * slopeMultiplier).
    /// Biome and slope come from the cell we're stepping FROM, in the direction of the next waypoint.
    /// </summary>
    float ResolveStepSpeed()
    {
        if (terrainDataStore == null || terrainDataStore.grid == null) return moveSpeed;

        CellData fromCell = terrainDataStore.grid[currentCell.x, currentCell.y];
        Vector2Int delta  = path[waypointIndex] - currentCell;

        int biomeCost = fromCell.biome != null ? fromCell.biome.GetMovementCost(unitType) : 3;
        float slopeMul = AStarPathfinder.ResolveSlopeMultiplier(fromCell, delta, unitType, out bool blocked);

        if (blocked || biomeCost <= 0) return 0f;
        return moveSpeed / (biomeCost * slopeMul);
    }

    void SnapToTerrain()
    {
        if (terrainDataStore == null || terrainDataStore.grid == null) return;
        Vector2Int g = terrainDataStore.WorldToGrid(transform.position);
        float y = terrainDataStore.GetRoundedHeight(g.x, g.y) + groundOffset;
        Vector3 p = transform.position;
        p.y = y;
        transform.position = p;
    }

    void RegisterPlan(Vector2Int startCell, Vector2Int goalCell, List<Vector2Int> walkPath,
                      IReadOnlyList<Vector2Int> requestedWaypoints, float totalCost, bool failed)
    {
        float seconds = failed
            ? float.PositiveInfinity
            : AStarPathfinder.CostToSeconds(totalCost, terrainDataStore.step, moveSpeed);

        UnitPathPlan plan = new UnitPathPlan
        {
            unitId             = unitId,
            startCell          = startCell,
            goalCell           = goalCell,
            path               = walkPath != null ? new List<Vector2Int>(walkPath) : new List<Vector2Int>(),
            estimatedSeconds   = seconds,
            failed             = failed,
            recordedAt         = Time.time,
        };

        if (requestedWaypoints != null)
            plan.requestedWaypoints = new List<Vector2Int>(requestedWaypoints);

        // Track this plan so Update can fill in actualPath / actualSeconds as the unit walks.
        // Each new plan resets tracking — actuals are scoped to the current walk.
        _activePlan = failed ? null : plan;

        // Lazy-create the session so it works both for menu-driven play (where the session
        // is pre-populated with a save file name) and direct in-scene play (no menu involved).
        MissionSession.GetOrCreate().RegisterPlan(plan);
    }
}
