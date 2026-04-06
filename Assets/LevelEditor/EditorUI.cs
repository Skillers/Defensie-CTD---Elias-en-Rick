using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EditorUI : MonoBehaviour
{
    [Header("References")]
    public LevelEditorManager  levelManager;
    public BrushController     brushController;
    public PlacementController placementController;

    [Header("Tool Buttons")]
    public Button raiseButton;
    public Button lowerButton;
    public Button smoothButton;
    public Button roadButton;
    public Button zoneButton;
    public Button spawnButton;
    public Button endButton;
    public Button waypointButton;
    public Button walkableButton;

    [Header("Sliders")]
    public Slider radiusSlider;
    public Slider strengthSlider;

    [Header("Dropdowns")]
    public TMP_Dropdown roadTypeDropdown;
    public TMP_Dropdown zoneTypeDropdown;

    [Header("Save / Load")]
    public Button saveButton;
    public Button loadButton;

    void Start()
    {
        raiseButton.onClick.AddListener(()   => SetBrushTool(BrushType.Raise));
        lowerButton.onClick.AddListener(()   => SetBrushTool(BrushType.Lower));
        smoothButton.onClick.AddListener(()  => SetBrushTool(BrushType.Smooth));
        roadButton.onClick.AddListener(()    => SetPlacementTool(PlacementTool.Road));
        zoneButton.onClick.AddListener(()    => SetPlacementTool(PlacementTool.Zone));
        spawnButton.onClick.AddListener(()   => SetPlacementTool(PlacementTool.Spawn));
        endButton.onClick.AddListener(()     => SetPlacementTool(PlacementTool.End));
        waypointButton.onClick.AddListener(()=> SetPlacementTool(PlacementTool.Waypoint));
        walkableButton.onClick.AddListener(()=> SetPlacementTool(PlacementTool.Walkable));

        radiusSlider.onValueChanged.AddListener(v   => brushController.BrushRadius   = v);
        strengthSlider.onValueChanged.AddListener(v => brushController.BrushStrength = v);

        roadTypeDropdown.onValueChanged.AddListener(i =>
            placementController.ActiveRoadType = (RoadType)i);

        zoneTypeDropdown.onValueChanged.AddListener(i =>
            placementController.ActiveZoneType = (ZoneType)(i + 1)); // skip None

        saveButton.onClick.AddListener(levelManager.Save);
        loadButton.onClick.AddListener(levelManager.Load);
    }

    void SetBrushTool(BrushType type)
    {
        brushController.ActiveBrush                = type;
        placementController.ActivePlacementTool    = PlacementTool.None;
    }

    void SetPlacementTool(PlacementTool tool)
    {
        placementController.ActivePlacementTool = tool;

        // Road spline resets when switching to road tool
        if (tool == PlacementTool.Road)
            placementController.ClearRoadSpline();
    }
}
