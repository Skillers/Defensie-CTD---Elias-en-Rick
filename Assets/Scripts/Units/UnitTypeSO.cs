using UnityEngine;

[CreateAssetMenu(fileName = "NewUnitType", menuName = "Config/Unit Type")]
public class UnitTypeSO : ScriptableObject
{
    public string typeName;

    [Header("Slope Traversal")]
    [Tooltip("If true, rules match against |slope|. If false, rules match the signed slope (uphill positive, downhill negative).")]
    public bool useAbsoluteSlope = false;

    [Tooltip("Multiplier applied when no rule matches the current slope.")]
    public float defaultSlopeMultiplier = 1f;

    [Tooltip("Ordered list of slope ranges. First match wins. Slopes outside every range fall back to defaultSlopeMultiplier.")]
    public SlopeRule[] slopeRules;

    /// <summary>
    /// Returns the cost/speed multiplier for a step taken across the given slope (degrees).
    /// Sets <paramref name="blocked"/> to true if the unit cannot traverse this slope at all.
    /// </summary>
    public float EvaluateSlope(float slopeDeg, out bool blocked)
    {
        blocked = false;
        float value = useAbsoluteSlope ? Mathf.Abs(slopeDeg) : slopeDeg;

        if (slopeRules != null)
        {
            for (int i = 0; i < slopeRules.Length; i++)
            {
                SlopeRule r = slopeRules[i];
                if (value >= r.minSlopeDeg && value <= r.maxSlopeDeg)
                {
                    if (r.blocked)
                    {
                        blocked = true;
                        return 0f;
                    }
                    return Mathf.Max(0.0001f, r.costMultiplier);
                }
            }
        }

        return Mathf.Max(0.0001f, defaultSlopeMultiplier);
    }
}

[System.Serializable]
public struct SlopeRule
{
    [Tooltip("Lower bound of the slope range (degrees). Inclusive. Negative = downhill, positive = uphill (unless useAbsoluteSlope is on).")]
    public float minSlopeDeg;

    [Tooltip("Upper bound of the slope range (degrees). Inclusive.")]
    public float maxSlopeDeg;

    [Tooltip("Cost multiplier for steps within this range. >1 slows the unit, <1 speeds it up. Ignored if blocked is true.")]
    public float costMultiplier;

    [Tooltip("If true, the unit cannot traverse a step in this slope range.")]
    public bool blocked;
}
