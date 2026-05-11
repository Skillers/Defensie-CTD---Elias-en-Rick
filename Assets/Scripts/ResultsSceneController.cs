using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Wires the results scene together: reads the save file named on the active
/// <see cref="MissionSession"/>, builds the top-down minimap into <see cref="mapImage"/>,
/// and instantiates one <see cref="UnitDataCard"/> per <see cref="UnitPathPlan"/> under
/// <see cref="cardParent"/>.
/// Drop this on any scene GameObject and drag the UI references in the inspector.
/// </summary>
public class ResultsSceneController : MonoBehaviour
{
    [Header("Map")]
    [Tooltip("RawImage that will display the generated minimap. Set its RectTransform to where you want the map to appear (e.g. bottom-left).")]
    [SerializeField] RawImage mapImage;
    [SerializeField] Color predictedColor      = Color.yellow;
    [SerializeField] Color actualColor         = Color.green;
    [Tooltip("Path stamp size in pixels (odd values centre cleanly). 1 = single pixel per cell. 3–5 is much more visible on larger maps.")]
    [SerializeField] int   pathThickness       = 3;
    [Tooltip("Color used for cells whose biome can't be resolved from Resources/Biomes.")]
    [SerializeField] Color fallbackBiomeColor  = Color.magenta;

    [Header("Start / End Markers")]
    [SerializeField] Color startMarkerColor = Color.green;
    [SerializeField] Color endMarkerColor   = Color.red;
    [Tooltip("Marker radius in pixels. 0 = single pixel, 2-4 = small visible disk.")]
    [SerializeField] int   markerRadius     = 3;

    [Header("Unit Cards")]
    [Tooltip("Parent transform under which cards are instantiated. Use a VerticalLayoutGroup for auto-stacking.")]
    [SerializeField] Transform     cardParent;
    [SerializeField] UnitDataCard  cardPrefab;

    [Header("Fallback")]
    [Tooltip("Save file used if MissionSession has no saveFileName when this scene loads (e.g. testing this scene directly).")]
    [SerializeField] string fallbackSaveFileName = "level.json";

    void Start()
    {
        string fileName = (MissionSession.Instance != null && !string.IsNullOrEmpty(MissionSession.Instance.saveFileName))
            ? MissionSession.Instance.saveFileName
            : fallbackSaveFileName;

        SaveData save = SaveFileLoader.LoadSave(fileName);
        if (save == null)
        {
            Debug.LogError($"ResultsSceneController: could not load save '{fileName}'. Map and cards skipped.");
            return;
        }

        IReadOnlyDictionary<string, BiomeSO> biomeLookup = SaveFileLoader.LoadBiomeLookup();
        IReadOnlyList<UnitPathPlan> plans = MissionSession.Instance != null
            ? MissionSession.Instance.Plans
            : null;

        BuildMap(save, biomeLookup, plans);
        BuildCards(plans);
    }

    void BuildMap(SaveData save, IReadOnlyDictionary<string, BiomeSO> biomeLookup, IReadOnlyList<UnitPathPlan> plans)
    {
        if (mapImage == null)
        {
            Debug.LogWarning("ResultsSceneController: mapImage is not assigned — skipping minimap build.");
            return;
        }

        Texture2D tex = ResultsMapRenderer.BuildMapTexture(
            save, biomeLookup, plans,
            predictedColor, actualColor, pathThickness, fallbackBiomeColor,
            startMarkerColor, endMarkerColor, markerRadius);

        // If the RawImage has an AspectRatioFitter sibling, push the map's true aspect
        // into it so the display stays proportional regardless of grid dimensions.
        if (mapImage.TryGetComponent(out AspectRatioFitter fitter))
            fitter.aspectRatio = (float)save.gridWidth / save.gridHeight;

        mapImage.texture = tex;
    }

    void BuildCards(IReadOnlyList<UnitPathPlan> plans)
    {
        if (cardParent == null || cardPrefab == null)
        {
            Debug.LogWarning("ResultsSceneController: cardParent or cardPrefab not assigned — skipping unit cards.");
            return;
        }
        if (plans == null || plans.Count == 0)
            return;

        for (int i = 0; i < plans.Count; i++)
        {
            UnitDataCard card = Instantiate(cardPrefab, cardParent);
            card.Bind(plans[i]);
        }
    }
}
