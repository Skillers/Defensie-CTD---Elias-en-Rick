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

    [Header("Sliders")]
    public Slider radiusSlider;
    public Slider strengthSlider;

    [Header("Value Labels")]
    public TMP_Text radiusText;
    public TMP_Text strengthText;

    void Start()
    {
        raiseLowerButton.onClick.AddListener(brushController.ToggleRaiseLower);
        flattenButton.onClick.AddListener(brushController.ToggleFlatten);
        biomePaintButton.onClick.AddListener(brushController.ToggleBiomePaint);

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

        UpdateLabels();
    }

    void UpdateLabels()
    {
        if (radiusText != null)   radiusText.text   = $"Radius: {brushController.BrushRadius:F1}";
        if (strengthText != null) strengthText.text  = $"Strength: {brushController.BrushStrength:F2}";
    }
}
