using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public enum FlagPhase { Start, End }

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
    FlagPhase _phase = FlagPhase.Start;
    bool _leftDown;
    bool _rightDown;
    bool _startPlaced;
    bool _endPlaced;

    GameObject _startFlagInstance;
    GameObject _endFlagInstance;

    /// <summary>True when this tool is the active editor tool.</summary>
    public bool IsActive => _active;

    /// <summary>Which flag the next click will place.</summary>
    public FlagPhase CurrentPhase => _phase;

    /// <summary>Fires whenever IsActive or CurrentPhase changes.</summary>
    public event Action OnStateChanged;

    void Awake()
    {
        // Subscribe in Awake so we're hooked up before TerrainDataStore.Start
        // fires its auto-load and we'd miss OnSaveLoaded.
        if (terrainDataStore != null)
            terrainDataStore.OnSaveLoaded += HandleSaveLoaded;
    }

    void OnDestroy()
    {
        if (terrainDataStore != null)
            terrainDataStore.OnSaveLoaded -= HandleSaveLoaded;
    }

    /// <summary>Wired to a UI button. Turns the tool on (idempotent).</summary>
    public void Activate()
    {
        if (_active) return;
        _active = true;
        _leftDown = false;
        _rightDown = false;
        _startPlaced = false;
        _endPlaced = false;
        _phase = FlagPhase.Start;
        OnStateChanged?.Invoke();
    }

    /// <summary>Wired to a UI button. Turns the tool off (idempotent).</summary>
    public void Cancel()
    {
        if (!_active) return;
        _active = false;
        _leftDown = false;
        _rightDown = false;
        OnStateChanged?.Invoke();
    }

    /// <summary>Convenience toggle for a single button that flips active state.</summary>
    public void Toggle()
    {
        if (_active) Cancel();
        else Activate();
    }

    void Update()
    {
        if (!_active) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        // Block press-onset while the cursor is over UI so clicking tool
        // buttons or panels doesn't drop a flag / flip phase behind the panel.
        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // ── Left click: place the current flag ──
        bool leftPressed = mouse.leftButton.isPressed;
        if (leftPressed && !_leftDown && !overUI)
        {
            _leftDown = true;
            TryPlaceAtCursor(mouse.position.ReadValue());
        }
        else if (!leftPressed && _leftDown)
        {
            _leftDown = false;
        }

        // ── Right click: switch between Start / End phase ──
        bool rightPressed = mouse.rightButton.isPressed;
        if (rightPressed && !_rightDown && !overUI)
        {
            _rightDown = true;
            _phase = _phase == FlagPhase.Start ? FlagPhase.End : FlagPhase.Start;
            OnStateChanged?.Invoke();
        }
        else if (!rightPressed && _rightDown)
        {
            _rightDown = false;
        }
    }

    void TryPlaceAtCursor(Vector2 screenPos)
    {
        if (terrainDataStore == null || editorCamera == null) return;

        var ray = editorCamera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        Vector2Int cell = terrainDataStore.WorldToGrid(hit.point);
        Vector3 worldPos = WorldPosForCell(cell);

        if (_phase == FlagPhase.Start)
        {
            terrainDataStore.SetStartCell(cell);
            SpawnOrMove(ref _startFlagInstance, startFlagPrefab, worldPos, "StartFlag");
            _startPlaced = true;

            // Auto-advance to End if it hasn't been placed yet
            if (!_endPlaced) _phase = FlagPhase.End;
        }
        else
        {
            var prefab = endFlagPrefab != null ? endFlagPrefab : startFlagPrefab;
            terrainDataStore.SetEndCell(cell);
            SpawnOrMove(ref _endFlagInstance, prefab, worldPos, "EndFlag");
            _endPlaced = true;

            // Auto-advance to Start if it hasn't been placed yet
            if (!_startPlaced) _phase = FlagPhase.Start;
        }

        // Both placed → deactivate
        if (_startPlaced && _endPlaced)
        {
            _active = false;
            _leftDown = false;
            _rightDown = false;
        }

        OnStateChanged?.Invoke();
    }

    void HandleSaveLoaded()
    {
        if (terrainDataStore == null) return;

        if (terrainDataStore.StartCell.HasValue)
        {
            Vector3 worldPos = WorldPosForCell(terrainDataStore.StartCell.Value);
            SpawnOrMove(ref _startFlagInstance, startFlagPrefab, worldPos, "StartFlag");
            _startPlaced = true;
        }

        if (terrainDataStore.EndCell.HasValue)
        {
            var prefab = endFlagPrefab != null ? endFlagPrefab : startFlagPrefab;
            Vector3 worldPos = WorldPosForCell(terrainDataStore.EndCell.Value);
            SpawnOrMove(ref _endFlagInstance, prefab, worldPos, "EndFlag");
            _endPlaced = true;
        }

        OnStateChanged?.Invoke();
    }

    Vector3 WorldPosForCell(Vector2Int cell)
    {
        Vector3 pos = terrainDataStore.GridToWorld(cell);
        pos.y = terrainDataStore.GetRoundedHeight(cell.x, cell.y) + heightOffset;
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
