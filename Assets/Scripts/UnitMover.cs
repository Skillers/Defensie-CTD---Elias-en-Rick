using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Walks a unit along a precomputed A* path. Path <em>making</em> lives in the
/// scene (<see cref="AStarPathGeneration"/>) — this component never runs A* itself.
/// Drive it with <see cref="FollowPath"/>: the caller supplies the cells to walk,
/// the goal, and the <see cref="UnitPathPlan"/> to fill in as the unit moves.
/// Speed is divided by the biome cost AND the unit's slope multiplier of the cell
/// being stepped from, so visual movement matches A*'s path cost.
/// Y is snapped to the terrain's rounded height every frame so the unit follows hills.
/// </summary>
public class UnitMover : MonoBehaviour
{
    [Header("References")]
    public TerrainDataStore terrainDataStore;

    [Header("Unit")]
    [Tooltip("Resolves per-biome movement costs and slope rules. Set by the scene path maker.")]
    public UnitTypeSO unitType;
    [Tooltip("Square footprint in cells. Read by AStarPathGeneration for A*'s CanFit check.")]
    public int unitSize = 5;
    [Tooltip("Set by UnitSpawner. Used as the key when registering this unit's plan in MissionSession.")]
    [HideInInspector] public int unitId;

    [Header("Movement")]
    public float moveSpeed   = 6f;
    public float turnSpeed   = 90f;
    [Tooltip("Extra height added to the terrain surface when sticking the unit to the ground.")]
    public float groundOffset = 0f;

    [Header("Ghost")]
    [Tooltip("Height above terrain at which the ghost's foot (stem base and the ghost's own path line) sits.")]
    public float pathLineLift  = 0.1f;

    [Header("Catch-up Path (unit → ghost)")]
    [Tooltip("Live best A* path from the unit to the ghost. Recomputed asynchronously every refresh interval. Obeys the same biome / slope rules as the main A* line.")]
    public bool  catchUpPathEnabled  = true;
    public Color catchUpPathColor    = new Color(0.85f, 0.15f, 1f, 1f);
    public float catchUpPathWidth    = 0.4f;
    [Tooltip("Extra height above terrain at which the catch-up line is drawn. Keep slightly above the main path line so they don't z-fight.")]
    public float catchUpPathLift     = 0.2f;
    [Tooltip("Seconds between async re-computations.")]
    public float catchUpPathInterval = 0.2f;

    [HideInInspector] public Vector3 moveDirection = Vector3.forward;

    /// <summary>Goal cell of the path this unit is following. Read by the scene path maker when re-routing.</summary>
    public Vector2Int GoalCell { get; private set; }

    /// <summary>True once <see cref="FollowPath"/> has been called — i.e. this unit has been given a route.</summary>
    public bool HasPath { get; private set; }

    Vector3 squadDirection = Vector3.forward;

    List<Vector2Int> path = new List<Vector2Int>();
    int    waypointIndex  = 0;
    bool   moving         = false;
    bool   initialized    = false;

    Vector2Int currentCell;
    Vector3    currentTarget;

    UnitPathPlan _activePlan;
    float        _planStartTime;

    UnitGhost               ghost;
    GhostSettings           ghostSettings = new GhostSettings();

    LineRenderer            catchUpLine;
    Task<List<Vector2Int>>  catchUpTask;
    float                   catchUpLastFireTime;
    TerrainDataStore        _subscribedStore;

    /// <summary>
    /// Configure the mover and start walking <paramref name="precomputedPath"/> (built
    /// by <see cref="AStarPathGeneration"/>). <paramref name="plan"/> is filled in with
    /// the actual path/seconds as the unit walks; pass null to skip tracking.
    /// </summary>
    public void FollowPath(TerrainDataStore tds, UnitTypeSO type, Vector2Int goalCell,
                           List<Vector2Int> precomputedPath, UnitPathPlan plan)
    {
        terrainDataStore = tds;
        unitType         = type;
        GoalCell         = goalCell;
        HasPath          = true;
        initialized      = true;

        SubscribeToObstacleEvents(tds);

        SnapToTerrain();

        // Only track a plan that has a walkable path — a failed/empty path leaves
        // the registered plan flagged failed and Update never finalizes it.
        _activePlan = (plan != null && precomputedPath != null && precomputedPath.Count > 1) ? plan : null;

        StartFollowingPath(precomputedPath ?? new List<Vector2Int>());
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

        InitGhostOnPath();
    }

    void Update()
    {
        UpdateGhost(Time.deltaTime);
        TickCatchUpPath();
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
            if (effectiveSpeed <= 0f)
            {
                // The current step is blocked by slope, biome or obstacle. Force the
                // next TickCatchUpPath to dispatch a fresh A* instead of waiting out
                // the interval, so the unit re-routes around whatever blocked it.
                ForceCatchUpRefire();
                break;
            }

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
                    // Reached end of the current followed segment. The followed path is
                    // now a per-tick catch-up to the ghost, so end-of-segment is normal:
                    // only finalise the mission if this segment's end is the actual goal.
                    moving = false;
                    if (currentCell == GoalCell)
                    {
                        DestroyGhost();
                        DestroyCatchUpLine();

                        if (_activePlan != null)
                        {
                            _activePlan.actualSeconds = Time.time - _planStartTime;
                            _activePlan.completed     = true;
                            _activePlan               = null;
                        }
                    }
                    // If not at goal, stay paused — TickCatchUpPath keeps firing and the
                    // next arriving path swaps us back into motion via SwitchFollowedPath.
                    return;
                }

                currentTarget = terrainDataStore.GridToWorld(path[waypointIndex]);
            }
        }
    }

    /// <summary>
    /// Speed for the current step. Mirrors <see cref="AStarPathfinder.FindPath"/>'s
    /// per-step cost formula: walk every cell the step physically crosses (the
    /// destination for cardinal/diagonal, the two intermediates + destination for
    /// knight), combine biome * obstacle per cell weighted by the cell's portion of
    /// the step, then divide moveSpeed by (weightedCellCost * slopeMul). Returns 0
    /// if slope blocks the direction or any crossed cell blocks biome/obstacle.
    /// </summary>
    float ResolveStepSpeed()
    {
        if (terrainDataStore == null || terrainDataStore.grid == null) return moveSpeed;

        CellData fromCell = terrainDataStore.grid[currentCell.x, currentCell.y];
        Vector2Int delta  = path[waypointIndex] - currentCell;

        float slopeMul = AStarPathfinder.ResolveSlopeMultiplier(fromCell, delta, unitType, out bool slopeBlocked);
        if (slopeBlocked) return 0f;

        int dirIndex = AStarPathfinder.GetDirectionIndex(delta);
        if (dirIndex < 0) return moveSpeed;  // off-grid delta (shouldn't happen on a valid path)

        CellCrossing[] crossings = CellPathing.Crossings[dirIndex];
        int w = terrainDataStore.GridWidth;
        int h = terrainDataStore.GridHeight;

        float weightedCellCost = 0f;
        for (int c = 0; c < crossings.Length; c++)
        {
            Vector2Int pos = currentCell + crossings[c].offset;
            if (pos.x < 0 || pos.x >= w || pos.y < 0 || pos.y >= h) return 0f;

            CellData crossed = terrainDataStore.grid[pos.x, pos.y];

            float biomeMul = AStarPathfinder.ResolveBiomeMultiplier(crossed, unitType, out bool biomeBlocked);
            if (biomeBlocked) return 0f;

            float obstacleMul = AStarPathfinder.ResolveObstacleMultiplier(crossed, unitType, out bool obstacleBlocked);
            if (obstacleBlocked) return 0f;

            weightedCellCost += crossings[c].portion * biomeMul * obstacleMul;
        }

        float effectiveCost = weightedCellCost * slopeMul;
        if (effectiveCost <= 0f) return 0f;
        return moveSpeed / effectiveCost;
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

    // --- Ghost ---------------------------------------------------------------

    /// <summary>
    /// Push the ghost configuration in from the spawner. Call before
    /// <see cref="FollowPath"/> so InitGhostOnPath sees the right values.
    /// Defaults are used if never called.
    /// </summary>
    public void ApplyGhostSettings(GhostSettings settings)
    {
        if (settings != null) ghostSettings = settings;
    }

    void InitGhostOnPath()
    {
        if (!ghostSettings.enabled || path == null || path.Count < 2) return;

        if (ghost == null)
        {
            if (ghostSettings.prefab != null)
            {
                ghost = Instantiate(ghostSettings.prefab);
                ghost.name = $"Ghost_{unitId}";
            }
            else
            {
                var go = new GameObject($"Ghost_{unitId}");
                ghost = go.AddComponent<UnitGhost>();
            }
        }

        ghost.Initialize(terrainDataStore, unitType, path, pathLineLift);
    }

    void DestroyGhost()
    {
        if (ghost != null) Destroy(ghost.gameObject);
        ghost = null;
    }

    void UpdateGhost(float dt)
    {
        if (!ghostSettings.enabled || ghost == null || !ghost.HasPath) return;

        // 1) Leash floor: if the unit has closed inside the minimum, snap the
        // ghost forward along its path until it sits at the minimum again.
        // Iterates because one Advance can cross a corner and change the
        // bearing back to the unit.
        for (int i = 0; i < ghostSettings.maxStepsPerFrame; i++)
        {
            if (ghost.IsFinished) break;

            Vector3 toUnit = ghost.GetFootPosition() - transform.position;
            toUnit.y = 0f;
            float dist = toUnit.magnitude;
            if (dist >= ghostSettings.minDistance) break;

            ghost.Advance(ghostSettings.minDistance - dist);
        }

        if (ghost.IsFinished) return;

        // 2) Free-run: ghost moves at the unit's nominal moveSpeed (unaffected
        // by terrain), so it pulls ahead while the unit is biome/slope-slowed.
        // Scale by a slow/stop falloff once it gets uncomfortably far ahead.
        Vector3 toUnitNow = ghost.GetFootPosition() - transform.position;
        toUnitNow.y = 0f;
        float distAhead = toUnitNow.magnitude;

        float speedMul;
        if (distAhead <= ghostSettings.slowDistance) speedMul = 1f;
        else if (distAhead >= ghostSettings.stopDistance) speedMul = 0f;
        else speedMul = 1f - (distAhead - ghostSettings.slowDistance) / Mathf.Max(0.0001f, ghostSettings.stopDistance - ghostSettings.slowDistance);

        float advance = moveSpeed * speedMul * dt;
        if (advance > 0f) ghost.Advance(advance);
    }

    void OnDestroy()
    {
        UnsubscribeFromObstacleEvents();
        DestroyGhost();
        DestroyCatchUpLine();
    }

    // --- Obstacle event hookup ----------------------------------------------

    void SubscribeToObstacleEvents(TerrainDataStore tds)
    {
        if (_subscribedStore == tds) return;
        UnsubscribeFromObstacleEvents();
        if (tds == null) return;

        tds.OnObstacleRegistered   += HandleObstacleChange;
        tds.OnObstacleUnregistered += HandleObstacleChange;
        _subscribedStore = tds;
    }

    void UnsubscribeFromObstacleEvents()
    {
        if (_subscribedStore == null) return;
        _subscribedStore.OnObstacleRegistered   -= HandleObstacleChange;
        _subscribedStore.OnObstacleUnregistered -= HandleObstacleChange;
        _subscribedStore = null;
    }

    void HandleObstacleChange(PlacedObstacle po) => ForceCatchUpRefire();

    /// <summary>
    /// Resets the catch-up throttle so the next <see cref="TickCatchUpPath"/> dispatches
    /// a fresh A* immediately, instead of waiting for the interval to elapse. Used both
    /// reactively (when a step is blocked) and proactively (when an obstacle change fires).
    /// If a task is already in-flight, the request is satisfied as soon as it completes.
    /// </summary>
    void ForceCatchUpRefire()
    {
        catchUpLastFireTime = -float.MaxValue;
    }

    // --- Catch-up path (unit → ghost) ---------------------------------------

    void TickCatchUpPath()
    {
        if (!catchUpPathEnabled) return;
        if (terrainDataStore == null || terrainDataStore.grid == null) return;

        // Apply finished work from the previous fire.
        if (catchUpTask != null && catchUpTask.IsCompleted)
        {
            if (catchUpTask.Status == TaskStatus.RanToCompletion)
                ApplyCatchUpPath(catchUpTask.Result);
            else if (catchUpTask.IsFaulted)
                Debug.LogWarning($"Catch-up A* failed: {catchUpTask.Exception?.GetBaseException().Message}");
            catchUpTask = null;
        }

        // Only fire while there's something to chase. Note: we fire even when paused
        // (moving == false) because the unit pauses at the end of each catch-up segment
        // and needs the next one to resume.
        if (ghost == null || !ghost.HasPath) return;

        if (catchUpTask == null && Time.time - catchUpLastFireTime >= catchUpPathInterval)
        {
            catchUpLastFireTime = Time.time;

            // Snapshot everything the worker needs on the main thread.
            CellData[,] grid = terrainDataStore.grid;
            int w = terrainDataStore.GridWidth;
            int h = terrainDataStore.GridHeight;
            Vector2Int start = terrainDataStore.WorldToGrid(transform.position);
            Vector2Int goal  = terrainDataStore.WorldToGrid(ghost.GetFootPosition());
            int        size  = unitSize;
            UnitTypeSO type  = unitType;

            catchUpTask = Task.Run(() =>
            {
                return AStarPathfinder.FindPath(grid, w, h, start, goal, out _, size, type);
            });
        }
    }

    void ApplyCatchUpPath(List<Vector2Int> cells)
    {
        EnsureCatchUpLine();
        if (cells == null || cells.Count < 2)
        {
            catchUpLine.positionCount = 0;
            return;
        }
        catchUpLine.positionCount = cells.Count;
        for (int i = 0; i < cells.Count; i++)
        {
            Vector3 wp = terrainDataStore.GridToWorld(cells[i]);
            wp.y = terrainDataStore.GetRoundedHeight(cells[i].x, cells[i].y) + catchUpPathLift;
            catchUpLine.SetPosition(i, wp);
        }

        // The catch-up path is what the unit actually walks now: hand it to the
        // mover's follow state. Ghost still walks the original precomputed main
        // path so the catch-up has a moving goal.
        SwitchFollowedPath(cells);
    }

    /// <summary>
    /// Mid-walk path swap. Replaces the followed path with <paramref name="newPath"/>
    /// without touching the ghost, the plan, or timing state. Resumes from the cell
    /// in newPath closest to the unit's current position so a stale catch-up snapshot
    /// doesn't make the unit backtrack.
    /// </summary>
    void SwitchFollowedPath(List<Vector2Int> newPath)
    {
        if (newPath == null || newPath.Count < 2 || terrainDataStore == null) return;

        path = newPath;

        Vector3 here = transform.position;
        int closestIdx = 0;
        float closestSqr = float.MaxValue;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 wp = terrainDataStore.GridToWorld(path[i]);
            float dx = wp.x - here.x;
            float dz = wp.z - here.z;
            float sqr = dx * dx + dz * dz;
            if (sqr < closestSqr) { closestSqr = sqr; closestIdx = i; }
        }

        currentCell   = path[closestIdx];
        waypointIndex = Mathf.Min(closestIdx + 1, path.Count - 1);
        currentTarget = terrainDataStore.GridToWorld(path[waypointIndex]);
        moving        = waypointIndex < path.Count;
    }

    void EnsureCatchUpLine()
    {
        if (catchUpLine != null) return;

        var go = new GameObject($"CatchUpLine_{unitId}");
        go.transform.SetParent(transform, false);
        catchUpLine = go.AddComponent<LineRenderer>();
        catchUpLine.useWorldSpace = true;
        catchUpLine.startWidth    = catchUpPathWidth;
        catchUpLine.endWidth      = catchUpPathWidth;
        catchUpLine.positionCount = 0;

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
        mat.color            = catchUpPathColor;
        catchUpLine.material   = mat;
        catchUpLine.startColor = catchUpPathColor;
        catchUpLine.endColor   = catchUpPathColor;
    }

    void DestroyCatchUpLine()
    {
        if (catchUpLine != null) Destroy(catchUpLine.gameObject);
        catchUpLine = null;
    }
}
