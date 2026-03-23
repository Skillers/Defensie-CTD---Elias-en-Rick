using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Attach to the parent Agent GameObject.
/// Finds path on Start, then smoothly moves cell to cell.
/// Recalculates path whenever the flag moves to a different grid cell.
/// Parent never rotates — rotation is handled here as a facing direction
/// passed down to UnitFacer components on the children.
/// </summary>
public class UnitMover : MonoBehaviour
{
    [Header("References")]
    public VoronoiMap voronoiMap;
    public Transform  flag;               // goal flag placed in scene

    [Header("Movement")]
    public float moveSpeed   = 6f;        // units per second
    public float turnSpeed   = 90f;       // degrees per second (squad slow turn)

    [Header("Path Visual")]
    public Color pathColor     = Color.yellow;
    public float pathLineWidth = 0.5f;

    // Instant facing direction — shared with UnitFacer so units snap immediately
    [HideInInspector] public Vector3 moveDirection = Vector3.forward;

    // Slowly-rotating direction — drives the parent transform rotation (formation turn)
    Vector3 squadDirection = Vector3.forward;

    List<Vector2Int> path = new List<Vector2Int>();
    int    waypointIndex  = 0;
    bool   moving         = false;

    Vector3    currentTarget;
    Vector2Int lastFlagCell;   // tracks flag position so we detect moves
    LineRenderer pathLine;

    void Start()
    {
        SetupLineRenderer();
        lastFlagCell = WorldToGrid(flag.position);
        RequestPath();
    }

    void SetupLineRenderer()
    {
        pathLine = gameObject.AddComponent<LineRenderer>();
        pathLine.useWorldSpace  = true;
        pathLine.startWidth     = pathLineWidth;
        pathLine.endWidth       = pathLineWidth;
        pathLine.positionCount  = 0;

        // Find a shader that renders solid color (same fallback chain as MapRenderer)
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
        mat.color       = pathColor;
        pathLine.material = mat;
        pathLine.startColor = pathColor;
        pathLine.endColor   = pathColor;
    }

    public void RequestPath()
    {
        lastFlagCell = WorldToGrid(flag.position);

        Vector2Int startCell = WorldToGrid(transform.position);
        Vector2Int goalCell  = lastFlagCell;

        path = AStarPathfinder.FindPath(
            voronoiMap.grid,
            voronoiMap.width,
            voronoiMap.height,
            startCell,
            goalCell
        );

        if (path.Count > 1)
        {
            waypointIndex = 1;   // 0 is the start cell
            currentTarget = GridToWorld(path[waypointIndex]);
            moving = true;
        }
        else
        {
            moving = false;
            Debug.LogWarning("UnitMover: no path found or already at goal.");
        }

        DrawPath();
    }

    void DrawPath()
    {
        pathLine.positionCount = path.Count;
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 wp = GridToWorld(path[i]);
            wp.y = 0.1f;   // slightly above terrain so it's visible
            pathLine.SetPosition(i, wp);
        }
    }

    void Update()
    {
        // Repath when the flag moves to a different cell
        Vector2Int flagCell = WorldToGrid(flag.position);
        if (flagCell != lastFlagCell)
            RequestPath();

        if (!moving) return;

        // ── Move toward current waypoint ──────────────────────────────────
        Vector3 toTarget = currentTarget - transform.position;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;

        if (dist > 0.05f)
        {
            Vector3 desiredDir = toTarget.normalized;

            // Units instantly face the movement direction
            moveDirection = desiredDir;

            // Squad parent slowly rotates to keep formation
            squadDirection = Vector3.RotateTowards(
                squadDirection,
                desiredDir,
                turnSpeed * Mathf.Deg2Rad * Time.deltaTime,
                0f
            );
            transform.rotation = Quaternion.LookRotation(squadDirection);

            // Scale speed by terrain cost of the current cell:
            // cost 1 = full speed, cost 10 = 1/10 speed
            Vector2Int currentCell = voronoiMap.WorldToGrid(transform.position);
            int terrainCost = voronoiMap.grid[currentCell.x, currentCell.y].movementCost;
            float effectiveSpeed = moveSpeed / terrainCost;

            transform.position += desiredDir * effectiveSpeed * Time.deltaTime;
        }
        else
        {
            transform.position = currentTarget;
            waypointIndex++;

            if (waypointIndex >= path.Count)
            {
                moving = false;
                pathLine.positionCount = 0;   // clear line on arrival
                return;
            }

            currentTarget = GridToWorld(path[waypointIndex]);
        }
    }

    // ── Grid / World conversion (XZ plane) ───────────────────────────────
    // 1 cell = 1 unit, map centred on voronoiMap.transform.position

    Vector2Int WorldToGrid(Vector3 world)
    {
        Vector3 local = world - voronoiMap.transform.position;
        int gx = Mathf.FloorToInt(local.x + voronoiMap.width  * 0.5f);
        int gz = Mathf.FloorToInt(local.z + voronoiMap.height * 0.5f);
        gx = Mathf.Clamp(gx, 0, voronoiMap.width  - 1);
        gz = Mathf.Clamp(gz, 0, voronoiMap.height - 1);
        return new Vector2Int(gx, gz);
    }

    Vector3 GridToWorld(Vector2Int cell)
    {
        float wx = cell.x - voronoiMap.width  * 0.5f + 0.5f;
        float wz = cell.y - voronoiMap.height * 0.5f + 0.5f;
        return voronoiMap.transform.position + new Vector3(wx, 0f, wz);
    }
}
