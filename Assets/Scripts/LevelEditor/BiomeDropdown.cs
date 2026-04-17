using UnityEngine;
using TMPro;

public class BiomeDropdown : MonoBehaviour
{
    [Header("References")]
    public BrushController brushController;
    public TMP_Dropdown dropdown;

    [Header("Available Biomes")]
    public BiomeSO[] biomes;

    void Start()
    {
        dropdown.ClearOptions();

        var options = new System.Collections.Generic.List<TMP_Dropdown.OptionData>();
        foreach (var biome in biomes)
            options.Add(new TMP_Dropdown.OptionData(biome.biomeName));

        dropdown.AddOptions(options);

        dropdown.onValueChanged.AddListener(OnBiomeSelected);

        // Set initial selection
        if (biomes.Length > 0)
        {
            dropdown.value = 0;
            OnBiomeSelected(0);
        }
    }

    void OnBiomeSelected(int index)
    {
        if (index >= 0 && index < biomes.Length)
            brushController.paintBiome = biomes[index];
    }
}
