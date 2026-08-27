using TMPro;
using UnityEngine;

/// <summary>
/// One unit's row in the results panel. Attach to your card prefab and wire any subset
/// of the TMP fields you want shown — unused refs are skipped, so the prefab can be as
/// minimal or as detailed as you like.
/// </summary>
public class UnitDataCard : MonoBehaviour
{
    [Header("Optional Text Refs")]
    [SerializeField] TMP_Text titleText;
    [SerializeField] TMP_Text estimatedText;
    [SerializeField] TMP_Text actualText;
    [SerializeField] TMP_Text deltaText;
    [SerializeField] TMP_Text statusText;
    [SerializeField] TMP_Text cellsText;

    public void Bind(UnitPathPlan plan)
    {
        if (plan == null) return;

        if (titleText != null)
            titleText.text = $"Unit #{plan.unitId}";

        if (estimatedText != null)
            estimatedText.text = $"Estimated: {plan.estimatedSeconds:F2}s";

        if (actualText != null)
            actualText.text = plan.completed
                ? $"Actual: {plan.actualSeconds:F2}s"
                : "Actual: -";

        if (deltaText != null)
        {
            if (plan.completed)
            {
                float delta = plan.actualSeconds - plan.estimatedSeconds;
                string sign = delta >= 0f ? "+" : "";
                deltaText.text = $"Diff: {sign}{delta:F2}s";
            }
            else
            {
                deltaText.text = "Diff: -";
            }
        }

        if (statusText != null)
        {
            string status = plan.failed ? "Failed"
                          : plan.completed ? "Completed"
                          : "Walking";
            statusText.text = $"Status: {status}";
        }

        if (cellsText != null)
            cellsText.text = $"Cells: {plan.path.Count} planned / {plan.actualPath.Count} walked";
    }
}
