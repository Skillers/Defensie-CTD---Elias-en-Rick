using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Level editor tool for placing start/end flag markers on the terrain.
/// Click-cycles between placing the start flag and the end flag; each
/// placement overwrites the previous marker of that type. Grid cells are
/// stored in <see cref="TerrainDataStore"/>.
/// </summary>
public class FlagPlacementTool : MonoBehaviour
{
    [Header("References")]
    public TerrainDataStore terrainDataStore;
    public Camera editorCamera;

    [Header("Flag Prefabs")]
    [Tooltip("Prefab spawned at the start point.")]
    public GameObject startFlagPrefab;
    [Tooltip("Prefab spawned at the end point. If null, startFlagPrefab is reused.")]
    public GameObject endFlagPrefab;

    [Header("Placement Offset")]
    [Tooltip("Extra height added to the terrain surface when positioning flags.")]
    public float heightOffset = 0f;

    bool _active;
    bool _placingEnd;   // false = next click places start, true = next click places end
    bool _leftDown;

    GameObject _startFlagInstance;
    GameObject _endFlagInstance;

    /// <summary>True when this tool is the active editor tool.</summary>
    public bool IsActive => _active;

    /// <summary>Wired to a UI button by EditorUI. Toggles the tool on/off.</summary>
    public void Toggle()
    {
        _active = !_active;
        _leftDown = false;

        if (_active)
            Debug.Log($"FlagPlacement ACTIVATED — next click places {(_placingEnd ? "END" : "START")}");
        else
            Debug.Log("FlagPlacement DEACTIVATED");
    }

    void Update()
    {
        if (!_active) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        bool leftPressed = mouse.leftButton.isPressed;

        // Edge-triggered: only fire on the frame the button goes down.
        if (leftPressed && !_leftDown)
        {
            _leftDown = true;
            TryPlaceAtCursor(mouse.position.ReadValue());
        }
        else if (!leftPressed && _leftDown)
        {
            _leftDown = false;
        }
    }

    void TryPlaceAtCursor(Vector2 screenPos)
    {
        if (terrainDataStore == null || editorCamera == null) return;

        var ray = editorCamera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        Vector2Int cell = terrainDataStore.WorldToGrid(hit.point);
        Vector3 worldPos = WorldPosForCell(cell);

        if (!_placingEnd)
        {
            terrainDataStore.SetStartCell(cell);
            SpawnOrMove(ref _startFlagInstance, startFlagPrefab, worldPos, "StartFlag");
            _placingEnd = true;
            Debug.Log($"Start placed at grid {cell} (world {worldPos})");
        }
        else
        {
            var prefab = endFlagPrefab != null ? endFlagPrefab : startFlagPrefab;
            terrainDataStore.SetEndCell(cell);
            SpawnOrMove(ref _endFlagInstance, prefab, worldPos, "EndFlag");
            _placingEnd = false;
            Debug.Log($"End placed at grid {cell} (world {worldPos})");

            // Auto-deactivate after a full start→end placement cycle.
            _active = false;
            _leftDown = false;
            Debug.Log("FlagPlacement DEACTIVATED (cycle complete)");
        }
    }

    Vector3 WorldPosForCell(Vector2Int cell)
    {
        Vector3 pos = terrainDataStore.GridToWorld(cell);
        pos.y = terrainDataStore.GetHeight(cell.x, cell.y) + heightOffset;
        return pos;
    }

    void SpawnOrMove(ref GameObject instance, GameObject prefab, Vector3 worldPos, string objectName)
    {
        if (prefab == null)
        {
            Debug.LogWarning($"FlagPlacementTool: no prefab assigned for {objectName}.");
            return;
        }

        if (instance == null)
        {
            instance = Instantiate(prefab, worldPos, Quaternion.identity, transform);
            instance.name = objectName;
        }
        else
        {
            instance.transform.position = worldPos;
        }
    }
}
