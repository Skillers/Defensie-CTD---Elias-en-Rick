using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Session carrier that survives exactly one scene transition (gameplay → results). Holds the unit path plans and the obstacle cost summary.</summary>
public class MissionSession : MonoBehaviour
{
    public static MissionSession Instance { get; private set; }

    /// <summary>Loaded save file name, read by the results scene.</summary>
    public string saveFileName;

    [SerializeField] List<UnitPathPlan> _plans = new List<UnitPathPlan>();

    public IReadOnlyList<UnitPathPlan> Plans => _plans;

    [SerializeField] List<ObstacleCostEntry> _obstacleSummary = new List<ObstacleCostEntry>();

    /// <summary>One entry per placed ObstacleSO, updated live on place and delete.</summary>
    public IReadOnlyList<ObstacleCostEntry> ObstacleSummary => _obstacleSummary;

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
        // Re-parent into the newly loaded scene so the next scene change destroys us normally.
        SceneManager.MoveGameObjectToScene(gameObject, scene);
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>Adds a plan, or overwrites the existing one for the same unitId.</summary>
    public void RegisterPlan(UnitPathPlan plan)
    {
        if (plan == null) return;
        int idx = _plans.FindIndex(p => p.unitId == plan.unitId);
        if (idx >= 0) _plans[idx] = plan;
        else _plans.Add(plan);
    }

    public UnitPathPlan GetPlan(int unitId) => _plans.Find(p => p.unitId == unitId);

    /// <summary>Adds segments to the type's summary entry. Line obstacles pass their segment count, point obstacles pass 1.</summary>
    public void RegisterObstaclePlacement(ObstacleSO type, int segments)
    {
        if (type == null || segments <= 0) return;
        var entry = _obstacleSummary.Find(e => e.obstacleType == type);
        if (entry != null) entry.count += segments;
        else _obstacleSummary.Add(new ObstacleCostEntry { obstacleType = type, count = segments });
    }

    /// <summary>Subtracts segments from the type's entry, removing it at zero. Pass the same count used to register.</summary>
    public void UnregisterObstaclePlacement(ObstacleSO type, int segments)
    {
        if (type == null || segments <= 0) return;
        int idx = _obstacleSummary.FindIndex(e => e.obstacleType == type);
        if (idx < 0) return;
        _obstacleSummary[idx].count -= segments;
        if (_obstacleSummary[idx].count <= 0) _obstacleSummary.RemoveAt(idx);
    }

    /// <summary>True when at least one plan exists and every plan has completed or failed.</summary>
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

/// <summary>One unit's planned path, time estimate and walked result.</summary>
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

    // Filled in as the unit walks; completed = true once it arrives.
    public List<Vector2Int> actualPath = new List<Vector2Int>();
    public float actualSeconds;
    public bool completed;
}

/// <summary>One obstacle type and the number of cost-bearing segments currently placed.</summary>
[System.Serializable]
public class ObstacleCostEntry
{
    public ObstacleSO obstacleType;
    public int count;

    public int CostPerType => obstacleType != null ? obstacleType.cost * count : 0;
}
