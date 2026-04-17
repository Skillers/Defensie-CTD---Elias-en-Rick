using System;
using UnityEngine;
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

    GameObject _startFlagInstance;
    GameObject _endFlagInstance;

    /// <summary>True when this tool is the active editor tool.</summary>
    public bool IsActive => _active;

    /// <summary>Which flag the next click will place.</summary>
    public FlagPhase CurrentPhase => _phase;

    /// <summary>Fires whenever IsActive or CurrentPhase changes.</summary>
    public event Action OnStateChanged;

    /// <summary>Wired to a UI button. Turns the tool on (idempotent).</summary>
    public void Activate()
    {
        if (_active) return;
        _active = true;
        _leftDown = false;
        Debug.Log($"FlagPlacement ACTIVATED — next click places {_phase}");
        OnStateChanged?.Invoke();
    }

    /// <summary>Wired to a UI button. Turns the tool off (idempotent).</summary>
    public void Cancel()
    {
        if (!_active) return;
        _active = false;
        _leftDown = false;
        Debug.Log("FlagPlacement DEACTIVATED");
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

        if (_phase == FlagPhase.Start)
        {
            terrainDataStore.SetStartCell(cell);
            SpawnOrMove(ref _startFlagInstance, startFlagPrefab, worldPos, "StartFlag");
            _phase = FlagPhase.End;
            Debug.Log($"Start placed at grid {cell} (world {worldPos})");
            OnStateChanged?.Invoke();
        }
        else
        {
            var prefab = endFlagPrefab != null ? endFlagPrefab : startFlagPrefab;
            terrainDataStore.SetEndCell(cell);
            SpawnOrMove(ref _endFlagInstance, prefab, worldPos, "EndFlag");
            _phase = FlagPhase.Start;
            Debug.Log($"End placed at grid {cell} (world {worldPos})");

            // Auto-deactivate after a full start→end placement cycle.
            _active = false;
            _leftDown = false;
            Debug.Log("FlagPlacement DEACTIVATED (cycle complete)");
            OnStateChanged?.Invoke();
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
