using TMPro;
using UnityEngine;

/// <summary>
/// Live "Total Cost: N" readout for the gameplay scene. Polls
/// <see cref="MissionSession.TotalObstacleCost"/> every frame but only writes to
/// the TMP field when the value changes, so it costs no TMP layout work in steady
/// state. Before the first obstacle is placed (MissionSession not yet created) it
/// displays 0.
/// </summary>
public class ObstacleCostHUD : MonoBehaviour
{
    [Tooltip("Label that receives the running total. Format: 'Total Cost: N'.")]
    [SerializeField] TMP_Text label;

    int _lastShown = int.MinValue;

    void Update()
    {
        int current = MissionSession.Instance != null
            ? MissionSession.Instance.TotalObstacleCost
            : 0;

        if (current == _lastShown) return;
        _lastShown = current;

        if (label != null)
            label.text = $"Total Cost: {current}";
    }
}
