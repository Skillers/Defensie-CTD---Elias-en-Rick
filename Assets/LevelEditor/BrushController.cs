using UnityEngine;
using UnityEngine.InputSystem;

public class BrushController : MonoBehaviour
{
    [Header("References")]
    public TerrainDataStore       terrainDataStore;
    public MarchingCubesTerrain   marchingCubes;
    public Camera                 editorCamera;

    [Header("Brush Settings")]
    public float BrushRadius   = 3f;
    public float BrushStrength = 0.1f;

    [Header("Brush Indicator")]
    public Color indicatorColor = Color.yellow;

    bool _brushActive;
    bool _leftDown;
    bool _rightDown;
    bool _leftBlocked;   // ignore left until physically released
    bool _rightBlocked;  // ignore right until physically released
    GameObject _indicator;

    const int RingSegments = 64;
    const float RingThickness = 0.15f; // fraction of radius

    Mesh _ringMesh;
    Vector3[] _baseInner;
    Vector3[] _baseOuter;

    void Start()
    {
        // Pre-compute unit circle directions
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

        // Build initial mesh with triangles (verts updated each frame)
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
    }

    void UpdateRingToTerrain(Vector3 center)
    {
        var verts = new Vector3[RingSegments * 2];
        float offset = 0.15f; // hover above surface

        for (int i = 0; i < RingSegments; i++)
        {
            Vector3 innerWorld = center + _baseInner[i] * BrushRadius;
            Vector3 outerWorld = center + _baseOuter[i] * BrushRadius;

            innerWorld.y = terrainDataStore.GetHeight(innerWorld) + offset;
            outerWorld.y = terrainDataStore.GetHeight(outerWorld) + offset;

            // Store in local space (indicator at origin)
            verts[i * 2]     = innerWorld;
            verts[i * 2 + 1] = outerWorld;
        }

        _ringMesh.vertices = verts;
        _ringMesh.RecalculateNormals();
        _ringMesh.RecalculateBounds();
    }

    public void ToggleBrush()
    {
        _brushActive = !_brushActive;
        Debug.Log($"Brush {(_brushActive ? "ACTIVATED" : "DEACTIVATED")}");
        if (!_brushActive) _indicator.SetActive(false);
    }

    void Update()
    {
        if (!_brushActive)
        {
            _indicator.SetActive(false);
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
        }
        else
        {
            _indicator.SetActive(false);
            return;
        }

        bool leftPressed  = mouse.leftButton.isPressed;
        bool rightPressed = mouse.rightButton.isPressed;

        // Unblock once physically released
        if (!leftPressed)  _leftBlocked  = false;
        if (!rightPressed) _rightBlocked = false;

        // Right pressed while left is held — switch
        if (_leftDown && rightPressed && !_rightBlocked)
        {
            _leftDown = false;
            _leftBlocked = true;  // ignore left until released
            Debug.Log("Left RELEASED (switched to right)");
        }

        // Left pressed while right is held — switch
        if (_rightDown && leftPressed && !_leftBlocked)
        {
            _rightDown = false;
            _rightBlocked = true;  // ignore right until released
            Debug.Log("Right RELEASED (switched to left)");
        }

        // Track left
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

        // Track right
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
    }

    void OnDestroy()
    {
        if (_indicator != null) Destroy(_indicator);
    }
}
