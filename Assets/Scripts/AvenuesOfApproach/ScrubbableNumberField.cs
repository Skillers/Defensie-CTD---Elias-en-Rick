using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Transparent overlay that gives a TMP_InputField an editor-style scrub: click to type,
/// horizontal drag to step. Must be an overlay (stretched Image, alpha 0, Raycast Target on)
/// above the field, because TMP_InputField's own IDragHandler would fight a scrubber on the
/// same GameObject.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ScrubbableNumberField : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    [Tooltip("Field this overlay sits on. Clicks are forwarded here; it is blurred while scrubbing.")]
    [SerializeField] private TMP_InputField targetField;

    [Tooltip("Horizontal pixels per integer step.")]
    [SerializeField] private float pixelsPerStep = 8f;

    public event Action ScrubBegan;

    /// <summary>Fired per integer step while scrubbing: +1 right, -1 left.</summary>
    public event Action<int> ScrubStepped;

    public event Action ScrubEnded;

    float _accum;

    public void OnBeginDrag(PointerEventData eventData)
    {
        _accum = 0f;
        if (targetField != null) targetField.DeactivateInputField();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        ScrubBegan?.Invoke();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (pixelsPerStep <= 0f) return;
        _accum += eventData.delta.x;
        // One event per whole step so a fast flick still lands every cell.
        while (Mathf.Abs(_accum) >= pixelsPerStep)
        {
            int dir = _accum > 0f ? 1 : -1;
            _accum -= dir * pixelsPerStep;
            ScrubStepped?.Invoke(dir);
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        ScrubEnded?.Invoke();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // uGUI only raises a click below the drag threshold, so this is the tap-to-type path.
        if (eventData.dragging) return;
        if (targetField == null) return;
        targetField.ActivateInputField();
        targetField.Select();
    }
}
