using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EditorUI : MonoBehaviour
{
    [Header("References")]
    public BrushController brushController;

    [Header("Buttons")]
    public Button brushToggleButton;

    [Header("Sliders")]
    public Slider radiusSlider;
    public Slider strengthSlider;

    [Header("Value Labels")]
    public TMP_Text radiusText;
    public TMP_Text strengthText;

    void Start()
    {
        brushToggleButton.onClick.AddListener(brushController.ToggleBrush);

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

        // Show initial values
        UpdateLabels();
    }

    void UpdateLabels()
    {
        if (radiusText != null)   radiusText.text   = $"Radius: {brushController.BrushRadius:F1}";
        if (strengthText != null) strengthText.text  = $"Strength: {brushController.BrushStrength:F2}";
    }
}
