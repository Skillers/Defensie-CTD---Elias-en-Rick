using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Text))]
[ExecuteAlways]
public class FollowFontSize : MonoBehaviour
{
    [SerializeField] private TMP_Text source;
    [SerializeField, Tooltip("Multiplier applied to the source font size.")]
    private float scale = 1f;
    [SerializeField, Tooltip("Added to the source font size after scaling.")]
    private float offset = 0f;

    private TMP_Text target;
    private float lastSize = float.NaN;

    private void Awake()
    {
        target = GetComponent<TMP_Text>();
    }

    private void LateUpdate()
    {
        if (source == null || target == null) return;

        float size = source.fontSize * scale + offset;
        if (!Mathf.Approximately(size, lastSize))
        {
            target.fontSize = size;
            lastSize = size;
        }
    }
}
