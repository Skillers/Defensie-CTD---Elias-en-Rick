using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>Walks a unit along a precomputed A* path. Never runs route A* itself; drive it with <see cref="FollowPath"/>.</summary>
public class UnitMover : MonoBehaviour
{
    [Header("References")]
    public TerrainDataStore terrainDataStore;

    [Header("Unit")]
    [Tooltip("Per-biome movement costs and slope rules.")]
    public UnitTypeSO unitType;
    [Tooltip("Square footprint in cells, used by A*'s fit check.")]
    public int unitSize = 5;
    [HideInInspector] public int unitId;

    [Header("Movement")]
    public float moveSpeed   = 6f;
    public float turnSpeed   = 90f;
    [Tooltip("Extra height above the terrain surface.")]
    public float groundOffset = 0f;

    [Header("Ghost")]
    [Tooltip("Height above terrain of the ghost's foot and path line.")]
    public float pathLineLift  = 0.1f;

    [Header("Catch-up Path (unit → ghost)")]
    [Tooltip("Recompute the unit → ghost A* path. Disabling also stops the obstacle reroute; use catchUpPathVisible to only hide the line.")]
    public bool  catchUpPathEnabled  = true;
    [Tooltip("Show the catch-up line. Visual only.")]
    public bool  catchUpPathVisible  = true;
    public Color catchUpPathColor    = new Color(0.85f, 0.15f, 1f, 1f);
    public float catchUpPathWidth    = 0.4f;
    [Tooltip("Draw height above terrain. Keep above the main path line to avoid z-fighting.")]
    public float catchUpPathLift     = 0.2f;
    [Tooltip("Seconds between async recomputes.")]
    public float catchUpPathInterval = 0.2f;

    [HideInInspector] public Vector3 moveDirection = Vector3.forward;

    /// <summary>Goal cell of the followed path, read by the scene path maker when re-routing.</summary>
    public Vector2Int GoalCell { get; private set; }

    /// <summary>True once this unit has been given a route.</summary>
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

    /// <summary>Starts walking the precomputed path. The plan is filled in as the unit walks; pass null to skip tracking.</summary>
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

        // Only track a plan with a walkable path; a failed plan stays flagged failed.
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

        // Spend the frame's time budget across waypoints; carrying the remainder keeps
        // actualSeconds from drifting away from estimatedSeconds.
        float remainingTime = Time.deltaTime;
        while (remainingTime > 0f && moving)
        {
            Vector3 delta = currentTarget - transform.position;
            delta.y = 0f;
            float dist = delta.magnitude;

            float effectiveSpeed = ResolveStepSpeed();
            if (effectiveSpeed <= 0f)
            {
                // Step blocked: request an immediate reroute instead of waiting out the interval.
                ForceCatchUpRefire();
                break;
            }

            float canMove = effectiveSpeed * remainingTime;

            if (canMove < dist)
            {
                Vector3 dir = dist > 0f ? delta / dist : Vector3.zero;
                transform.position += dir * canMove;
                SnapToTerrain();
                remainingTime = 0f;
            }
            else
            {
                // Waypoint reached with time to spare: snap to it and spend the rest on the next cell.
                transform.position = currentTarget;
                SnapToTerrain();
                currentCell = path[waypointIndex];

                if (_activePlan != null) _activePlan.actualPath.Add(path[waypointIndex]);

                remainingTime -= dist / effectiveSpeed;
                waypointIndex++;

                if (waypointIndex >= path.Count)
                {
                    // End of the followed segment. Only finalise if this is the actual goal.
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
                    // Not at goal: stay paused until the next catch-up path arrives.
                    return;
                }

                currentTarget = terrainDataStore.GridToWorld(path[waypointIndex]);
            }
        }
    }

    /// <summary>Speed for the current step, mirroring A*'s per-step cost formula. 0 if the step is blocked.</summary>
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

    /// <summary>Sets the ghost configuration. Call before <see cref="FollowPath"/>.</summary>
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

        // Leash floor: keep the ghost at least minDistance ahead. Iterates because one
        // Advance can cross a corner and change the bearing back to the unit.
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

        // Free-run at nominal moveSpeed (terrain-independent), with a slow/stop falloff
        // once the ghost gets too far ahead.
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

    /// <summary>Resets the throttle so the next tick dispatches a fresh catch-up A* immediately.</summary>
    void ForceCatchUpRefire()
    {
        catchUpLastFireTime = -float.MaxValue;
    }

    // --- Catch-up path (unit → ghost) ---------------------------------------

    void TickCatchUpPath()
    {
        if (catchUpLine != null && catchUpLine.gameObject.activeSelf != catchUpPathVisible)
            catchUpLine.gameObject.SetActive(catchUpPathVisible);

        if (!catchUpPathEnabled) return;
        if (terrainDataStore == null || terrainDataStore.grid == null) return;

        if (catchUpTask != null && catchUpTask.IsCompleted)
        {
            if (catchUpTask.Status == TaskStatus.RanToCompletion)
                ApplyCatchUpPath(catchUpTask.Result);
            else if (catchUpTask.IsFaulted)
                Debug.LogWarning($"Catch-up A* failed: {catchUpTask.Exception?.GetBaseException().Message}");
            catchUpTask = null;
        }

        // Fires even while paused: the unit needs the next catch-up segment to resume.
        if (ghost == null || !ghost.HasPath) return;

        if (catchUpTask == null && Time.time - catchUpLastFireTime >= catchUpPathInterval)
        {
            catchUpLastFireTime = Time.time;

            // Snapshot on the main thread; the task runs off it.
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

        // The catch-up path is what the unit walks; the ghost stays on the main path.
        SwitchFollowedPath(cells);
    }

    /// <summary>Mid-walk path swap, resuming from the cell closest to the unit so it never backtracks.</summary>
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
