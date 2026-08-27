using UnityEngine;

/// <summary>Effect a cell exerts on a traversing unit. Biome and obstacle are evaluated independently; Slow multipliers compose.</summary>
public enum CellEffect { None, Slow, Disrupt, Block, Turn }

/// <summary>An effect plus its cost multiplier, with no unit-type binding.</summary>
[System.Serializable]
public struct CellEffectSpec
{
    public CellEffect effect;

    [Tooltip("Cost multiplier when effect is Slow. Ignored for Block / None / Disrupt / Turn.")]
    public float costMultiplier;
}

/// <summary>Per-unit-type effect override.</summary>
[System.Serializable]
public struct CellUnitEffect
{
    public UnitTypeSO unitType;
    public CellEffect effect;

    [Tooltip("Cost multiplier when effect is Slow. Ignored for Block / None / Disrupt / Turn.")]
    public float costMultiplier;
}
