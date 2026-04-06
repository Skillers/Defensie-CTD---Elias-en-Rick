using UnityEngine;

[CreateAssetMenu(fileName = "NewBiome", menuName = "Config/Biome")]
public class BiomeSO : ScriptableObject
{
    public string biomeName;
    public Color color = Color.white;
    public int defaultMovementCost = 3;

    [Tooltip("Optional per-unit-type movement cost overrides.")]
    public UnitWeight[] unitWeights;

    public int GetMovementCost(UnitTypeSO unitType)
    {
        if (unitType != null && unitWeights != null)
        {
            foreach (var w in unitWeights)
            {
                if (w.unitType == unitType)
                    return w.movementCost;
            }
        }
        return defaultMovementCost;
    }
}

[System.Serializable]
public struct UnitWeight
{
    public UnitTypeSO unitType;
    public int movementCost;
}

[System.Serializable]
public class BiomeCell
{
    public BiomeSO biome;
}
