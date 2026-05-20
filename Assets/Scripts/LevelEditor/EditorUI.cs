using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class EditorUI : MonoBehaviour
{
    [Header("References")]
    public BrushController brushController;
    public FlagPlacementTool flagPlacementTool;
    public AvenuesOfApproachHandler aoaHandler;
    public TerrainDataStore terrainDataStore;

    [Header("Tool Buttons")]
    public Button raiseLowerButton;
    public Button flattenButton;
    public Button biomePaintButton;
    public Button flagButton;
    public Button cancelButton;
    public Button exitToMenuButton;

    [Header("Main Menu Scene")]
#if UNITY_EDITOR
    [SerializeField] private UnityEditor.SceneAsset mainMenuScene;
#endif
    [SerializeField, HideInInspector] private string mainMenuSceneName = "UiScene";

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (mainMenuScene != null) mainMenuSceneName = mainMenuScene.name;
    }
#endif

    [Header("Sliders")]
    public Slider radiusSlider;
    public Slider strengthSlider;

    [Header("Value Labels")]
    public TMP_Text radiusText;
    public TMP_Text strengthText;

    [Header("Slider Groups (optional — wraps slider + label so both hide together)")]
    public GameObject radiusGroup;
    public GameObject strengthGroup;

    [Header("Tool Settings Menu")]
    [Tooltip("Root of the shared tool settings panel. Hidden when no tool is active.")]
    public GameObject toolSettingsMenu;
    [Tooltip("Title text at the top of the shared menu; rewritten per active tool.")]
    public TMP_Text settingTitle;
    [Tooltip("Biome dropdown group, shown only for the terrain paint tool.")]
    public GameObject terrainTypeGroup;
    [Tooltip("Avenue-navigation controls, shown only while the AoA tool is open.")]
    public GameObject avenueNavGroup;
    [Tooltip("Circle/Square brush-shape buttons, shown for the radius-based brushes.")]
    public GameObject drawingModeGroup;
    [Tooltip("Selects the circular brush footprint.")]
    public Button circleButton;
    [Tooltip("Selects the square brush footprint.")]
    public Button squareButton;

    [Header("Status Labels")]
    public TMP_Text mainText;
    public TMP_Text subText;

    [Header("Active Button Highlight")]
    public Color activeButtonColor = new Color(0.4f, 0.7f, 1f, 1f);

    Color _raiseLowerNormal;
    Color _flattenNormal;
    Color _biomePaintNormal;
    Color _flagNormal;
    Color _circleNormal;
    Color _squareNormal;

    void Start()
    {
        raiseLowerButton.onClick.AddListener(OnRaiseLowerClicked);
        flattenButton.onClick.AddListener(OnFlattenClicked);
        biomePaintButton.onClick.AddListener(OnBiomePaintClicked);
        if (flagButton != null)
            flagButton.onClick.AddListener(OnFlagClicked);
        if (cancelButton != null)
            cancelButton.onClick.AddListener(OnCancelClicked);
        if (exitToMenuButton != null)
            exitToMenuButton.onClick.AddListener(OnExitToMenuClicked);
        if (circleButton != null)
            circleButton.onClick.AddListener(() => brushController.DrawingShape = BrushShape.Circle);
        if (squareButton != null)
            squareButton.onClick.AddListener(() => brushController.DrawingShape = BrushShape.Square);

        radiusSlider.onValueChanged.AddListener(v =>
        {
            brushController.BrushRadius = v;
            UpdateLabels();
        });

        strengthSlider.onValueChanged.AddListener(v =>
        {
            brushController.BrushStrength = v;
            UpdateLabels();
        });

        // Sync sliders to BrushController defaults
        radiusSlider.value   = brushController.BrushRadius;
        strengthSlider.value = brushController.BrushStrength;

        // Cache the inspector-configured "idle" color for each tool button
        _raiseLowerNormal = raiseLowerButton != null ? raiseLowerButton.colors.normalColor : Color.white;
        _flattenNormal    = flattenButton    != null ? flattenButton.colors.normalColor    : Color.white;
        _biomePaintNormal = biomePaintButton != null ? biomePaintButton.colors.normalColor : Color.white;
        _flagNormal       = flagButton       != null ? flagButton.colors.normalColor       : Color.white;
        _circleNormal     = circleButton     != null ? circleButton.colors.normalColor     : Color.white;
        _squareNormal     = squareButton     != null ? squareButton.colors.normalColor     : Color.white;

        brushController.OnToolChanged  += HandleBrushToolChanged;
        brushController.OnBiomeChanged += HandleBiomeChanged;
        brushController.OnShapeChanged += HandleShapeChanged;
        if (flagPlacementTool != null)
            flagPlacementTool.OnStateChanged += RefreshUI;
        if (aoaHandler != null)
            aoaHandler.OnStateChanged += HandleAoAStateChanged;

        UpdateLabels();
        RefreshUI();
    }

    void OnDestroy()
    {
        if (brushController != null)
        {
            brushController.OnToolChanged  -= HandleBrushToolChanged;
            brushController.OnBiomeChanged -= HandleBiomeChanged;
            brushController.OnShapeChanged -= HandleShapeChanged;
        }
        if (flagPlacementTool != null)
            flagPlacementTool.OnStateChanged -= RefreshUI;
        if (aoaHandler != null)
            aoaHandler.OnStateChanged -= HandleAoAStateChanged;
    }

    // ── Button handlers (own the mutex between brush and flag tools) ──

    void OnRaiseLowerClicked()
    {
        if (flagPlacementTool != null) flagPlacementTool.Cancel();
        if (aoaHandler != null) aoaHandler.Close();
        brushController.ToggleRaiseLower();
    }

    void OnFlattenClicked()
    {
        if (flagPlacementTool != null) flagPlacementTool.Cancel();
        if (aoaHandler != null) aoaHandler.Close();
        brushController.ToggleFlatten();
    }

    void OnBiomePaintClicked()
    {
        if (flagPlacementTool != null) flagPlacementTool.Cancel();
        if (aoaHandler != null) aoaHandler.Close();
        brushController.ToggleBiomePaint();
    }

    void OnFlagClicked()
    {
        brushController.CancelTool();
        if (aoaHandler != null) aoaHandler.Close();
        if (flagPlacementTool != null) flagPlacementTool.Toggle();
    }

    void OnCancelClicked()
    {
        brushController.CancelTool();
        if (flagPlacementTool != null) flagPlacementTool.Cancel();
        if (aoaHandler != null) aoaHandler.Close();
    }

    void OnExitToMenuClicked()
    {
        if (terrainDataStore != null)
        {
            terrainDataStore.WriteSave();
            Debug.Log($"EditorUI: saved level to {terrainDataStore.SaveFilePath} before exit.");
        }
        SceneManager.LoadScene(mainMenuSceneName);
    }

    // ── State change handlers ──

    void HandleBrushToolChanged(BrushTool tool) => RefreshUI();
    void HandleBiomeChanged(BiomeSO biome)      => RefreshUI();
    void HandleShapeChanged(BrushShape shape)   => RefreshUI();

    void HandleAoAStateChanged()
    {
        if (aoaHandler != null && aoaHandler.IsSelected)
        {
            brushController.CancelTool();
            if (flagPlacementTool != null) flagPlacementTool.Cancel();
        }
        RefreshUI();
    }

    void UpdateLabels()
    {
        if (radiusText != null)   radiusText.text   = $"Radius: {brushController.BrushRadius:F1}";
        if (strengthText != null) strengthText.text  = $"Strength: {brushController.BrushStrength:F2}";
    }

    [System.Flags]
    enum ToolSetting
    {
        None        = 0,
        Radius      = 1 << 0,
        Strength    = 1 << 1,
        TerrainType = 1 << 2,
        AvenueNav   = 1 << 3,
        DrawingMode = 1 << 4,
    }

    readonly struct ToolView
    {
        public readonly bool PanelVisible;
        public readonly string Title;
        public readonly ToolSetting Settings;
        public readonly string Sub; // null = hide the sub label

        public ToolView(bool panelVisible, string title, ToolSetting settings, string sub)
        {
            PanelVisible = panelVisible;
            Title        = title;
            Settings     = settings;
            Sub          = sub;
        }
    }

    void RefreshUI()
    {
        BrushTool tool  = brushController.ActiveTool;
        bool flagActive = flagPlacementTool != null && flagPlacementTool.IsActive;
        bool aoaActive  = aoaHandler != null && aoaHandler.IsSelected;
        bool anyActive  = flagActive || aoaActive || tool != BrushTool.None;

        ToolView view = ResolveActiveTool(tool, flagActive);

        if (toolSettingsMenu != null)
            toolSettingsMenu.SetActive(view.PanelVisible);

        if (settingTitle != null)
            settingTitle.text = view.Title;

        SetActive(radiusGroup,      (view.Settings & ToolSetting.Radius)      != 0);
        SetActive(drawingModeGroup, (view.Settings & ToolSetting.DrawingMode) != 0);
        SetActive(strengthGroup,    (view.Settings & ToolSetting.Strength)    != 0);
        SetActive(terrainTypeGroup, (view.Settings & ToolSetting.TerrainType) != 0);
        SetActive(avenueNavGroup,   (view.Settings & ToolSetting.AvenueNav)   != 0);

        // Cancel button visible whenever any tool is active
        if (cancelButton != null)
            cancelButton.gameObject.SetActive(anyActive);

        // Highlight the active tool button (at most one is highlighted)
        SetButtonNormalColor(raiseLowerButton, tool == BrushTool.RaiseLower ? activeButtonColor : _raiseLowerNormal);
        SetButtonNormalColor(flattenButton,    tool == BrushTool.Flatten    ? activeButtonColor : _flattenNormal);
        SetButtonNormalColor(biomePaintButton, tool == BrushTool.BiomePaint ? activeButtonColor : _biomePaintNormal);
        SetButtonNormalColor(flagButton,       flagActive                   ? activeButtonColor : _flagNormal);

        // Drawing-shape buttons stay highlighted to show the current selection
        SetButtonNormalColor(circleButton, brushController.DrawingShape == BrushShape.Circle ? activeButtonColor : _circleNormal);
        SetButtonNormalColor(squareButton, brushController.DrawingShape == BrushShape.Square ? activeButtonColor : _squareNormal);

        RefreshMainLabel(tool, flagActive, aoaActive);

        if (subText != null)
        {
            subText.gameObject.SetActive(view.Sub != null);
            if (view.Sub != null) subText.text = view.Sub;
        }
    }

    // Per-tool view config. Adding a future tool is one branch here — nothing
    // else in the menu wiring needs to change.
    ToolView ResolveActiveTool(BrushTool tool, bool flagActive)
    {
        // AoA owns its own start/target warning gate. While selected but
        // blocked (not open), hide the shared menu so the warning shows alone.
        if (aoaHandler != null && aoaHandler.IsSelected)
        {
            return aoaHandler.IsOpen
                ? new ToolView(true, "Avenues of Approach", ToolSetting.AvenueNav, null)
                : new ToolView(false, string.Empty, ToolSetting.None, null);
        }

        if (flagActive)
        {
            string flagSub = flagPlacementTool.CurrentPhase == FlagPhase.Start
                ? "Click to place START flag"
                : "Click to place TARGET flag";
            return new ToolView(true, "Start & Target", ToolSetting.None, flagSub);
        }

        switch (tool)
        {
            case BrushTool.RaiseLower:
                return new ToolView(true, "Raise / Lower", ToolSetting.Radius | ToolSetting.DrawingMode | ToolSetting.Strength, null);
            case BrushTool.Flatten:
                return new ToolView(true, "Flatten", ToolSetting.Radius | ToolSetting.DrawingMode | ToolSetting.Strength, null);
            case BrushTool.BiomePaint:
                var b = brushController.paintBiome;
                string biomeSub = b != null ? $"Terrain: {b.biomeName}" : "Terrain: (none)";
                return new ToolView(true, "Paint Terrain", ToolSetting.Radius | ToolSetting.DrawingMode | ToolSetting.TerrainType, biomeSub);
            default:
                return new ToolView(false, string.Empty, ToolSetting.None, null);
        }
    }

    // Separate persistent status read-out, independent of the shared menu.
    // Safe to delete the mainText object if it's redundant with settingTitle.
    void RefreshMainLabel(BrushTool tool, bool flagActive, bool aoaActive)
    {
        if (mainText == null) return;

        if (aoaActive)
            mainText.text = "Tool: Avenues of Approach Placer";
        else if (flagActive)
            mainText.text = "Tool: Start and Target";
        else
            mainText.text = tool switch
            {
                BrushTool.RaiseLower => "Brush: Raise / Lower",
                BrushTool.Flatten    => "Brush: Flatten",
                BrushTool.BiomePaint => "Brush: Terrain",
                _                    => "No brush selected",
            };
    }

    static void SetActive(GameObject go, bool active)
    {
        if (go != null) go.SetActive(active);
    }

    static void SetButtonNormalColor(Button b, Color c)
    {
        if (b == null) return;
        var cb = b.colors;
        cb.normalColor   = c;
        cb.selectedColor = c;
        b.colors = cb;
    }
}
