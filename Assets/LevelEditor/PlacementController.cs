using System.Collections.Generic;
using UnityEngine;

public class PlacementController : MonoBehaviour
{
    [Header("References")]
    public LevelEditorManager levelManager;
    public Camera             editorCamera;
    public BrushController    brushController;

    [Header("Placement Settings")]
    public RoadType ActiveRoadType = RoadType.Paved;
    public ZoneType ActiveZoneType = ZoneType.Forest;
    public float    ZonePaintRadius = 3f;

    const float GridStep = 0.5f;

    // Tracks last placed spawn/end so we can clear them when placing a new one
    Vector3Int? _spawnCell;
    Vector3Int? _endCell;

    // Spline points accumulated during road painting
    readonly List<Vector3Int> _roadSpline = new();

    void Update()
    {
        if (!Input.GetMouseButtonDown(0)) return;

        var ray = editorCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        Vector3Int grid = WorldToGrid(hit.point);
        HandleActiveTool(grid);
    }

    void HandleActiveTool(Vector3Int g)
    {
        switch (brushController.ActiveBrush)
        {
            // Brush tools are handled by BrushController — nothing to do here
            case BrushType.Raise:
            case BrushType.Lower:
            case BrushType.Smooth:
                break;
        }

        // Placement tools are driven by EditorUI setting ActivePlacementTool
        switch (ActivePlacementTool)
        {
            case PlacementTool.Spawn:    PlacePoint(g, PointType.Spawn);  break;
            case PlacementTool.End:      PlacePoint(g, PointType.End);    break;
            case PlacementTool.Road:     PaintRoad(g);                    break;
            case PlacementTool.Zone:     PaintZone(g);                    break;
            case PlacementTool.Waypoint: AddWaypoint(g);                  break;
            case PlacementTool.Walkable: ToggleWalkable(g);               break;
        }
    }

    public PlacementTool ActivePlacementTool { get; set; } = PlacementTool.None;

    void PlacePoint(Vector3Int g, PointType type)
    {
        // Clear previous cell of this type
        ref var tracked = ref type == PointType.Spawn ? ref _spawnCell : ref _endCell;
        if (tracked.HasValue)
        {
            var old = levelManager.GetCell(tracked.Value.x, tracked.Value.y, tracked.Value.z);
            if (old != null) old.point = PointType.None;
        }

        var cell = levelManager.GetCell(g.x, g.y, g.z);
        if (cell == null) return;
        cell.point = type;
        tracked = g;
    }

    void PaintRoad(Vector3Int g)
    {
        var cell = levelManager.GetCell(g.x, g.y, g.z);
        if (cell == null) return;
        cell.road = ActiveRoadType;
        _roadSpline.Add(g);

        // Paint road along spline between last two points
        if (_roadSpline.Count >= 2)
            PaintRoadSegment(_roadSpline[_roadSpline.Count - 2], _roadSpline[_roadSpline.Count - 1]);
    }

    void PaintRoadSegment(Vector3Int a, Vector3Int b)
    {
        // Bresenham-style interpolation along segment
        int steps = Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y), Mathf.Abs(b.z - a.z));
        for (int i = 0; i <= steps; i++)
        {
            float t = steps == 0 ? 0f : (float)i / steps;
            int x = Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t));
            int y = Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t));
            int z = Mathf.RoundToInt(Mathf.Lerp(a.z, b.z, t));
            var cell = levelManager.GetCell(x, y, z);
            if (cell != null) cell.road = ActiveRoadType;
        }
    }

    void PaintZone(Vector3Int g)
    {
        int radiusCells = Mathf.CeilToInt(ZonePaintRadius / GridStep);
        for (int dx = -radiusCells; dx <= radiusCells; dx++)
        for (int dz = -radiusCells; dz <= radiusCells; dz++)
        {
            if (new Vector2(dx, dz).magnitude * GridStep > ZonePaintRadius) continue;
            var cell = levelManager.GetCell(g.x + dx, g.y, g.z + dz);
            if (cell != null) cell.zone = ActiveZoneType;
        }
    }

    void AddWaypoint(Vector3Int g)
    {
        Vector3 worldPos = new Vector3(g.x, g.y, g.z) * GridStep;
        levelManager.AddWaypoint(worldPos);
    }

    void ToggleWalkable(Vector3Int g)
    {
        var cell = levelManager.GetCell(g.x, g.y, g.z);
        if (cell != null) cell.walkable = !cell.walkable;
    }

    Vector3Int WorldToGrid(Vector3 worldPos) => new Vector3Int(
        Mathf.RoundToInt(worldPos.x / GridStep),
        Mathf.RoundToInt(worldPos.y / GridStep),
        Mathf.RoundToInt(worldPos.z / GridStep)
    );

    public void ClearRoadSpline() => _roadSpline.Clear();
}

public enum PlacementTool { None, Spawn, End, Road, Zone, Waypoint, Walkable }
