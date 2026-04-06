using System;
using System.IO;
using UnityEngine;

public enum ZoneType  { None, Forest, Marsh, Urban }
public enum RoadType  { None, Paved, Dirt }
public enum PointType { None, Spawn, End }

[Serializable]
public class MapCell
{
    public float     density;
    public ZoneType  zone;
    public RoadType  road;
    public PointType point;
    public bool      walkable;
}

[Serializable]
public class WaypointData
{
    public string   id;
    public Vector3  position;
}

[Serializable]
public class LevelData
{
    public int            width;
    public int            height;
    public int            depth;
    public MapCell[]      cells;
    public WaypointData[] waypoints;
}

public class LevelEditorManager : MonoBehaviour
{
    [Header("Grid Dimensions")]
    public int width  = 128;
    public int height = 64;
    public int depth  = 128;

    MapCell[]      _grid;
    WaypointData[] _waypoints = Array.Empty<WaypointData>();

    string SavePath => Path.Combine(Application.persistentDataPath, "level.json");

    void Awake()
    {
        _grid = new MapCell[width * height * depth];
        for (int i = 0; i < _grid.Length; i++)
            _grid[i] = new MapCell { walkable = true };
    }

    int Index(int x, int y, int z) => x + y * width + z * width * height;

    bool InBounds(int x, int y, int z) =>
        x >= 0 && x < width && y >= 0 && y < height && z >= 0 && z < depth;

    public MapCell GetCell(int x, int y, int z)
    {
        if (!InBounds(x, y, z)) return null;
        return _grid[Index(x, y, z)];
    }

    public void SetCell(int x, int y, int z, MapCell cell)
    {
        if (!InBounds(x, y, z)) return;
        _grid[Index(x, y, z)] = cell;
    }

    public WaypointData[] GetWaypoints() => _waypoints;

    public void AddWaypoint(Vector3 position)
    {
        var wp = new WaypointData { id = "wp_" + (_waypoints.Length + 1), position = position };
        Array.Resize(ref _waypoints, _waypoints.Length + 1);
        _waypoints[_waypoints.Length - 1] = wp;
    }

    public void Save()
    {
        var data = new LevelData
        {
            width     = width,
            height    = height,
            depth     = depth,
            cells     = _grid,
            waypoints = _waypoints
        };
        File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
        Debug.Log("Level saved to " + SavePath);
    }

    public void Load()
    {
        if (!File.Exists(SavePath))
        {
            Debug.LogWarning("No save file found at " + SavePath);
            return;
        }

        var data = JsonUtility.FromJson<LevelData>(File.ReadAllText(SavePath));
        width     = data.width;
        height    = data.height;
        depth     = data.depth;
        _grid     = data.cells;
        _waypoints = data.waypoints ?? Array.Empty<WaypointData>();
        Debug.Log("Level loaded from " + SavePath);
    }
}
