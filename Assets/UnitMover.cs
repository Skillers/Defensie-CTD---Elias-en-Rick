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
    public TerrainDataStore terrainDataStore;
    public Transform  flag;

    [Header("Unit")]
    [Tooltip("Optional — used to resolve per-biome movement costs.")]
    public UnitTypeSO unitType;

    [Header("Movement")]
    public float moveSpeed   = 6f;
    public float turnSpeed   = 90f;

    [Header("Path Visual")]
    public Color pathColor     = Color.yellow;
    public float pathLineWidth = 0.5f;

    [HideInInspector] public Vector3 moveDirection = Vector3.forward;

    Vector3 squadDirection = Vector3.forward;

    List<Vector2Int> path = new List<Vector2Int>();
    int    waypointIndex  = 0;
    bool   moving         = false;

    Vector3    currentTarget;
    Vector2Int lastFlagCell;
    LineRenderer pathLine;

    void Start()
    {
        SetupLineRenderer();
        lastFlagCell = terrainDataStore.WorldToGrid(flag.position);
        RequestPath();
    }

    void SetupLineRenderer()
    {
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
        mat.color       = pathColor;
        pathLine.material = mat;
        pathLine.startColor = pathColor;
        pathLine.endColor   = pathColor;
    }

    public void RequestPath()
    {
        lastFlagCell = terrainDataStore.WorldToGrid(flag.position);

        Vector2Int startCell = terrainDataStore.WorldToGrid(transform.position);
        Vector2Int goalCell  = lastFlagCell;

        path = AStarPathfinder.FindPath(
            terrainDataStore.grid,
            terrainDataStore.GridWidth,
            terrainDataStore.GridHeight,
            startCell,
            goalCell,
            unitType: unitType
        );

        if (path.Count > 1)
        {
            waypointIndex = 1;
            currentTarget = terrainDataStore.GridToWorld(path[waypointIndex]);
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
            Vector3 wp = terrainDataStore.GridToWorld(path[i]);
            wp.y = 0.1f;
            pathLine.SetPosition(i, wp);
        }
    }

    void Update()
    {
        Vector2Int flagCell = terrainDataStore.WorldToGrid(flag.position);
        if (flagCell != lastFlagCell)
            RequestPath();

        if (!moving) return;

        Vector3 toTarget = currentTarget - transform.position;
        toTarget.y = 0f;
        float dist = toTarget.magnitude;

        if (dist > 0.05f)
        {
            Vector3 desiredDir = toTarget.normalized;

            moveDirection = desiredDir;

            squadDirection = Vector3.RotateTowards(
                squadDirection,
                desiredDir,
                turnSpeed * Mathf.Deg2Rad * Time.deltaTime,
                0f
            );
            transform.rotation = Quaternion.LookRotation(squadDirection);

            Vector2Int currentCell = terrainDataStore.WorldToGrid(transform.position);
            var cell = terrainDataStore.grid[currentCell.x, currentCell.y];
            int terrainCost = cell?.biome != null ? cell.biome.GetMovementCost(unitType) : 3;
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
                pathLine.positionCount = 0;
                return;
            }

            currentTarget = terrainDataStore.GridToWorld(path[waypointIndex]);
        }
    }
}
