using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Single, persistent-for-one-scene-transition session carrier. Created by the first
/// <see cref="UnitMover"/> that registers a plan — that is the only spawn path. Marked
/// DontDestroyOnLoad so it survives the gameplay → results transition, then re-parented
/// into the next loaded scene on the first sceneLoaded event so it dies naturally with
/// the scene change after that. Carries the save file name (set by the creating unit
/// from its <see cref="TerrainDataStore"/>) into the results scene and collects per-unit
/// <see cref="UnitPathPlan"/> records as units pathfind.
/// </summary>
public class MissionSession : MonoBehaviour
{
    public static MissionSession Instance { get; private set; }

    /// <summary>Save file the unit's TerrainDataStore loaded — read by the results scene.</summary>
    public string saveFileName;

    [SerializeField] List<UnitPathPlan> _plans = new List<UnitPathPlan>();

    /// <summary>Read-only view of every plan registered this session.</summary>
    public IReadOnlyList<UnitPathPlan> Plans => _plans;

    [SerializeField] List<ObstacleCostEntry> _obstacleSummary = new List<ObstacleCostEntry>();

    /// <summary>One entry per <see cref="ObstacleSO"/> currently placed. Updated live as obstacles are placed and deleted.</summary>
    public IReadOnlyList<ObstacleCostEntry> ObstacleSummary => _obstacleSummary;

    /// <summary>Sum of <see cref="ObstacleCostEntry.CostPerType"/> across every entry.</summary>
    public int TotalObstacleCost
    {
        get
        {
            int total = 0;
            for (int i = 0; i < _obstacleSummary.Count; i++) total += _obstacleSummary[i].CostPerType;
            return total;
        }
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
    /// Increments the count for <paramref name="type"/> in the obstacle summary, creating an
    /// entry if this is the first placement of that type. Called by ObstaclePlacementManager
    /// each time an obstacle is placed.
    /// </summary>
    public void RegisterObstaclePlacement(ObstacleSO type)
    {
        if (type == null) return;
        var entry = _obstacleSummary.Find(e => e.obstacleType == type);
        if (entry != null) entry.count++;
        else _obstacleSummary.Add(new ObstacleCostEntry { obstacleType = type, count = 1 });
    }

    /// <summary>
    /// Decrements the count for <paramref name="type"/>, removing the entry when it reaches zero.
    /// Called by ObstaclePlacementManager when a placed obstacle is deleted.
    /// </summary>
    public void UnregisterObstaclePlacement(ObstacleSO type)
    {
        if (type == null) return;
        int idx = _obstacleSummary.FindIndex(e => e.obstacleType == type);
        if (idx < 0) return;
        _obstacleSummary[idx].count--;
        if (_obstacleSummary[idx].count <= 0) _obstacleSummary.RemoveAt(idx);
    }

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

/// <summary>
/// One row of the obstacle cost summary: a placed obstacle type and how many of it are
/// currently on the map. Cost is derived from <see cref="ObstacleSO.cost"/> so the SO
/// stays the single source of truth.
/// </summary>
[System.Serializable]
public class ObstacleCostEntry
{
    public ObstacleSO obstacleType;
    public int count;

    /// <summary>Cost contribution of this type: unit cost from the SO multiplied by <see cref="count"/>.</summary>
    public int CostPerType => obstacleType != null ? obstacleType.cost * count : 0;
}
