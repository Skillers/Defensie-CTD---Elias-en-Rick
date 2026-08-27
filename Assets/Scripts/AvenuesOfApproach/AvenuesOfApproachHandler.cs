using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>One player-defined avenue of approach: a title, ordered grid waypoints and their runtime visuals.</summary>
[Serializable]
public class AvenueOfApproach
{
    public string title;
    public List<Vector2Int> waypoints = new List<Vector2Int>();

    [NonSerialized] public List<GameObject> waypointSpheres = new List<GameObject>();
    [NonSerialized] public LineRenderer line;
}

/// <summary>
/// Handler for the Avenues of Approach tool: panel open/close state, the avenue
/// list, and click-to-place waypoint editing on the terrain.
/// </summary>
public class AvenuesOfApproachHandler : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Used to verify start and target flags before the tool can open.")]
    [SerializeField] private TerrainDataStore terrainDataStore;

    [Tooltip("Camera used to raycast clicks onto the terrain.")]
    [SerializeField] private Camera editorCamera;

    [Header("UI")]
    [SerializeField] private Button toggleButton;

    [Tooltip("Warning shown when opening without start/target flags placed.")]
    [SerializeField] private GameObject warningPanel;

    [Tooltip("Rewritten based on which flag(s) are missing.")]
    [SerializeField] private TMP_Text warningText;

    [Tooltip("Tells the player which tool fixes the missing flags.")]
    [SerializeField] private TMP_Text adviceText;

    [Header("Avenue Navigation")]
    [SerializeField] private Button previousButton;
    [SerializeField] private Button nextButton;
    [SerializeField] private Button newAvenueButton;
    [SerializeField] private Button removeAvenueButton;

    [Tooltip("Edits the current avenue's title.")]
    [SerializeField] private TMP_InputField titleInput;

    [Tooltip("Shows '<current> / <total>' for the avenues list.")]
    [SerializeField] private TMP_Text counterText;

    [Header("Waypoint Placement")]
    [Tooltip("Optional. If null, a default sphere primitive is created per waypoint.")]
    [SerializeField] private GameObject waypointSpherePrefab;

    [Tooltip("Extra height above the terrain surface.")]
    [SerializeField] private float waypointHeightOffset = 0.2f;

    [Header("Waypoint Editing")]
    [Tooltip("Pixel radius within which a left-press grabs an existing waypoint instead of placing a new one.")]
    [SerializeField] private float grabPixelRadius = 24f;

    [SerializeField] private Button prevWaypointButton;
    [SerializeField] private Button nextWaypointButton;
    [SerializeField] private Button waypointXMinusButton;
    [SerializeField] private Button waypointXPlusButton;
    [SerializeField] private Button waypointZMinusButton;
    [SerializeField] private Button waypointZPlusButton;

    [Tooltip("Grid X of the selected waypoint. Commits on end-edit.")]
    [SerializeField] private TMP_InputField waypointXInput;

    [Tooltip("Grid Z of the selected waypoint. Commits on end-edit.")]
    [SerializeField] private TMP_InputField waypointZInput;

    [Tooltip("Optional. Shows '<selected> / <total>' for the current avenue's waypoints.")]
    [SerializeField] private TMP_Text waypointCounterText;

    [Tooltip("Optional drag-scrub overlay over the X field.")]
    [SerializeField] private ScrubbableNumberField waypointXScrub;

    [Tooltip("Optional drag-scrub overlay over the Z field.")]
    [SerializeField] private ScrubbableNumberField waypointZScrub;

    [Header("Waypoint Visuals")]
    [Tooltip("Diameter of the selected avenue's waypoints (except the last).")]
    [SerializeField] private float currentSphereSize = 0.5f;

    [Tooltip("Diameter of the selected avenue's last waypoint.")]
    [SerializeField] private float lastSphereSize = 0.7f;

    [Tooltip("Diameter of other avenues' waypoints.")]
    [SerializeField] private float otherSphereSize = 0.35f;

    [SerializeField] private Color currentSphereColor = new Color(0.878f, 0.447f, 0.031f, 1f);
    [SerializeField] private Color lastSphereColor    = new Color(1f,   0.95f, 0.2f, 1f);
    [SerializeField] private Color otherSphereColor   = new Color(0.55f, 0.55f, 0.6f, 1f);

    [Header("Line Visuals")]
    [Tooltip("Optional. Instanced per avenue so colors don't bleed; an Unlit material is created when null.")]
    [SerializeField] private Material lineMaterial;

    [SerializeField] private float currentLineWidth = 0.25f;
    [SerializeField] private float otherLineWidth = 0.12f;
    [SerializeField] private Color currentLineColor = new Color(1f, 0.549f, 0f, 1f);
    [SerializeField] private Color otherLineColor   = new Color(0.45f, 0.45f, 0.5f, 1f);

    [Tooltip("Spacing of terrain-following samples between waypoints. 0 disables sampling (straight segments).")]
    [SerializeField] private float lineSampleSpacing = 0.5f;

    [Header("Initial State")]
    [SerializeField] private bool startOpen = false;

    bool _isSelected;
    bool _isOpen;
    bool _leftDown;
    bool _draggingOrb;
    int _dragOrbIndex = -1;
    bool _dragMoved;
    int _selectedWaypoint = -1;
    bool _scrubChanged;

    readonly List<AvenueOfApproach> _avenues = new List<AvenueOfApproach>();
    int _currentIndex = -1;
    GameObject _startIndicatorSphere;

    /// <summary>True when AoA is the user's currently chosen tool, even if the panel is blocked behind a warning.</summary>
    public bool IsSelected => _isSelected;

    /// <summary>True only while the AoA panel itself is visible.</summary>
    public bool IsOpen => _isOpen;

    /// <summary>All avenues currently defined (read-only view).</summary>
    public IReadOnlyList<AvenueOfApproach> Avenues => _avenues;

    /// <summary>Index of the currently selected avenue, or -1 if the list is empty.</summary>
    public int CurrentIndex => _currentIndex;

    /// <summary>The avenue at <see cref="CurrentIndex"/>, or null if the list is empty.</summary>
    public AvenueOfApproach CurrentAvenue =>
        _currentIndex >= 0 && _currentIndex < _avenues.Count ? _avenues[_currentIndex] : null;

    /// <summary>True if at least one avenue has been created.</summary>
    public bool HasAvenues => _avenues.Count > 0;

    /// <summary>Fires whenever IsSelected or IsOpen changes.</summary>
    public event Action OnStateChanged;

    /// <summary>Fires whenever the avenue list, current index, title, or waypoints change.</summary>
    public event Action OnAvenueChanged;

    void Awake()
    {
        // Subscribe in Awake so we're hooked up before TerrainDataStore.Start
        // fires its auto-load and we'd miss OnApplyingSaveData.
        if (terrainDataStore != null)
        {
            terrainDataStore.OnBuildingSaveData += HandleBuildingSaveData;
            terrainDataStore.OnApplyingSaveData += HandleApplyingSaveData;
        }
    }

    void Start()
    {
        if (toggleButton != null)        toggleButton.onClick.AddListener(Toggle);
        if (previousButton != null)      previousButton.onClick.AddListener(GoToPrevious);
        if (nextButton != null)          nextButton.onClick.AddListener(GoToNext);
        if (newAvenueButton != null)     newAvenueButton.onClick.AddListener(CreateNewAvenue);
        if (removeAvenueButton != null)  removeAvenueButton.onClick.AddListener(RemoveCurrentAvenue);
        if (titleInput != null)          titleInput.onValueChanged.AddListener(OnTitleChanged);

        if (prevWaypointButton  != null) prevWaypointButton.onClick.AddListener(GoToPreviousWaypoint);
        if (nextWaypointButton  != null) nextWaypointButton.onClick.AddListener(GoToNextWaypoint);
        if (waypointXMinusButton != null) waypointXMinusButton.onClick.AddListener(NudgeSelectedWaypointXMinus);
        if (waypointXPlusButton  != null) waypointXPlusButton.onClick.AddListener(NudgeSelectedWaypointXPlus);
        if (waypointZMinusButton != null) waypointZMinusButton.onClick.AddListener(NudgeSelectedWaypointZMinus);
        if (waypointZPlusButton  != null) waypointZPlusButton.onClick.AddListener(NudgeSelectedWaypointZPlus);
        if (waypointXInput != null)      waypointXInput.onEndEdit.AddListener(OnWaypointXChanged);
        if (waypointZInput != null)      waypointZInput.onEndEdit.AddListener(OnWaypointZChanged);

        if (waypointXScrub != null)
        {
            waypointXScrub.ScrubBegan   += OnScrubBegin;
            waypointXScrub.ScrubStepped += OnScrubX;
            waypointXScrub.ScrubEnded   += OnScrubEnd;
        }
        if (waypointZScrub != null)
        {
            waypointZScrub.ScrubBegan   += OnScrubBegin;
            waypointZScrub.ScrubStepped += OnScrubZ;
            waypointZScrub.ScrubEnded   += OnScrubEnd;
        }

        if (warningPanel != null) warningPanel.SetActive(false);

        // Only auto-open if conditions are met; never pop a warning unprompted on scene load.
        if (startOpen && CanOpen()) ApplyState(true);

        RefreshAvenueUI();
    }

    void OnDestroy()
    {
        if (toggleButton != null)        toggleButton.onClick.RemoveListener(Toggle);
        if (previousButton != null)      previousButton.onClick.RemoveListener(GoToPrevious);
        if (nextButton != null)          nextButton.onClick.RemoveListener(GoToNext);
        if (newAvenueButton != null)     newAvenueButton.onClick.RemoveListener(CreateNewAvenue);
        if (removeAvenueButton != null)  removeAvenueButton.onClick.RemoveListener(RemoveCurrentAvenue);
        if (titleInput != null)          titleInput.onValueChanged.RemoveListener(OnTitleChanged);

        if (prevWaypointButton  != null) prevWaypointButton.onClick.RemoveListener(GoToPreviousWaypoint);
        if (nextWaypointButton  != null) nextWaypointButton.onClick.RemoveListener(GoToNextWaypoint);
        if (waypointXMinusButton != null) waypointXMinusButton.onClick.RemoveListener(NudgeSelectedWaypointXMinus);
        if (waypointXPlusButton  != null) waypointXPlusButton.onClick.RemoveListener(NudgeSelectedWaypointXPlus);
        if (waypointZMinusButton != null) waypointZMinusButton.onClick.RemoveListener(NudgeSelectedWaypointZMinus);
        if (waypointZPlusButton  != null) waypointZPlusButton.onClick.RemoveListener(NudgeSelectedWaypointZPlus);
        if (waypointXInput != null)      waypointXInput.onEndEdit.RemoveListener(OnWaypointXChanged);
        if (waypointZInput != null)      waypointZInput.onEndEdit.RemoveListener(OnWaypointZChanged);

        if (waypointXScrub != null)
        {
            waypointXScrub.ScrubBegan   -= OnScrubBegin;
            waypointXScrub.ScrubStepped -= OnScrubX;
            waypointXScrub.ScrubEnded   -= OnScrubEnd;
        }
        if (waypointZScrub != null)
        {
            waypointZScrub.ScrubBegan   -= OnScrubBegin;
            waypointZScrub.ScrubStepped -= OnScrubZ;
            waypointZScrub.ScrubEnded   -= OnScrubEnd;
        }

        if (terrainDataStore != null)
        {
            terrainDataStore.OnBuildingSaveData -= HandleBuildingSaveData;
            terrainDataStore.OnApplyingSaveData -= HandleApplyingSaveData;
        }
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        if (!Application.isPlaying) return;
        // Defer — modifying scene state directly inside OnValidate is unsafe.
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null) RefreshAvenueVisuals();
        };
    }
#endif

    void Update()
    {
        if (!_isOpen) return;
        if (CurrentAvenue == null) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        bool leftPressed = mouse.leftButton.isPressed;
        Vector2 screenPos = mouse.position.ReadValue();

        if (leftPressed && !_leftDown)
        {
            _leftDown = true;
            bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            if (!overUI)
            {
                // Grabbing an existing waypoint of the active avenue takes
                // priority over placing a new one.
                if (TryPickOrbOnCurrentAvenue(screenPos, out int orbIndex))
                {
                    _draggingOrb = true;
                    _dragOrbIndex = orbIndex;
                    _dragMoved = false;
                    // Grabbing an orb also makes it the selected/highlighted one.
                    SelectWaypoint(orbIndex);
                }
                else
                {
                    TryPlaceAtCursor(screenPos);
                }
            }
        }
        else if (leftPressed && _leftDown && _draggingOrb)
        {
            DragOrbToCursor(screenPos);
        }
        else if (!leftPressed && _leftDown)
        {
            _leftDown = false;
            if (_draggingOrb)
            {
                _draggingOrb = false;
                _dragOrbIndex = -1;
                // Defer the change notification (and any save churn) to the
                // end of the drag, and only if the waypoint actually moved.
                if (_dragMoved) OnAvenueChanged?.Invoke();
                _dragMoved = false;
            }
        }
    }

    /// <summary>Nearest current-avenue waypoint to the cursor within grabPixelRadius. Other avenues' orbs are not grabbable.</summary>
    bool TryPickOrbOnCurrentAvenue(Vector2 screenPos, out int index)
    {
        index = -1;
        var avenue = CurrentAvenue;
        if (avenue == null || editorCamera == null) return false;

        int count = Mathf.Min(avenue.waypoints.Count, avenue.waypointSpheres.Count);
        float bestDistSqr = grabPixelRadius * grabPixelRadius;

        for (int i = 0; i < count; i++)
        {
            var sphere = avenue.waypointSpheres[i];
            if (sphere == null) continue;

            Vector3 sp = editorCamera.WorldToScreenPoint(sphere.transform.position);
            if (sp.z <= 0f) continue; // behind the camera

            float dx = sp.x - screenPos.x;
            float dy = sp.y - screenPos.y;
            float distSqr = dx * dx + dy * dy;
            if (distSqr <= bestDistSqr)
            {
                bestDistSqr = distSqr;
                index = i;
            }
        }

        return index >= 0;
    }

    /// <summary>Moves the dragged waypoint to the cell under the cursor. The drag stays alive while over panel UI.</summary>
    void DragOrbToCursor(Vector2 screenPos)
    {
        var avenue = CurrentAvenue;
        if (avenue == null || editorCamera == null || terrainDataStore == null) return;
        if (_dragOrbIndex < 0 || _dragOrbIndex >= avenue.waypoints.Count) return;

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        if (overUI) return;

        var ray = editorCamera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        Vector2Int cell = terrainDataStore.WorldToGrid(hit.point);
        if (cell == avenue.waypoints[_dragOrbIndex]) return; // same cell — nothing to update

        avenue.waypoints[_dragOrbIndex] = cell;
        _dragMoved = true;
        RefreshAvenueVisuals();
        RefreshWaypointEditorUI();
    }

    /// <summary>Wired to a UI button. Selects/deselects AoA. If start/target aren't placed, the panel stays closed but the warning shows and the tool is still considered selected.</summary>
    public void Toggle() => ApplyState(!_isSelected);

    /// <summary>Selects the AoA tool. Opens the panel if start/target are placed; otherwise shows the warning.</summary>
    public void Open() => ApplyState(true);

    /// <summary>Deselects AoA, closes the panel, and hides the warning.</summary>
    public void Close() => ApplyState(false);

    /// <summary>Wired to the warning panel's dismiss button. Treated as deselecting the tool.</summary>
    public void DismissWarning() => ApplyState(false);

    /// <summary>Wired to the "New Avenue" button. Appends a new avenue and switches to it.</summary>
    public void CreateNewAvenue()
    {
        var avenue = new AvenueOfApproach { title = $"Avenue {_avenues.Count + 1}" };
        avenue.line = SpawnLine();
        _avenues.Add(avenue);
        _currentIndex = _avenues.Count - 1;
        _selectedWaypoint = -1; // new avenue has no waypoints yet
        RefreshAvenueUI();
        OnAvenueChanged?.Invoke();
    }

    /// <summary>Wired to the "Remove Avenue" button. Destroys the current avenue's visuals, drops it, and clamps the index.</summary>
    public void RemoveCurrentAvenue()
    {
        if (_currentIndex < 0 || _currentIndex >= _avenues.Count) return;
        var avenue = _avenues[_currentIndex];
        DestroyAvenueSpheres(avenue);
        if (avenue.line != null) Destroy(avenue.line.gameObject);
        _avenues.RemoveAt(_currentIndex);
        _currentIndex = _avenues.Count == 0 ? -1 : Mathf.Min(_currentIndex, _avenues.Count - 1);
        ResetSelectionToLast();
        RefreshAvenueUI();
        OnAvenueChanged?.Invoke();
    }

    /// <summary>Wired to the next-arrow button. Steps to the next avenue (no wrap-around).</summary>
    public void GoToNext()
    {
        if (_avenues.Count == 0) return;
        int next = Mathf.Min(_currentIndex + 1, _avenues.Count - 1);
        if (next == _currentIndex) return;
        _currentIndex = next;
        ResetSelectionToLast();
        RefreshAvenueUI();
        OnAvenueChanged?.Invoke();
    }

    /// <summary>Wired to the previous-arrow button. Steps to the previous avenue (no wrap-around).</summary>
    public void GoToPrevious()
    {
        if (_avenues.Count == 0) return;
        int prev = Mathf.Max(_currentIndex - 1, 0);
        if (prev == _currentIndex) return;
        _currentIndex = prev;
        ResetSelectionToLast();
        RefreshAvenueUI();
        OnAvenueChanged?.Invoke();
    }

    /// <summary>Adds a waypoint cell to the current avenue and spawns its sphere.</summary>
    public void AddWaypointToCurrentAvenue(Vector2Int cell)
    {
        if (CurrentAvenue == null) return;
        CurrentAvenue.waypoints.Add(cell);
        var sphere = SpawnSphere(cell);
        CurrentAvenue.waypointSpheres.Add(sphere);
        _selectedWaypoint = CurrentAvenue.waypoints.Count - 1; // last placed = selected
        RefreshAvenueVisuals();
        RefreshWaypointEditorUI();
        OnAvenueChanged?.Invoke();
    }

    // ---- Waypoint selection & editing ----

    /// <summary>Selects the current avenue's last waypoint, or none if it has none.</summary>
    void ResetSelectionToLast()
    {
        var avenue = CurrentAvenue;
        _selectedWaypoint = (avenue != null && avenue.waypoints.Count > 0)
            ? avenue.waypoints.Count - 1
            : -1;
    }

    /// <summary>Selects a waypoint of the current avenue by index (clamped). Editor-only state — does not fire OnAvenueChanged.</summary>
    public void SelectWaypoint(int index)
    {
        var avenue = CurrentAvenue;
        int count = avenue != null ? avenue.waypoints.Count : 0;
        _selectedWaypoint = count == 0 ? -1 : Mathf.Clamp(index, 0, count - 1);
        RefreshAvenueVisuals();
        RefreshWaypointEditorUI();
    }

    /// <summary>Wired to the previous-waypoint button. No wrap-around.</summary>
    public void GoToPreviousWaypoint() => SelectWaypoint(_selectedWaypoint - 1);

    /// <summary>Wired to the next-waypoint button. No wrap-around.</summary>
    public void GoToNextWaypoint() => SelectWaypoint(_selectedWaypoint + 1);

    // Wired to the four arrow buttons — nudge the selected waypoint one grid cell.
    public void NudgeSelectedWaypointXMinus() => NudgeSelectedWaypoint(-1, 0);
    public void NudgeSelectedWaypointXPlus()  => NudgeSelectedWaypoint(+1, 0);
    public void NudgeSelectedWaypointZMinus() => NudgeSelectedWaypoint(0, -1);
    public void NudgeSelectedWaypointZPlus()  => NudgeSelectedWaypoint(0, +1);

    void NudgeSelectedWaypoint(int dx, int dz)
    {
        var avenue = CurrentAvenue;
        if (avenue == null || _selectedWaypoint < 0 || _selectedWaypoint >= avenue.waypoints.Count) return;
        Vector2Int c = avenue.waypoints[_selectedWaypoint];
        MoveSelectedWaypointTo(new Vector2Int(c.x + dx, c.y + dz));
    }

    /// <summary>Wired to the X input field's end-edit. Garbage/empty input reverts the field.</summary>
    void OnWaypointXChanged(string s)
    {
        var avenue = CurrentAvenue;
        if (avenue == null || _selectedWaypoint < 0 || _selectedWaypoint >= avenue.waypoints.Count) return;
        if (!int.TryParse(s, out int x)) { RefreshWaypointEditorUI(); return; }
        Vector2Int c = avenue.waypoints[_selectedWaypoint];
        MoveSelectedWaypointTo(new Vector2Int(x, c.y));
    }

    /// <summary>Wired to the Z input field's end-edit. Garbage/empty input reverts the field.</summary>
    void OnWaypointZChanged(string s)
    {
        var avenue = CurrentAvenue;
        if (avenue == null || _selectedWaypoint < 0 || _selectedWaypoint >= avenue.waypoints.Count) return;
        if (!int.TryParse(s, out int z)) { RefreshWaypointEditorUI(); return; }
        Vector2Int c = avenue.waypoints[_selectedWaypoint];
        MoveSelectedWaypointTo(new Vector2Int(c.x, z));
    }

    /// <summary>Moves the selected waypoint and notifies immediately. Used by the type fields and the nudge arrows (one discrete edit each).</summary>
    void MoveSelectedWaypointTo(Vector2Int cell)
    {
        if (ApplySelectedWaypointMove(cell)) OnAvenueChanged?.Invoke();
    }

    /// <summary>Clamps, applies the move and refreshes visuals. Does not fire OnAvenueChanged; the caller decides when.</summary>
    bool ApplySelectedWaypointMove(Vector2Int cell)
    {
        var avenue = CurrentAvenue;
        if (avenue == null || _selectedWaypoint < 0 || _selectedWaypoint >= avenue.waypoints.Count) return false;

        if (terrainDataStore != null && terrainDataStore.GridWidth > 0 && terrainDataStore.GridHeight > 0)
        {
            cell.x = Mathf.Clamp(cell.x, 0, terrainDataStore.GridWidth - 1);
            cell.y = Mathf.Clamp(cell.y, 0, terrainDataStore.GridHeight - 1);
        }

        if (cell == avenue.waypoints[_selectedWaypoint])
        {
            // No net change (edge clamp, or a typed value that clamped back).
            // Resync the fields so they show the truth.
            RefreshWaypointEditorUI();
            return false;
        }

        avenue.waypoints[_selectedWaypoint] = cell;
        RefreshAvenueVisuals();
        RefreshWaypointEditorUI();
        return true;
    }

    // ---- Drag-scrub on the X/Z fields (deferred commit) ----

    void OnScrubBegin() => _scrubChanged = false;

    void OnScrubX(int steps)
    {
        var avenue = CurrentAvenue;
        if (avenue == null || _selectedWaypoint < 0 || _selectedWaypoint >= avenue.waypoints.Count) return;
        Vector2Int c = avenue.waypoints[_selectedWaypoint];
        if (ApplySelectedWaypointMove(new Vector2Int(c.x + steps, c.y))) _scrubChanged = true;
    }

    void OnScrubZ(int steps)
    {
        var avenue = CurrentAvenue;
        if (avenue == null || _selectedWaypoint < 0 || _selectedWaypoint >= avenue.waypoints.Count) return;
        Vector2Int c = avenue.waypoints[_selectedWaypoint];
        if (ApplySelectedWaypointMove(new Vector2Int(c.x, c.y + steps))) _scrubChanged = true;
    }

    void OnScrubEnd()
    {
        if (_scrubChanged) OnAvenueChanged?.Invoke();
        _scrubChanged = false;
    }

    /// <summary>Syncs the waypoint cycle/nudge controls and X/Z fields to the current selection.</summary>
    void RefreshWaypointEditorUI()
    {
        var avenue = CurrentAvenue;
        int count = avenue != null ? avenue.waypoints.Count : 0;

        // Defensive clamp — selection can go stale after load/remove.
        if (count == 0) _selectedWaypoint = -1;
        else if (_selectedWaypoint >= count) _selectedWaypoint = count - 1;

        bool hasSel = _selectedWaypoint >= 0 && _selectedWaypoint < count;
        Vector2Int sel = hasSel ? avenue.waypoints[_selectedWaypoint] : default;

        if (waypointCounterText != null)
            waypointCounterText.text = hasSel ? $"{_selectedWaypoint + 1} / {count}" : $"0 / {count}";

        if (waypointXInput != null)
        {
            waypointXInput.interactable = hasSel;
            // SetTextWithoutNotify so syncing from code never re-fires onEndEdit.
            waypointXInput.SetTextWithoutNotify(hasSel ? sel.x.ToString() : string.Empty);
        }
        if (waypointZInput != null)
        {
            waypointZInput.interactable = hasSel;
            waypointZInput.SetTextWithoutNotify(hasSel ? sel.y.ToString() : string.Empty);
        }

        if (prevWaypointButton != null)
            prevWaypointButton.interactable = hasSel && _selectedWaypoint > 0;
        if (nextWaypointButton != null)
            nextWaypointButton.interactable = hasSel && _selectedWaypoint < count - 1;

        if (waypointXMinusButton != null) waypointXMinusButton.interactable = hasSel;
        if (waypointXPlusButton  != null) waypointXPlusButton.interactable  = hasSel;
        if (waypointZMinusButton != null) waypointZMinusButton.interactable = hasSel;
        if (waypointZPlusButton  != null) waypointZPlusButton.interactable  = hasSel;
    }

    void OnTitleChanged(string newTitle)
    {
        if (CurrentAvenue == null) return;
        CurrentAvenue.title = newTitle;
        OnAvenueChanged?.Invoke();
    }

    void HandleBuildingSaveData(SaveData data)
    {
        var dtos = new AvenueDto[_avenues.Count];
        for (int i = 0; i < _avenues.Count; i++)
        {
            var avenue = _avenues[i];
            int waypointCount = avenue.waypoints.Count;
            var dto = new AvenueDto
            {
                index = i,
                title = avenue.title,
                waypointXs = new int[waypointCount],
                waypointZs = new int[waypointCount],
            };
            for (int w = 0; w < waypointCount; w++)
            {
                dto.waypointXs[w] = avenue.waypoints[w].x;
                dto.waypointZs[w] = avenue.waypoints[w].y;
            }
            dtos[i] = dto;
        }
        data.avenues = dtos;
    }

    void HandleApplyingSaveData(SaveData data)
    {
        // Tear down anything that's already in the scene before replacing it.
        for (int i = 0; i < _avenues.Count; i++)
        {
            var existing = _avenues[i];
            DestroyAvenueSpheres(existing);
            if (existing.line != null) Destroy(existing.line.gameObject);
        }
        _avenues.Clear();
        _currentIndex = -1;

        if (data.avenues != null)
        {
            for (int i = 0; i < data.avenues.Length; i++)
            {
                var dto = data.avenues[i];
                if (dto == null) continue;

                var avenue = new AvenueOfApproach { title = dto.title ?? string.Empty };
                avenue.line = SpawnLine();

                int xLen = dto.waypointXs?.Length ?? 0;
                int zLen = dto.waypointZs?.Length ?? 0;
                int n = Mathf.Min(xLen, zLen);
                for (int w = 0; w < n; w++)
                {
                    var cell = new Vector2Int(dto.waypointXs[w], dto.waypointZs[w]);
                    avenue.waypoints.Add(cell);
                    avenue.waypointSpheres.Add(SpawnSphere(cell));
                }

                _avenues.Add(avenue);
            }
            _currentIndex = _avenues.Count > 0 ? 0 : -1;
        }

        ResetSelectionToLast();
        RefreshAvenueUI();
        OnAvenueChanged?.Invoke();
    }

    void TryPlaceAtCursor(Vector2 screenPos)
    {
        if (editorCamera == null || terrainDataStore == null) return;
        var ray = editorCamera.ScreenPointToRay(screenPos);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;
        Vector2Int cell = terrainDataStore.WorldToGrid(hit.point);
        AddWaypointToCurrentAvenue(cell);
    }

    GameObject SpawnSphere(Vector2Int cell)
    {
        Vector3 worldPos = WorldPosForCell(cell);
        GameObject sphere;
        if (waypointSpherePrefab != null)
        {
            sphere = Instantiate(waypointSpherePrefab, worldPos, Quaternion.identity, transform);
        }
        else
        {
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.SetParent(transform);
            sphere.transform.position = worldPos;
            // Drop the auto-collider so the sphere never blocks the next placement raycast.
            var col = sphere.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }
        sphere.name = "AoA_Waypoint";
        return sphere;
    }

    LineRenderer SpawnLine()
    {
        var obj = new GameObject("AoA_Line");
        obj.transform.SetParent(transform);
        var lr = obj.AddComponent<LineRenderer>();
        lr.material = CreateLineMaterialInstance();
        lr.useWorldSpace = true;
        lr.numCornerVertices = 2;
        lr.numCapVertices = 2;
        lr.positionCount = 0;
        return lr;
    }

    Material CreateLineMaterialInstance()
    {
        // Always instance per line so colors don't bleed between avenues.
        if (lineMaterial != null) return new Material(lineMaterial);
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        return new Material(shader);
    }

    Vector3 WorldPosForCell(Vector2Int cell)
    {
        Vector3 pos = terrainDataStore.GridToWorld(cell);
        pos.y = terrainDataStore.GetRoundedHeight(cell.x, cell.y) + waypointHeightOffset;
        return pos;
    }

    void DestroyAvenueSpheres(AvenueOfApproach avenue)
    {
        for (int i = 0; i < avenue.waypointSpheres.Count; i++)
            if (avenue.waypointSpheres[i] != null) Destroy(avenue.waypointSpheres[i]);
        avenue.waypointSpheres.Clear();
    }

    void RefreshAvenueUI()
    {
        int count = _avenues.Count;
        bool hasAny = count > 0;

        if (counterText != null)
            counterText.text = hasAny ? $"{_currentIndex + 1} / {count}" : "0 / 0";

        if (titleInput != null)
        {
            titleInput.interactable = hasAny;
            // SetTextWithoutNotify avoids re-firing OnTitleChanged when we sync from code.
            titleInput.SetTextWithoutNotify(hasAny ? CurrentAvenue.title : string.Empty);
        }

        if (previousButton != null)
            previousButton.interactable = hasAny && _currentIndex > 0;
        if (nextButton != null)
            nextButton.interactable = hasAny && _currentIndex < count - 1;
        if (removeAvenueButton != null)
            removeAvenueButton.interactable = hasAny;

        RefreshAvenueVisuals();
        RefreshWaypointEditorUI();
    }

    void RefreshAvenueVisuals()
    {
        for (int i = 0; i < _avenues.Count; i++)
        {
            var avenue = _avenues[i];
            bool isCurrent = i == _currentIndex;

            for (int w = 0; w < avenue.waypointSpheres.Count; w++)
            {
                var sphere = avenue.waypointSpheres[w];
                if (sphere == null) continue;
                // Reposition each refresh so spheres stay in sync with waypointHeightOffset and terrain height tweaks.
                sphere.transform.position = WorldPosForCell(avenue.waypoints[w]);
                bool isHighlighted = isCurrent && w == _selectedWaypoint;
                ApplySphereStyle(sphere, isCurrent, isHighlighted);
            }

            RefreshAvenueLine(avenue, isCurrent);
        }

        RefreshStartIndicator();
    }

    void RefreshStartIndicator()
    {
        bool shouldShow = CurrentAvenue != null
                       && CurrentAvenue.waypoints.Count == 0
                       && terrainDataStore != null
                       && terrainDataStore.StartCell.HasValue;

        if (!shouldShow)
        {
            if (_startIndicatorSphere != null) _startIndicatorSphere.SetActive(false);
            return;
        }

        if (_startIndicatorSphere == null)
        {
            _startIndicatorSphere = SpawnSphere(terrainDataStore.StartCell.Value);
            _startIndicatorSphere.name = "AoA_StartIndicator";
        }
        _startIndicatorSphere.transform.position = WorldPosForCell(terrainDataStore.StartCell.Value);
        _startIndicatorSphere.SetActive(true);
        ApplySphereStyle(_startIndicatorSphere, isCurrent: true, isHighlighted: true);
    }

    void RefreshAvenueLine(AvenueOfApproach avenue, bool isCurrent)
    {
        if (avenue.line == null) return;

        // Collect control points: start → waypoints → target.
        var controls = new List<Vector2Int>();
        if (terrainDataStore != null && terrainDataStore.StartCell.HasValue)
            controls.Add(terrainDataStore.StartCell.Value);
        for (int w = 0; w < avenue.waypoints.Count; w++)
            controls.Add(avenue.waypoints[w]);
        if (terrainDataStore != null && terrainDataStore.EndCell.HasValue)
            controls.Add(terrainDataStore.EndCell.Value);

        // Build line positions: each control point + interpolated terrain-following samples between them.
        // The samples are render-only — never written back to avenue.waypoints.
        var positions = new List<Vector3>();
        for (int i = 0; i < controls.Count; i++)
        {
            Vector3 startPos = WorldPosForCell(controls[i]);
            positions.Add(startPos);

            if (i >= controls.Count - 1) continue;

            Vector3 endPos = WorldPosForCell(controls[i + 1]);
            float distance = Vector3.Distance(startPos, endPos);
            int samples = lineSampleSpacing > 0f
                ? Mathf.Max(1, Mathf.CeilToInt(distance / lineSampleSpacing))
                : 1;

            for (int s = 1; s < samples; s++)
            {
                float t = s / (float)samples;
                Vector3 sample = Vector3.Lerp(startPos, endPos, t);
                sample.y = terrainDataStore.GetRoundedHeight(sample) + waypointHeightOffset;
                positions.Add(sample);
            }
        }

        avenue.line.positionCount = positions.Count;
        if (positions.Count > 0) avenue.line.SetPositions(positions.ToArray());
        avenue.line.enabled = positions.Count >= 2;

        float width = isCurrent ? currentLineWidth : otherLineWidth;
        Color color = isCurrent ? currentLineColor : otherLineColor;
        avenue.line.startWidth = width;
        avenue.line.endWidth   = width;

        var mat = avenue.line.sharedMaterial;
        if (mat == null) return;
        // Cover both built-in RP (_Color) and URP (_BaseColor).
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        else mat.color = color;
    }

    void ApplySphereStyle(GameObject sphere, bool isCurrent, bool isHighlighted)
    {
        float size;
        Color color;
        if (isHighlighted)  { size = lastSphereSize;    color = lastSphereColor; }
        else if (isCurrent) { size = currentSphereSize; color = currentSphereColor; }
        else                { size = otherSphereSize;   color = otherSphereColor; }

        sphere.transform.localScale = Vector3.one * size;

        var rend = sphere.GetComponent<Renderer>();
        if (rend == null) return;

        var mat = rend.material;
        // Cover both built-in RP (_Color) and URP/HDRP (_BaseColor).
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        else mat.color = color;
    }

    bool CanOpen()
    {
        // No store wired = can't verify, so don't block (lets the tool work in setups without flag tracking).
        if (terrainDataStore == null) return true;
        return terrainDataStore.StartCell.HasValue && terrainDataStore.EndCell.HasValue;
    }

    void ShowWarning()
    {
        if (warningText != null) warningText.text = BuildWarningMessage();
        if (adviceText != null)  adviceText.text  = BuildAdviceMessage();
        if (warningPanel != null) warningPanel.SetActive(true);
    }

    void HideWarning()
    {
        if (warningPanel != null) warningPanel.SetActive(false);
    }

    string BuildWarningMessage()
    {
        if (terrainDataStore == null)
            return "Terrain data missing — cannot verify start / target flags.";

        bool hasStart = terrainDataStore.StartCell.HasValue;
        bool hasEnd   = terrainDataStore.EndCell.HasValue;

        if (!hasStart && !hasEnd)
            return "Place a START and TARGET flag before using Avenues of Approach.";
        if (!hasStart)
            return "Place a START flag before using Avenues of Approach.";
        return "Place a TARGET flag before using Avenues of Approach.";
    }

    string BuildAdviceMessage()
    {
        if (terrainDataStore == null)
            return "Check the scene setup.";

        bool hasStart = terrainDataStore.StartCell.HasValue;
        bool hasEnd   = terrainDataStore.EndCell.HasValue;

        if (!hasStart && !hasEnd)
            return "Use the Start and Target tool on the right to place both.";
        if (!hasStart)
            return "Use the Start and Target tool on the right to place the start.";
        return "Use the Start and Target tool on the right to place the target.";
    }

    void ApplyState(bool selected)
    {
        bool nextOpen = selected && CanOpen();
        bool changed  = _isSelected != selected || _isOpen != nextOpen;

        _isSelected = selected;
        _isOpen     = nextOpen;

        if (_isSelected && !_isOpen) ShowWarning();
        else                          HideWarning();

        if (changed) OnStateChanged?.Invoke();
    }
}
