using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EditorUI : MonoBehaviour
{
    [Header("References")]
    public BrushController brushController;

    [Header("Tool Buttons")]
    public Button raiseLowerButton;
    public Button flattenButton;
    public Button biomePaintButton;
    public Button cancelButton;

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

    void Start()
    {
        raiseLowerButton.onClick.AddListener(brushController.ToggleRaiseLower);
        flattenButton.onClick.AddListener(brushController.ToggleFlatten);
        biomePaintButton.onClick.AddListener(brushController.ToggleBiomePaint);
        if (cancelButton != null)
            cancelButton.onClick.AddListener(brushController.CancelTool);

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

        brushController.OnToolChanged  += HandleToolChanged;
        brushController.OnBiomeChanged += HandleBiomeChanged;

        UpdateLabels();
        HandleToolChanged(brushController.ActiveTool);
        HandleBiomeChanged(brushController.paintBiome);
    }

    void OnDestroy()
    {
        if (brushController != null)
        {
            brushController.OnToolChanged  -= HandleToolChanged;
            brushController.OnBiomeChanged -= HandleBiomeChanged;
        }
    }

    void UpdateLabels()
    {
        if (radiusText != null)   radiusText.text   = $"Radius: {brushController.BrushRadius:F1}";
        if (strengthText != null) strengthText.text  = $"Strength: {brushController.BrushStrength:F2}";
    }

    void HandleToolChanged(BrushTool tool)
    {
        RefreshStatusLabels();

        // Cancel button only visible when something is active
        if (cancelButton != null)
            cancelButton.gameObject.SetActive(tool != BrushTool.None);

        // Sliders: hide both when no tool, hide strength for biome paint
        bool toolActive = tool != BrushTool.None;
        bool showStrength = toolActive && tool != BrushTool.BiomePaint;
        SetGroupActive(radiusGroup,   radiusSlider,   radiusText,   toolActive);
        SetGroupActive(strengthGroup, strengthSlider, strengthText, showStrength);

        // Highlight the active tool button
        SetButtonNormalColor(raiseLowerButton, tool == BrushTool.RaiseLower ? activeButtonColor : _raiseLowerNormal);
        SetButtonNormalColor(flattenButton,    tool == BrushTool.Flatten    ? activeButtonColor : _flattenNormal);
        SetButtonNormalColor(biomePaintButton, tool == BrushTool.BiomePaint ? activeButtonColor : _biomePaintNormal);
    }

    void HandleBiomeChanged(BiomeSO biome) => RefreshStatusLabels();

    void RefreshStatusLabels()
    {
        var tool = brushController.ActiveTool;

        if (mainText != null)
        {
            mainText.text = tool switch
            {
                BrushTool.RaiseLower => "Brush: Raise / Lower",
                BrushTool.Flatten    => "Brush: Flatten",
                BrushTool.BiomePaint => "Brush: Terrain",
                _                    => "No brush selected",
            };
        }

        // Sub text = optional extra info; hidden when null
        string sub = GetSubLabel(tool);
        if (subText != null)
        {
            subText.gameObject.SetActive(sub != null);
            if (sub != null) subText.text = sub;
        }
    }

    string GetSubLabel(BrushTool tool)
    {
        switch (tool)
        {
            case BrushTool.BiomePaint:
                var b = brushController.paintBiome;
                return b != null ? $"Terrain: {b.biomeName}" : "Terrain: (none)";
            // Add other brushes here that want a sub-label
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
