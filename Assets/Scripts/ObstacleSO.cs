using UnityEngine;

public enum PlacementType { Point, Line }
public enum ObstacleEffect { None, Slow, Disrupt, Block, Turn }

[CreateAssetMenu(fileName = "NewObstacle", menuName = "CTD/Obstacle")]
public class ObstacleSO : ScriptableObject
{
    [Header("UI")]
    public string obstacleName;
    public Sprite icon;
    public GameObject prefab;

    [Header("Placement")]
    public PlacementType placementType;

    [Header("Pathfinding")]
    [Tooltip("Effect applied when no per-unit-type override matches.")]
    public ObstacleUnitEffect defaultEffect = new() { effect = ObstacleEffect.None, costMultiplier = 1f };

    [Tooltip("Per-unit-type overrides. First match wins; otherwise falls back to defaultEffect.")]
    public ObstacleUnitEffect[] unitEffects;

    public ObstacleUnitEffect ResolveEffect(UnitTypeSO unitType)
    {
        if (unitType != null && unitEffects != null)
        {
            for (int i = 0; i < unitEffects.Length; i++)
            {
                if (unitEffects[i].unitType == unitType)
                    return unitEffects[i];
            }
        }
        return defaultEffect;
    }
}

[System.Serializable]
public struct ObstacleUnitEffect
{
    public UnitTypeSO unitType;
    public ObstacleEffect effect;

    [Tooltip("Cost multiplier when effect is Slow. Ignored for Block / None / Disrupt / Turn.")]
    public float costMultiplier;
}
