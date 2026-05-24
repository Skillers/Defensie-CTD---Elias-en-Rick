using UnityEngine;

/// <summary>
/// Effect a single grid cell exerts on a unit attempting to traverse it.
/// Shared by <see cref="BiomeSO"/> (the cell's intrinsic terrain) and
/// <see cref="ObstacleSO"/> (overlay placed on top). The pathfinder evaluates
/// biome and obstacle independently, both can block, and Slow multipliers
/// from each source compose multiplicatively.
/// </summary>
public enum CellEffect { None, Slow, Disrupt, Block, Turn }

/// <summary>
/// An effect + its cost multiplier, with no unit-type binding. Used as the
/// default on <see cref="BiomeSO"/> / <see cref="ObstacleSO"/> and as the
/// return type of their ResolveEffect calls.
/// </summary>
[System.Serializable]
public struct CellEffectSpec
{
    public CellEffect effect;

    [Tooltip("Cost multiplier when effect is Slow. Ignored for Block / None / Disrupt / Turn.")]
    public float costMultiplier;
}

/// <summary>
/// Per-unit-type effect override. Used in <see cref="BiomeSO.unitEffects"/>
/// and <see cref="ObstacleSO.unitEffects"/>.
/// </summary>
[System.Serializable]
public struct CellUnitEffect
{
    public UnitTypeSO unitType;
    public CellEffect effect;

    [Tooltip("Cost multiplier when effect is Slow. Ignored for Block / None / Disrupt / Turn.")]
    public float costMultiplier;
}
