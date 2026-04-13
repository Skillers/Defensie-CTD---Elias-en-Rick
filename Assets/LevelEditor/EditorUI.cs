using UnityEngine;
using UnityEngine.UI;

public class EditorUI : MonoBehaviour
{
    [Header("References")]
    public BrushController brushController;

    [Header("Sliders")]
    public Slider radiusSlider;
    public Slider strengthSlider;

    void Start()
    {
        radiusSlider.onValueChanged.AddListener(v   => brushController.BrushRadius   = v);
        strengthSlider.onValueChanged.AddListener(v => brushController.BrushStrength = v);
    }
}
