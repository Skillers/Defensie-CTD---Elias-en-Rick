using UnityEngine;

[CreateAssetMenu(fileName = "NewBiome", menuName = "Config/Biome")]
public class BiomeSO : ScriptableObject
{
    public string biomeName;
    public Color color = Color.white;

    [Header("Pathfinding")]
    [Tooltip("Effect applied when no per-unit-type override matches. Use Slow with costMultiplier >= 1 for slower terrain (e.g. swamp = 3), or Block for impassable terrain (e.g. water for infantry).")]
    public CellEffectSpec defaultEffect = new() { effect = CellEffect.Slow, costMultiplier = 3f };

    [Tooltip("Per-unit-type overrides. First match wins; otherwise falls back to defaultEffect.")]
    public CellUnitEffect[] unitEffects;

    public CellEffectSpec ResolveEffect(UnitTypeSO unitType)
    {
        if (unitType != null && unitEffects != null)
        {
            for (int i = 0; i < unitEffects.Length; i++)
            {
                if (unitEffects[i].unitType == unitType)
                    return new CellEffectSpec
                    {
                        effect         = unitEffects[i].effect,
                        costMultiplier = unitEffects[i].costMultiplier,
                    };
            }
        }
        return defaultEffect;
    }
}
