using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>Central holder of the CellData grid: queries, grid/world conversion, save/load and obstacle registration.</summary>
public class TerrainDataStore : MonoBehaviour
{
    [Header("Plane Extents (units from center)")]
    public float extentX = 10f;
    public float extentZ = 10f;

    [Header("Grid Settings")]
    public float step = 0.5f;
    public float roundStep = 0.25f;

    [Header("Noise Settings")]
    public int seed = 0;
    public float noiseScale = 0.1f;
    public Vector2 noiseOffset = Vector2.zero;
    public float heightMultiplier = 0f;

    [Header("Save / Load")]
    [SerializeField] string saveFileName = "level.json";
    [Tooltip("Check the save file on Start. Subscribers must register in Awake.")]
    [SerializeField] bool autoLoadOnStart = true;
    [Tooltip("Base biome always registered as used by this terrain.")]
    [SerializeField] BiomeSO baseBiome;

    [HideInInspector] public CellData[,] grid;

    // Biomes actually used in this terrain; base is always present.
    readonly List<BiomeSO> _usedBiomes = new List<BiomeSO>();
    public IReadOnlyList<BiomeSO> UsedBiomes => _usedBiomes;

    public int GridWidth  { get; private set; }
    public int GridHeight { get; private set; }

    public event System.Action OnGridReady;

    public event System.Action<PlacedObstacle> OnObstacleRegistered;
    public event System.Action<PlacedObstacle> OnObstacleUnregistered;

    public event System.Action OnSaveLoaded;
    public event System.Action OnSaveCreated;
    public event System.Action<string> OnSaveFailed;

    // Subscribers add extra fields to the SaveData payload before write / after grid restore.
    public event System.Action<SaveData> OnBuildingSaveData;
    public event System.Action<SaveData> OnApplyingSaveData;

    public string SaveFilePath => Path.Combine(Application.persistentDataPath, saveFileName);
    public bool SaveFileExists => File.Exists(SaveFilePath);
    public string SaveFileName => saveFileName;


    Vector2Int? _startCell;
    public Vector2Int? StartCell => _startCell;

    Vector2Int? _endCell;
    public Vector2Int? EndCell => _endCell;

    private ObstacleGridHelper ObstacleGridHelper { get; } = new();

    void Awake()
    {
        RegisterBiome(baseBiome);
        ApplyLevelSelection();
    }

    // Derive file name and seed from the level selector when present. For existing
    // saves the persisted seed wins; the load path overwrites it after Awake.
    void ApplyLevelSelection()
    {
        if (LevelSelection.Instance == null) return;
        string selected = LevelSelection.Instance.SelectedLevelFileName;
        if (string.IsNullOrEmpty(selected)) return;
        saveFileName = selected;
        seed = DeterministicHash(selected);
    }

    // string.GetHashCode is randomized per-process; this keeps name → seed stable across runs.
    static int DeterministicHash(string s)
    {
        unchecked
        {
            int hash = 23;
            foreach (char c in s) hash = hash * 31 + c;
            return hash;
        }
    }

    void Start()
    {
        if (autoLoadOnStart) CheckSaveFile();
    }

    /// <summary>Adds a biome to the used-biome list if not already present.</summary>
    public void RegisterBiome(BiomeSO biome)
    {
        if (biome == null) return;
        if (!_usedBiomes.Contains(biome))
            _usedBiomes.Add(biome);
    }

    public void SetGrid(CellData[,] cellGrid)
    {
        grid = cellGrid;
        if (grid != null)
        {
            GridWidth  = grid.GetLength(0);
            GridHeight = grid.GetLength(1);
            OnGridReady?.Invoke();
        }
    }


    public CellData GetData(Vector3 worldPos)
    {
        Vector2Int g = WorldToGrid(worldPos);
        return GetData(g.x, g.y);
    }

    public CellData GetData(int gridX, int gridZ)
    {
        if (grid == null) return default;
        int x = Mathf.Clamp(gridX, 0, GridWidth  - 1);
        int z = Mathf.Clamp(gridZ, 0, GridHeight - 1);
        return grid[x, z];
    }


    public bool InBounds(int x, int z)
    {
        return grid != null && x >= 0 && x < GridWidth && z >= 0 && z < GridHeight;
    }

    public Vector2Int WorldToGrid(Vector3 world)
    {
        Vector3 local = world - transform.position;
        int gx = Mathf.RoundToInt((local.x + extentX) / step);
        int gz = Mathf.RoundToInt((local.z + extentZ) / step);
        return new Vector2Int(
            Mathf.Clamp(gx, 0, GridWidth  - 1),
            Mathf.Clamp(gz, 0, GridHeight - 1));
    }

    public Vector3 GridToWorld(Vector2Int cell)
    {
        float wx = -extentX + cell.x * step;
        float wz = -extentZ + cell.y * step;
        return transform.position + new Vector3(wx, 0f, wz);
    }

    public float GetRoundedHeight(int gx, int gz)
    {
        if (grid == null) return 0f;
        int x = Mathf.Clamp(gx, 0, GridWidth  - 1);
        int z = Mathf.Clamp(gz, 0, GridHeight - 1);
        return grid[x, z].roundedHeight;
    }

    public float GetRoundedHeight(Vector3 worldPos)
    {
        Vector2Int g = WorldToGrid(worldPos);
        return GetRoundedHeight(g.x, g.y);
    }

    public float GetRawHeight(int gx, int gz)
    {
        if (grid == null) return 0f;
        int x = Mathf.Clamp(gx, 0, GridWidth  - 1);
        int z = Mathf.Clamp(gz, 0, GridHeight - 1);
        return grid[x, z].rawHeight;
    }

    public float GetRawHeight(Vector3 worldPos)
    {
        Vector2Int g = WorldToGrid(worldPos);
        return GetRawHeight(g.x, g.y);
    }


    public void SetStartCell(Vector2Int cell) => _startCell = cell;
    public void SetEndCell(Vector2Int cell)   => _endCell   = cell;

    public bool RaycastTerrain(Ray ray, out Vector3 hitPoint, float maxDistance = 500f)
    {
        hitPoint = Vector3.zero;
        if (grid == null) return false;

        float stepDist = step * 0.5f;
        bool everInside = false;

        for (float d = 0f; d < maxDistance; d += stepDist)
        {
            Vector3 pos = ray.GetPoint(d);

            float lx = pos.x - transform.position.x;
            float lz = pos.z - transform.position.z;
            bool inside = lx >= -extentX && lx <= extentX && lz >= -extentZ && lz <= extentZ;

            if (!inside)
            {
                if (everInside) return false;
                continue;
            }

            float terrainY = GetRoundedHeight(pos);

            if (!everInside)
            {
                if (pos.y < terrainY) return false;
                everInside = true;
            }

            if (pos.y <= terrainY)
            {
                hitPoint = new Vector3(pos.x, terrainY, pos.z);
                return true;
            }
        }

        return false;
    }

    // ── Save / Load ──────────────────────────────────────────────────────

    /// <summary>Loads the save if present (OnSaveLoaded), fires OnSaveCreated if absent, OnSaveFailed on errors.</summary>
    public void CheckSaveFile()
    {
        Debug.Log($"TerrainDataStore: checking for save at {SaveFilePath} (exists={SaveFileExists}).");

        if (!SaveFileExists)
        {
            OnSaveCreated?.Invoke();
            return;
        }

        try
        {
            string json = File.ReadAllText(SaveFilePath);
            SaveData data = JsonUtility.FromJson<SaveData>(json);
            if (data == null)
            {
                OnSaveFailed?.Invoke("Save file parsed to null.");
                return;
            }
            Debug.Log($"TerrainDataStore: parsed save ({json.Length} chars, grid {data.gridWidth}x{data.gridHeight}, cells={(data.cells != null ? data.cells.Length : 0)}).");
            ApplySaveData(data);
            OnSaveLoaded?.Invoke();
        }
        catch (System.Exception e)
        {
            OnSaveFailed?.Invoke(e.Message);
        }
    }

    /// <summary>Serializes the current grid, settings and flag positions to disk.</summary>
    public void WriteSave()
    {
        if (grid == null)
        {
            Debug.LogWarning("TerrainDataStore.WriteSave: grid is null — nothing to save.");
            return;
        }

        try
        {
            SaveData data = BuildSaveData();
            string json = JsonUtility.ToJson(data);
            File.WriteAllText(SaveFilePath, json);
            Debug.Log($"TerrainDataStore: wrote save ({json.Length} chars, {data.gridWidth}x{data.gridHeight} cells) to {SaveFilePath}.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"TerrainDataStore.WriteSave failed: {e}");
            OnSaveFailed?.Invoke(e.Message);
        }
    }

    SaveData BuildSaveData()
    {
        SaveData data = new SaveData
        {
            extentX = extentX,
            extentZ = extentZ,
            step = step,
            roundStep = roundStep,
            seed = seed,
            noiseScale = noiseScale,
            noiseOffset = noiseOffset,
            heightMultiplier = heightMultiplier,
            gridWidth = GridWidth,
            gridHeight = GridHeight,
            hasStart = _startCell.HasValue,
            startX = _startCell?.x ?? 0,
            startZ = _startCell?.y ?? 0,
            hasEnd = _endCell.HasValue,
            endX = _endCell?.x ?? 0,
            endZ = _endCell?.y ?? 0,
        };

        if (grid != null)
        {
            data.cells = new CellDataDto[GridWidth * GridHeight];
            for (int x = 0; x < GridWidth; x++)
            {
                for (int z = 0; z < GridHeight; z++)
                {
                    CellData c = grid[x, z];
                    data.cells[x * GridHeight + z] = new CellDataDto
                    {
                        rawHeight = c.rawHeight,
                        roundedHeight = c.roundedHeight,
                        biomeName = c.biome != null ? c.biome.biomeName : null,
                    };
                }
            }
        }

        OnBuildingSaveData?.Invoke(data);

        return data;
    }

    void ApplySaveData(SaveData data)
    {
        extentX = data.extentX;
        extentZ = data.extentZ;
        step = data.step;
        roundStep = data.roundStep;
        seed = data.seed;
        noiseScale = data.noiseScale;
        noiseOffset = data.noiseOffset;
        heightMultiplier = data.heightMultiplier;

        _startCell = data.hasStart ? new Vector2Int(data.startX, data.startZ) : (Vector2Int?)null;
        _endCell   = data.hasEnd   ? new Vector2Int(data.endX,   data.endZ)   : (Vector2Int?)null;

        if (data.cells != null && data.gridWidth > 0 && data.gridHeight > 0)
        {
            Dictionary<string, BiomeSO> lookup = BuildBiomeLookup();
            CellData[,] restored = new CellData[data.gridWidth, data.gridHeight];

            for (int x = 0; x < data.gridWidth; x++)
            {
                for (int z = 0; z < data.gridHeight; z++)
                {
                    CellDataDto dto = data.cells[x * data.gridHeight + z];
                    BiomeSO biome = null;
                    if (dto != null && !string.IsNullOrEmpty(dto.biomeName))
                        lookup.TryGetValue(dto.biomeName, out biome);

                    restored[x, z] = new CellData
                    {
                        rawHeight = dto?.rawHeight ?? 0f,
                        roundedHeight = dto?.roundedHeight ?? 0f,
                        biome = biome,
                    };

                    RegisterBiome(biome);
                }
            }

            // slopeOutgoing is derived from rawHeight and not serialized; re-bake before use.
            SlopeMap.BakeSlopesIntoGrid(restored, data.gridWidth, data.gridHeight, step);

            SetGrid(restored);
        }

        OnApplyingSaveData?.Invoke(data);
    }

    // BiomeSO assets must live under Assets/Resources/Biomes/ to load in builds.
    Dictionary<string, BiomeSO> BuildBiomeLookup()
    {
        Dictionary<string, BiomeSO> map = new Dictionary<string, BiomeSO>();
        foreach (BiomeSO b in Resources.LoadAll<BiomeSO>("Biomes"))
        {
            if (b != null && !string.IsNullOrEmpty(b.biomeName))
                map[b.biomeName] = b;
        }
        return map;
    }

    public void RegisterObstacleCells(PlacedObstacle po)
    {
        // Clear the prior footprint so re-registration (e.g. after rotation) leaves no stale entries.
        if (po.affectedCells != null)
        {
            foreach (Vector2Int cell in po.affectedCells)
            {
                if (InBounds(cell.x, cell.y) && grid[cell.x, cell.y].obstacle == po)
                    grid[cell.x, cell.y].obstacle = null;
            }
            po.affectedCells.Clear();
        }

        // Same for the prior radius footprint.
        if (po.affectedRadiusCells != null)
        {
            foreach (Vector2Int cell in po.affectedRadiusCells)
            {
                if (!InBounds(cell.x, cell.y)) continue;
                List<PlacedObstacle> list = grid[cell.x, cell.y].radiusObstacles;
                if (list == null) continue;
                list.Remove(po);
                if (list.Count == 0) grid[cell.x, cell.y].radiusObstacles = null;
            }
            po.affectedRadiusCells.Clear();
        }

        List<Renderer> renderers = ListPool<Renderer>.Get();
        List<Collider> colliders = ListPool<Collider>.Get();

        po.GetComponentsInChildren(renderers);
        po.GetComponentsInChildren(colliders);

        var partObjects = new HashSet<GameObject>();

        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer r = renderers[i];
            if (r.enabled) partObjects.Add(r.gameObject);
        }

        for (int i = 0; i < colliders.Count; i++)
        {
            Collider c = colliders[i];
            if (c.enabled) partObjects.Add(c.gameObject);
        }

        ListPool<Renderer>.Release(renderers);
        ListPool<Collider>.Release(colliders);

        foreach (GameObject part in partObjects)
        {
            Vector3 refPosition = part.transform.position;
            Quaternion refRotation = ObstacleGridHelper.ExtractYawRotation(part.transform.rotation);

            Bounds localBounds = ObstacleGridHelper.GetPartLocalBounds(part, refPosition, refRotation);

            if (localBounds.size == Vector3.zero) continue;

            Vector3[] localCorners = ObstacleGridHelper.GetBoundsCorners(localBounds);
            var worldAABB = new Bounds();
            bool first = true;
            foreach (Vector3 lc in localCorners)
            {
                Vector3 wc = refPosition + refRotation * lc;
                if (first)
                {
                    worldAABB = new Bounds(wc, Vector3.zero);
                    first = false;
                }
                else
                {
                    worldAABB.Encapsulate(wc);
                }
            }

            Vector2Int min = WorldToGrid(worldAABB.min);
            Vector2Int max = WorldToGrid(worldAABB.max);
            Quaternion invRot = Quaternion.Inverse(refRotation);

            for (int x = min.x; x <= max.x; x++)
            for (int z = min.y; z <= max.y; z++)
            {
                if (!InBounds(x, z)) continue;

                Vector3 worldPos = GridToWorld(new Vector2Int(x, z));
                worldPos.y = refPosition.y;
                Vector3 localPos = invRot * (worldPos - refPosition);

                bool insideX = localPos.x >= localBounds.min.x && localPos.x <= localBounds.max.x;
                bool insideZ = localPos.z >= localBounds.min.z && localPos.z <= localBounds.max.z;

                if (!insideX || !insideZ)
                {
                    continue;
                }

                var cell = new Vector2Int(x, z);

                if (po.affectedCells.Contains(cell))
                {
                    continue;
                }

                grid[x, z].obstacle = po;
                po.affectedCells.Add(cell);
            }
        }

        // Fill gaps between line-obstacle segments (e.g. concertina wire)
        if (po.obstacleSo != null && po.obstacleSo.fillSegmentGaps)
        {
            foreach (GameObject part in partObjects)
            {
                ObstacleGridHelper.FillSegmentGapCells(part, this, po);
            }
        }

        // Radius pass. Skips the obstacle's own footprint cells; other obstacles'
        // footprints are included, so radius bleeds across them.
        if (po.obstacleSo != null
            && po.obstacleSo.radiusShape != RadiusShape.None
            && po.obstacleSo.radiusCells > 0
            && po.affectedCells.Count > 0)
        {
            int r = po.obstacleSo.radiusCells;
            RadiusShape shape = po.obstacleSo.radiusShape;
            HashSet<Vector2Int> footprint = new HashSet<Vector2Int>(po.affectedCells);
            HashSet<Vector2Int> radiusCells = new HashSet<Vector2Int>();

            foreach (Vector2Int fp in po.affectedCells)
            {
                for (int dx = -r; dx <= r; dx++)
                for (int dz = -r; dz <= r; dz++)
                {
                    if (dx == 0 && dz == 0) continue;
                    if (shape == RadiusShape.Circle && dx * dx + dz * dz > r * r) continue;

                    int x = fp.x + dx;
                    int z = fp.y + dz;
                    if (!InBounds(x, z)) continue;

                    Vector2Int rc = new Vector2Int(x, z);
                    if (footprint.Contains(rc)) continue;
                    radiusCells.Add(rc);
                }
            }

            foreach (Vector2Int rc in radiusCells)
            {
                if (grid[rc.x, rc.y].radiusObstacles == null)
                    grid[rc.x, rc.y].radiusObstacles = new List<PlacedObstacle>();
                grid[rc.x, rc.y].radiusObstacles.Add(po);
                po.affectedRadiusCells.Add(rc);
            }
        }

        if (po.affectedCells.Count > 0)
        {
            po.OnRegistered(this);
        }

        OnObstacleRegistered?.Invoke(po);
    }

    public void UnregisterObstacleCells(PlacedObstacle po)
    {
        if (po == null) return;

        if (po.affectedCells != null)
        {
            foreach (Vector2Int cell in po.affectedCells)
            {
                if (InBounds(cell.x, cell.y) && grid[cell.x, cell.y].obstacle == po)
                {
                    grid[cell.x, cell.y].obstacle = null;
                }
            }

            po.affectedCells.Clear();
        }

        if (po.affectedRadiusCells != null)
        {
            foreach (Vector2Int cell in po.affectedRadiusCells)
            {
                if (!InBounds(cell.x, cell.y)) continue;
                List<PlacedObstacle> list = grid[cell.x, cell.y].radiusObstacles;
                if (list == null) continue;
                list.Remove(po);
                if (list.Count == 0) grid[cell.x, cell.y].radiusObstacles = null;
            }

            po.affectedRadiusCells.Clear();
        }

        OnObstacleUnregistered?.Invoke(po);
    }
}