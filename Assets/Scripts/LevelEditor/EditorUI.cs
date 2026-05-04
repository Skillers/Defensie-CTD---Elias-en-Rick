using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

public class EditorUI : MonoBehaviour
{
    [Header("References")]
    public BrushController brushController;
    public FlagPlacementTool flagPlacementTool;
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

    [Header("Status Labels")]
    public TMP_Text mainText;
    public TMP_Text subText;

    [Header("Active Button Highlight")]
    public Color activeButtonColor = new Color(0.4f, 0.7f, 1f, 1f);

    Color _raiseLowerNormal;
    Color _flattenNormal;
    Color _biomePaintNormal;
    Color _flagNormal;

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

        brushController.OnToolChanged  += HandleBrushToolChanged;
        brushController.OnBiomeChanged += HandleBiomeChanged;
        if (flagPlacementTool != null)
            flagPlacementTool.OnStateChanged += RefreshUI;

        UpdateLabels();
        RefreshUI();
    }

    void OnDestroy()
    {
        if (brushController != null)
        {
            brushController.OnToolChanged  -= HandleBrushToolChanged;
            brushController.OnBiomeChanged -= HandleBiomeChanged;
        }
        if (flagPlacementTool != null)
            flagPlacementTool.OnStateChanged -= RefreshUI;
    }

    // ── Button handlers (own the mutex between brush and flag tools) ──

    void OnRaiseLowerClicked()
    {
        if (flagPlacementTool != null) flagPlacementTool.Cancel();
        brushController.ToggleRaiseLower();
    }

    void OnFlattenClicked()
    {
        if (flagPlacementTool != null) flagPlacementTool.Cancel();
        brushController.ToggleFlatten();
    }

    void OnBiomePaintClicked()
    {
        if (flagPlacementTool != null) flagPlacementTool.Cancel();
        brushController.ToggleBiomePaint();
    }

    void OnFlagClicked()
    {
        brushController.CancelTool();
        if (flagPlacementTool != null) flagPlacementTool.Toggle();
    }

    void OnCancelClicked()
    {
        brushController.CancelTool();
        if (flagPlacementTool != null) flagPlacementTool.Cancel();
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

    void UpdateLabels()
    {
        if (radiusText != null)   radiusText.text   = $"Radius: {brushController.BrushRadius:F1}";
        if (strengthText != null) strengthText.text  = $"Strength: {brushController.BrushStrength:F2}";
    }

    void RefreshUI()
    {
        BrushTool tool   = brushController.ActiveTool;
        bool flagActive  = flagPlacementTool != null && flagPlacementTool.IsActive;
        bool anyActive   = flagActive || tool != BrushTool.None;

        // Cancel button visible whenever any tool is active
        if (cancelButton != null)
            cancelButton.gameObject.SetActive(anyActive);

        // Sliders: only meaningful for brushes; hidden when flag tool or nothing is active
        bool showRadius   = !flagActive && tool != BrushTool.None;
        bool showStrength = showRadius && tool != BrushTool.BiomePaint;
        SetGroupActive(radiusGroup,   radiusSlider,   radiusText,   showRadius);
        SetGroupActive(strengthGroup, strengthSlider, strengthText, showStrength);

        // Highlight the active tool button (at most one is highlighted)
        SetButtonNormalColor(raiseLowerButton, tool == BrushTool.RaiseLower ? activeButtonColor : _raiseLowerNormal);
        SetButtonNormalColor(flattenButton,    tool == BrushTool.Flatten    ? activeButtonColor : _flattenNormal);
        SetButtonNormalColor(biomePaintButton, tool == BrushTool.BiomePaint ? activeButtonColor : _biomePaintNormal);
        SetButtonNormalColor(flagButton,       flagActive                   ? activeButtonColor : _flagNormal);

        RefreshStatusLabels(tool, flagActive);
    }

    void RefreshStatusLabels(BrushTool tool, bool flagActive)
    {
        if (mainText != null)
        {
            if (flagActive)
            {
                mainText.text = "Tool: Place Flags";
            }
            else
            {
                mainText.text = tool switch
                {
                    BrushTool.RaiseLower => "Brush: Raise / Lower",
                    BrushTool.Flatten    => "Brush: Flatten",
                    BrushTool.BiomePaint => "Brush: Terrain",
                    _                    => "No brush selected",
                };
            }
        }

        string sub = GetSubLabel(tool, flagActive);
        if (subText != null)
        {
            subText.gameObject.SetActive(sub != null);
            if (sub != null) subText.text = sub;
        }
    }

    string GetSubLabel(BrushTool tool, bool flagActive)
    {
        if (flagActive)
        {
            return flagPlacementTool.CurrentPhase == FlagPhase.Start
                ? "Click to place START flag"
                : "Click to place END flag";
        }

        switch (tool)
        {
            case BrushTool.BiomePaint:
                var b = brushController.paintBiome;
                return b != null ? $"Terrain: {b.biomeName}" : "Terrain: (none)";
            default:
                return null; // null = hide the sub group
        }
    }

    static void SetGroupActive(GameObject group, Slider slider, TMP_Text label, bool active)
    {
        if (group != null)
        {
            group.SetActive(active);
            return;
        }
        if (slider != null) slider.gameObject.SetActive(active);
        if (label  != null) label.gameObject.SetActive(active);
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
