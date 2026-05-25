using UnityEngine;

public enum PlacementType { Point, Line }

[CreateAssetMenu(fileName = "NewObstacle", menuName = "CTD/Obstacle")]
public class ObstacleSO : ScriptableObject
{
    [Header("UI")]
    public string obstacleName;
    public Sprite icon;
    public GameObject prefab;

    [Header("Placement")]
    public PlacementType placementType;

    [Tooltip("Fill grid cells between child segments along their local Z-axis (use for line obstacles like wire).")]
    public bool fillSegmentGaps = false;

    [Header("Cost")]
    [Tooltip("Budget points consumed per segment. Point obstacles always count as one segment; line obstacles charge once per generated segment. Aggregated per type and totaled in the AAR.")]
    public int cost;

    [Header("Pathfinding")]
    [Tooltip("Effect applied when no per-unit-type override matches.")]
    public CellEffectSpec defaultEffect = new() { effect = CellEffect.None, costMultiplier = 1f };

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
