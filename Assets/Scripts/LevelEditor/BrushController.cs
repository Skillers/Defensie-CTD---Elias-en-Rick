using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public enum BrushTool { None, RaiseLower, Flatten, BiomePaint }
public enum BrushShape { Circle, Square }

public class BrushController : MonoBehaviour
{
    [Header("References")]
    public TerrainDataStore       terrainDataStore;
    public MarchingCubesTerrain   marchingCubes;
    public Camera                 editorCamera;

    [Header("Brush Settings")]
    public float BrushRadius   = 3f;
    public float BrushStrength = 0.1f;

    [Header("Biome Paint")]
    [FormerlySerializedAs("paintBiome")]
    [SerializeField] BiomeSO _paintBiome;

    public BiomeSO paintBiome
    {
        get => _paintBiome;
        set
        {
            if (_paintBiome == value) return;
            _paintBiome = value;
            OnBiomeChanged?.Invoke(_paintBiome);
        }
    }

    [Header("Drawing Shape")]
    [Tooltip("Brush footprint selected in the UI. The apply math is still circular until Square is wired up.")]
    [SerializeField] BrushShape _drawingShape = BrushShape.Circle;

    public BrushShape DrawingShape
    {
        get => _drawingShape;
        set
        {
            if (_drawingShape == value) return;
            _drawingShape = value;
            OnShapeChanged?.Invoke(_drawingShape);
        }
    }

    [Header("Brush Indicator")]
    public Color indicatorColor = Color.yellow;
    public Color centerColor    = Color.red;

    BrushTool _activeTool = BrushTool.None;
    public BrushTool ActiveTool => _activeTool;

    public event Action<BrushTool> OnToolChanged;
    public event Action<BiomeSO>   OnBiomeChanged;
    public event Action<BrushShape> OnShapeChanged;
    bool _leftDown;
    bool _rightDown;
    bool _leftBlocked;
    bool _rightBlocked;
    float _brushTimer;
    const float BrushInterval = 0.15f;
    const float PaintInterval = 0.05f;

    GameObject _indicator;
    GameObject _centerDot;

    const int RingSegments = 64;
    const float RingThickness = 0.15f;

    Mesh _ringMesh;
    Vector3[] _baseInner;
    Vector3[] _baseOuter;

    void Start()
    {
        // Ring indicator
        _baseInner = new Vector3[RingSegments];
        _baseOuter = new Vector3[RingSegments];
        float inner = 1f - RingThickness;

        for (int i = 0; i < RingSegments; i++)
        {
            float angle = (float)i / RingSegments * Mathf.PI * 2f;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            _baseInner[i] = new Vector3(cos * inner, 0f, sin * inner);
            _baseOuter[i] = new Vector3(cos, 0f, sin);
        }

        var tris = new int[RingSegments * 6];
        for (int i = 0; i < RingSegments; i++)
        {
            int next = (i + 1) % RingSegments;
            int t = i * 6;
            tris[t]     = i * 2;
            tris[t + 1] = next * 2;
            tris[t + 2] = i * 2 + 1;
            tris[t + 3] = next * 2;
            tris[t + 4] = next * 2 + 1;
            tris[t + 5] = i * 2 + 1;
        }

        _ringMesh = new Mesh { name = "BrushRing" };
        _ringMesh.MarkDynamic();
        _ringMesh.vertices = new Vector3[RingSegments * 2];
        _ringMesh.triangles = tris;

        _indicator = new GameObject("BrushIndicator");
        _indicator.AddComponent<MeshFilter>().sharedMesh = _ringMesh;
        var mr = _indicator.AddComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mr.sharedMaterial = new Material(shader) { color = indicatorColor };
        _indicator.SetActive(false);

        // Center dot for flatten tool
        _centerDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _centerDot.name = "BrushCenterDot";
        _centerDot.transform.localScale = Vector3.one * 0.5f;
        Destroy(_centerDot.GetComponent<Collider>());
        _centerDot.GetComponent<MeshRenderer>().sharedMaterial = new Material(shader) { color = centerColor };
        _centerDot.SetActive(false);
    }

    public void ToggleRaiseLower() => SetActiveTool(BrushTool.RaiseLower);
    public void ToggleFlatten()    => SetActiveTool(BrushTool.Flatten);
    public void ToggleBiomePaint() => SetActiveTool(BrushTool.BiomePaint);
    public void CancelTool()       => SetActiveTool(BrushTool.None);

    void SetActiveTool(BrushTool tool)
    {
        // Toggle semantics: clicking the active tool cancels it.
        if (tool != BrushTool.None && _activeTool == tool) tool = BrushTool.None;
        if (_activeTool == tool) return;

        var prev = _activeTool;
        _activeTool = tool;
        ResetState();

        if (_activeTool == BrushTool.BiomePaint)
            Debug.Log($"Tool: {prev} -> {_activeTool} (painting: {(paintBiome != null ? paintBiome.biomeName : "NONE")})");
        else
            Debug.Log($"Tool: {prev} -> {_activeTool}");

        OnToolChanged?.Invoke(_activeTool);
    }

    void ResetState()
    {
        _leftDown = _rightDown = false;
        _leftBlocked = _rightBlocked = false;
        _brushTimer = 0f;
        if (_activeTool == BrushTool.None)
        {
            _indicator.SetActive(false);
            _centerDot.SetActive(false);
        }
    }

    void Update()
    {
        if (_activeTool == BrushTool.None)
        {
            _indicator.SetActive(false);
            _centerDot.SetActive(false);
            return;
        }

        var mouse = Mouse.current;
        if (mouse == null) return;

        var ray = editorCamera.ScreenPointToRay(mouse.position.ReadValue());
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            _indicator.SetActive(true);
            _indicator.transform.position = Vector3.zero;
            _indicator.transform.localScale = Vector3.one;
            UpdateRingToTerrain(hit.point);

            // Show center dot only for flatten tool
            if (_activeTool == BrushTool.Flatten)
            {
                _centerDot.SetActive(true);
                _centerDot.transform.position = new Vector3(
                    hit.point.x,
                    terrainDataStore.GetRoundedHeight(hit.point) + 0.3f,
                    hit.point.z);
            }
            else
            {
                _centerDot.SetActive(false);
            }
        }
        else
        {
            _indicator.SetActive(false);
            _centerDot.SetActive(false);
            return;
        }

        // --- Input tracking (shared) ---
        bool leftPressed  = mouse.leftButton.isPressed;
        bool rightPressed = mouse.rightButton.isPressed;

        if (!leftPressed)  _leftBlocked  = false;
        if (!rightPressed) _rightBlocked = false;

        if (_leftDown && rightPressed && !_rightBlocked)
        {
            _leftDown = false;
            _leftBlocked = true;
            Debug.Log("Left RELEASED (switched to right)");
        }

        if (_rightDown && leftPressed && !_leftBlocked)
        {
            _rightDown = false;
            _rightBlocked = true;
            Debug.Log("Right RELEASED (switched to left)");
        }

        if (leftPressed && !_leftDown && !_rightDown && !_leftBlocked)
        {
            _leftDown = true;
            Debug.Log($"Left DOWN at {hit.point}");
        }
        else if (!leftPressed && _leftDown)
        {
            _leftDown = false;
            Debug.Log($"Left UP at {hit.point}");
        }

        if (rightPressed && !_rightDown && !_leftDown && !_rightBlocked)
        {
            _rightDown = true;
            Debug.Log($"Right DOWN at {hit.point}");
        }
        else if (!rightPressed && _rightDown)
        {
            _rightDown = false;
            Debug.Log($"Right UP at {hit.point}");
        }

        // --- Apply tool on interval (fires immediately on first press) ---
        if (_leftDown || _rightDown)
        {
            float interval = _activeTool == BrushTool.BiomePaint ? PaintInterval : BrushInterval;
            _brushTimer += Time.deltaTime;

            if (_brushTimer >= interval)
            {
                _brushTimer = 0f;

                switch (_activeTool)
                {
                    case BrushTool.RaiseLower:
                        float sign = _rightDown ? 1f : -1f;
                        ApplyRaiseLower(hit.point, sign);
                        break;
                    case BrushTool.Flatten:
                        ApplyFlatten(hit.point, _rightDown);
                        break;
                    case BrushTool.BiomePaint:
                        if (_leftDown) ApplyBiomePaint(hit.point);
                        break;
                }
            }
        }
        else
        {
            // Start at interval so next press fires immediately
            _brushTimer = 999f;
        }
    }

    void UpdateRingToTerrain(Vector3 center)
    {
        var verts = new Vector3[RingSegments * 2];
        float offset = 0.25f;

        for (int i = 0; i < RingSegments; i++)
        {
            Vector3 innerWorld = center + _baseInner[i] * BrushRadius;
            Vector3 outerWorld = center + _baseOuter[i] * BrushRadius;

            innerWorld.y = terrainDataStore.GetRoundedHeight(innerWorld) + offset;
            outerWorld.y = terrainDataStore.GetRoundedHeight(outerWorld) + offset;

            verts[i * 2]     = innerWorld;
            verts[i * 2 + 1] = outerWorld;
        }

        _ringMesh.vertices = verts;
        _ringMesh.RecalculateNormals();
        _ringMesh.RecalculateBounds();
    }

    // --- Raise / Lower ---
    void ApplyRaiseLower(Vector3 worldPos, float sign)
    {
        Vector2Int center = terrainDataStore.WorldToGrid(worldPos);
        int radiusCells = Mathf.CeilToInt(BrushRadius / terrainDataStore.step);

        int gxMin = int.MaxValue, gzMin = int.MaxValue;
        int gxMax = int.MinValue, gzMax = int.MinValue;

        for (int dx = -radiusCells; dx <= radiusCells; dx++)
        for (int dz = -radiusCells; dz <= radiusCells; dz++)
        {
            int gx = center.x + dx;
            int gz = center.y + dz;
            if (!terrainDataStore.InBounds(gx, gz)) continue;

            float dist = new Vector2(dx, dz).magnitude * terrainDataStore.step;
            if (dist > BrushRadius) continue;
            float falloff = 1f - dist / BrushRadius;

            terrainDataStore.grid[gx, gz].rawHeight += sign * BrushStrength * falloff * BrushInterval;

            if (gx < gxMin) gxMin = gx;
            if (gx > gxMax) gxMax = gx;
            if (gz < gzMin) gzMin = gz;
            if (gz > gzMax) gzMax = gz;
        }

        if (gxMin <= gxMax)
            marchingCubes.RebuildRegion(gxMin, gzMin, gxMax, gzMax);
    }

    // --- Flatten ---
    void ApplyFlatten(Vector3 worldPos, bool snapToCenter)
    {
        Vector2Int center = terrainDataStore.WorldToGrid(worldPos);
        int radiusCells = Mathf.CeilToInt(BrushRadius / terrainDataStore.step);

        // Determine target height
        float targetHeight;
        if (snapToCenter)
        {
            // Right click: snap to center point height
            targetHeight = terrainDataStore.grid[
                Mathf.Clamp(center.x, 0, terrainDataStore.GridWidth - 1),
                Mathf.Clamp(center.y, 0, terrainDataStore.GridHeight - 1)
            ].rawHeight;
        }
        else
        {
            // Left click: average height of area
            float sum = 0f;
            int count = 0;
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            for (int dz = -radiusCells; dz <= radiusCells; dz++)
            {
                int gx = center.x + dx;
                int gz = center.y + dz;
                if (!terrainDataStore.InBounds(gx, gz)) continue;
                float dist = new Vector2(dx, dz).magnitude * terrainDataStore.step;
                if (dist > BrushRadius) continue;
                sum += terrainDataStore.grid[gx, gz].rawHeight;
                count++;
            }
            targetHeight = count > 0 ? sum / count : 0f;
        }

        int gxMin = int.MaxValue, gzMin = int.MaxValue;
        int gxMax = int.MinValue, gzMax = int.MinValue;

        for (int dx = -radiusCells; dx <= radiusCells; dx++)
        for (int dz = -radiusCells; dz <= radiusCells; dz++)
        {
            int gx = center.x + dx;
            int gz = center.y + dz;
            if (!terrainDataStore.InBounds(gx, gz)) continue;

            float dist = new Vector2(dx, dz).magnitude * terrainDataStore.step;
            if (dist > BrushRadius) continue;
            float falloff = 1f - dist / BrushRadius;

            float current = terrainDataStore.grid[gx, gz].rawHeight;
            float diff = targetHeight - current;
            terrainDataStore.grid[gx, gz].rawHeight += diff * BrushStrength * falloff * BrushInterval;

            if (gx < gxMin) gxMin = gx;
            if (gx > gxMax) gxMax = gx;
            if (gz < gzMin) gzMin = gz;
            if (gz > gzMax) gzMax = gz;
        }

        if (gxMin <= gxMax)
            marchingCubes.RebuildRegion(gxMin, gzMin, gxMax, gzMax);
    }

    // --- Biome Paint ---
    void ApplyBiomePaint(Vector3 worldPos)
    {
        if (paintBiome == null) { Debug.LogWarning("No paintBiome assigned."); return; }

        Vector2Int center = terrainDataStore.WorldToGrid(worldPos);
        int radiusCells = Mathf.CeilToInt(BrushRadius / terrainDataStore.step);

        int gxMin = int.MaxValue, gzMin = int.MaxValue;
        int gxMax = int.MinValue, gzMax = int.MinValue;
        bool painted = false;

        for (int dx = -radiusCells; dx <= radiusCells; dx++)
        for (int dz = -radiusCells; dz <= radiusCells; dz++)
        {
            int gx = center.x + dx;
            int gz = center.y + dz;
            if (!terrainDataStore.InBounds(gx, gz)) continue;

            float dist = new Vector2(dx, dz).magnitude * terrainDataStore.step;
            if (dist > BrushRadius) continue;

            terrainDataStore.grid[gx, gz].biome = paintBiome;
            painted = true;

            if (gx < gxMin) gxMin = gx;
            if (gx > gxMax) gxMax = gx;
            if (gz < gzMin) gzMin = gz;
            if (gz > gzMax) gzMax = gz;
        }

        if (painted)
            terrainDataStore.RegisterBiome(paintBiome);

        if (gxMin <= gxMax)
            marchingCubes.RecolorRegion(gxMin, gzMin, gxMax, gzMax);
    }

    void OnDestroy()
    {
        if (_indicator != null) Destroy(_indicator);
        if (_centerDot != null) Destroy(_centerDot);
    }
}
