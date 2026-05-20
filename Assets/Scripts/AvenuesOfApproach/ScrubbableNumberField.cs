using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Transparent click/drag overlay that gives a runtime TMP_InputField the
/// Unity-Editor-style "scrub" affordance:
///   • a plain click forwards focus to the field so the user can type;
///   • a horizontal click-and-drag emits integer step events instead.
///
/// Why an overlay and not a component on the field itself: TMP_InputField
/// already implements IDragHandler (for text-selection), and uGUI dispatches
/// the drag to every handler on the object — a scrubber on the same
/// GameObject would fight it. This overlay must sit ABOVE the field in the
/// UI (a stretched Image, alpha 0, Raycast Target ON) so it receives pointer
/// events first; the field only gets focus when we explicitly forward a click.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ScrubbableNumberField : MonoBehaviour,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IPointerClickHandler
{
    [Tooltip("The input field this overlay sits on top of. Clicks are forwarded here; it is blurred while scrubbing.")]
    [SerializeField] private TMP_InputField targetField;

    [Tooltip("Horizontal mouse pixels per one integer step. Larger = finer / less sensitive.")]
    [SerializeField] private float pixelsPerStep = 8f;

    /// <summary>Fired once when a scrub drag starts.</summary>
    public event Action ScrubBegan;

    /// <summary>Fired per integer step while scrubbing: +1 (dragging right) or -1 (left). May fire multiple times in one frame on a fast drag.</summary>
    public event Action<int> ScrubStepped;

    /// <summary>Fired once when a scrub drag ends.</summary>
    public event Action ScrubEnded;

    float _accum;

    public void OnBeginDrag(PointerEventData eventData)
    {
        _accum = 0f;
        // Make sure the field is not in text-edit mode while we scrub.
        if (targetField != null) targetField.DeactivateInputField();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        ScrubBegan?.Invoke();
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (pixelsPerStep <= 0f) return;
        _accum += eventData.delta.x;
        // Emit one event per whole step so a fast flick still lands every cell.
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
        // uGUI only raises a click when the press stayed under the drag
        // threshold, so this is unambiguously the "tap to type" path.
        if (eventData.dragging) return;
        if (targetField == null) return;
        targetField.ActivateInputField();
        targetField.Select();
    }
}
