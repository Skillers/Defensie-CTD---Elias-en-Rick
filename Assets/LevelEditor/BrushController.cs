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

    void Start()
    {
        _indicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _indicator.name = "BrushIndicator";
        _indicator.transform.localScale = Vector3.one * 5f;
        Destroy(_indicator.GetComponent<Collider>());

        var mr = _indicator.GetComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        mr.sharedMaterial = new Material(shader) { color = indicatorColor };

        _indicator.SetActive(false);
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
            _indicator.transform.position = hit.point;
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
