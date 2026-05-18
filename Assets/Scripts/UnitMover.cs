using System.Collections.Generic;
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

    [Header("Path Visual")]
    public Color pathColor     = Color.yellow;
    public float pathLineWidth = 0.5f;
    [Tooltip("Extra height above terrain at which the path line is drawn.")]
    public float pathLineLift  = 0.1f;

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
    LineRenderer pathLine;

    UnitPathPlan _activePlan;
    float        _planStartTime;

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

        SetupLineRenderer();
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

        DrawPath();
    }

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
}
