using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Single, persistent-for-one-scene-transition session carrier. Created in the menu
/// scene before <see cref="SceneManager.LoadScene(string)"/>, marked DontDestroyOnLoad
/// so it survives into the destination scene, then re-parented into that scene on the
/// first sceneLoaded event so it dies naturally with the next scene change.
/// Carries the save file name into the destination scene's <see cref="TerrainDataStore"/>
/// and collects per-unit <see cref="UnitPathPlan"/> records as units pathfind.
/// </summary>
public class MissionSession : MonoBehaviour
{
    public static MissionSession Instance { get; private set; }

    /// <summary>Save file the destination scene's TerrainDataStore should load.</summary>
    public string saveFileName;

    [SerializeField] List<UnitPathPlan> _plans = new List<UnitPathPlan>();

    /// <summary>Read-only view of every plan registered this session.</summary>
    public IReadOnlyList<UnitPathPlan> Plans => _plans;

    /// <summary>
    /// Returns the existing session or lazily creates one. Use from menu-scene
    /// transition code to populate <see cref="saveFileName"/> just before LoadScene.
    /// </summary>
    public static MissionSession GetOrCreate()
    {
        if (Instance != null) return Instance;
        var go = new GameObject("MissionSession");
        return go.AddComponent<MissionSession>();
    }

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;

        // Direct-in-scene play (no menu bootstrap): fall back to whatever the scene's
        // TerrainDataStore is configured to load, so the save name isn't empty.
        if (string.IsNullOrEmpty(saveFileName))
        {
            TerrainDataStore tds = FindFirstObjectByType<TerrainDataStore>();
            if (tds != null) saveFileName = tds.SaveFileName;
        }
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // First load after creation: re-parent into that scene so the persistent flag
        // is effectively dropped — the next scene change will destroy us normally.
        SceneManager.MoveGameObjectToScene(gameObject, scene);
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// Adds a plan or overwrites the existing one for the same <see cref="UnitPathPlan.unitId"/>.
    /// Latest-only semantics: recomputed paths replace earlier ones.
    /// </summary>
    public void RegisterPlan(UnitPathPlan plan)
    {
        if (plan == null) return;
        int idx = _plans.FindIndex(p => p.unitId == plan.unitId);
        if (idx >= 0) _plans[idx] = plan;
        else _plans.Add(plan);
    }

    /// <summary>Returns the plan registered for <paramref name="unitId"/>, or null if none.</summary>
    public UnitPathPlan GetPlan(int unitId) => _plans.Find(p => p.unitId == unitId);

    /// <summary>
    /// True when at least one plan exists AND every registered plan has either arrived
    /// (<see cref="UnitPathPlan.completed"/>) or been marked unreachable
    /// (<see cref="UnitPathPlan.failed"/>). Failed plans count as "finished" so the
    /// mission doesn't hang forever on an unreachable unit.
    /// </summary>
    public bool AllPlansFinished
    {
        get
        {
            if (_plans.Count == 0) return false;
            for (int i = 0; i < _plans.Count; i++)
                if (!_plans[i].completed && !_plans[i].failed) return false;
            return true;
        }
    }
}

/// <summary>
/// Snapshot of a single unit's A* result: the cells it intends to walk, the upfront
/// time estimate, and (for route walks) the avenue waypoints that were requested.
/// </summary>
[System.Serializable]
public class UnitPathPlan
{
    public int unitId;
    public Vector2Int startCell;
    public Vector2Int goalCell;
    public List<Vector2Int> path = new List<Vector2Int>();
    public List<Vector2Int> requestedWaypoints = new List<Vector2Int>();
    public float estimatedSeconds;
    public bool failed;
    public float recordedAt;

    // Filled in as the unit actually walks the path. completed=true once it arrives.
    public List<Vector2Int> actualPath = new List<Vector2Int>();
    public float actualSeconds;
    public bool completed;
}
