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

    [Header("Ghost")]
    [Tooltip("Spawn a floating blue orb that walks the main A* line. It slows / stops when it gets too far ahead of the unit.")]
    public bool  ghostEnabled      = true;
    public Color ghostColor        = new Color(0.25f, 0.55f, 1f, 1f);
    [Tooltip("Diameter of the floating orb.")]
    public float ghostOrbSize      = 1.2f;
    [Tooltip("How high the orb floats above the main line.")]
    public float ghostHoverHeight  = 4f;
    [Tooltip("Width of the vertical stem drawn from the orb down to the line.")]
    public float ghostStemWidth    = 0.12f;
    [Tooltip("When the orb gets more than this far (world units, planar) ahead of the unit, it starts to slow down.")]
    public float ghostSlowDistance = 8f;
    [Tooltip("When the orb gets more than this far ahead of the unit, it stops entirely until the unit catches up.")]
    public float ghostStopDistance = 20f;

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

    GameObject   ghostOrb;
    LineRenderer ghostStem;
    int          ghostSegmentIndex;
    float        ghostSegmentT;
    bool         ghostFinished;

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
        InitGhostOnPath();
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
        UpdateGhost(Time.deltaTime);
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
                    DestroyGhost();

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

    // --- Ghost ---------------------------------------------------------------

    void InitGhostOnPath()
    {
        if (!ghostEnabled || path == null || path.Count < 2) return;
        SetupGhost();
        ghostSegmentIndex = 0;
        ghostSegmentT     = 0f;
        ghostFinished     = false;
        UpdateGhostVisual();
    }

    void SetupGhost()
    {
        if (ghostOrb != null) return;

        ghostOrb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        ghostOrb.name = $"Ghost_{unitId}";
        var col = ghostOrb.GetComponent<Collider>();
        if (col != null) Destroy(col);
        ghostOrb.transform.localScale = Vector3.one * ghostOrbSize;

        var orbMr = ghostOrb.GetComponent<MeshRenderer>();
        var orbShader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var orbMat = new Material(orbShader);
        orbMat.color = ghostColor;
        if (orbMat.HasProperty("_BaseColor")) orbMat.SetColor("_BaseColor", ghostColor);
        if (orbMat.HasProperty("_EmissionColor"))
        {
            orbMat.EnableKeyword("_EMISSION");
            orbMat.SetColor("_EmissionColor", ghostColor * 1.5f);
        }
        orbMr.sharedMaterial = orbMat;

        var stemGO = new GameObject("Stem");
        stemGO.transform.SetParent(ghostOrb.transform, false);
        ghostStem = stemGO.AddComponent<LineRenderer>();
        ghostStem.useWorldSpace = true;
        ghostStem.startWidth    = ghostStemWidth;
        ghostStem.endWidth      = ghostStemWidth;
        ghostStem.positionCount = 2;

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
        stemMat.color        = ghostColor;
        ghostStem.material   = stemMat;
        ghostStem.startColor = ghostColor;
        ghostStem.endColor   = ghostColor;
    }

    void DestroyGhost()
    {
        if (ghostOrb != null) Destroy(ghostOrb);
        ghostOrb      = null;
        ghostStem     = null;
        ghostFinished = true;
    }

    void UpdateGhost(float dt)
    {
        if (!ghostEnabled || ghostOrb == null || ghostFinished) return;
        if (path == null || path.Count < 2) return;

        // Slow / stop based on how far ahead of the unit the ghost has drifted.
        Vector3 foot = GetGhostFootPosition();
        Vector3 toUnit = foot - transform.position;
        toUnit.y = 0f;
        float distAhead = toUnit.magnitude;

        float speedMul;
        if (distAhead <= ghostSlowDistance) speedMul = 1f;
        else if (distAhead >= ghostStopDistance) speedMul = 0f;
        else speedMul = 1f - (distAhead - ghostSlowDistance) / Mathf.Max(0.0001f, ghostStopDistance - ghostSlowDistance);

        float advance = moveSpeed * speedMul * dt;
        if (advance > 0f) AdvanceGhost(advance);

        UpdateGhostVisual();
    }

    void AdvanceGhost(float amount)
    {
        while (amount > 0f && !ghostFinished && ghostSegmentIndex + 1 < path.Count)
        {
            Vector3 a = GhostCellFoot(path[ghostSegmentIndex]);
            Vector3 b = GhostCellFoot(path[ghostSegmentIndex + 1]);
            float segLen = Vector3.Distance(a, b);
            if (segLen <= 0f) { ghostSegmentIndex++; ghostSegmentT = 0f; continue; }

            float remaining = (1f - ghostSegmentT) * segLen;
            if (amount < remaining)
            {
                ghostSegmentT += amount / segLen;
                amount = 0f;
            }
            else
            {
                amount -= remaining;
                ghostSegmentIndex++;
                ghostSegmentT = 0f;
                if (ghostSegmentIndex + 1 >= path.Count)
                {
                    ghostSegmentIndex = path.Count - 2;
                    ghostSegmentT     = 1f;
                    ghostFinished     = true;
                }
            }
        }
    }

    Vector3 GhostCellFoot(Vector2Int cell)
    {
        Vector3 p = terrainDataStore.GridToWorld(cell);
        p.y = terrainDataStore.GetRoundedHeight(cell.x, cell.y) + pathLineLift;
        return p;
    }

    Vector3 GetGhostFootPosition()
    {
        if (path == null || path.Count == 0) return transform.position;
        if (path.Count == 1 || ghostSegmentIndex + 1 >= path.Count) return GhostCellFoot(path[path.Count - 1]);
        Vector3 a = GhostCellFoot(path[ghostSegmentIndex]);
        Vector3 b = GhostCellFoot(path[ghostSegmentIndex + 1]);
        return Vector3.Lerp(a, b, ghostSegmentT);
    }

    void UpdateGhostVisual()
    {
        if (ghostOrb == null) return;
        Vector3 foot = GetGhostFootPosition();
        Vector3 orbPos = foot + Vector3.up * ghostHoverHeight;
        ghostOrb.transform.position = orbPos;
        if (ghostStem != null)
        {
            ghostStem.SetPosition(0, orbPos);
            ghostStem.SetPosition(1, foot);
        }
    }

    void OnDestroy()
    {
        DestroyGhost();
    }
}
