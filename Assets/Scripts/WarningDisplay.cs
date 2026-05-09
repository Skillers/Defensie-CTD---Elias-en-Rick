using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Simple on-screen banner. Call <see cref="Show"/> with a message and duration;
/// the banner displays for the given seconds, then hides and fires the optional callback.
/// Calling Show again while a banner is active restarts the timer with the new message.
/// </summary>
public class WarningDisplay : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Text component the message is written to.")]
    [SerializeField] TMP_Text text;

    [Tooltip("Optional. Root toggled with the banner. If null, the text's GameObject is toggled instead.")]
    [SerializeField] GameObject root;

    Coroutine _running;

    void Awake()
    {
        SetVisible(false);
    }

    /// <summary>Shows <paramref name="message"/> for <paramref name="seconds"/>, then invokes <paramref name="onDone"/>.</summary>
    public void Show(string message, float seconds, Action onDone = null)
    {
        if (_running != null) StopCoroutine(_running);
        _running = StartCoroutine(ShowRoutine(message, seconds, onDone));
    }

    IEnumerator ShowRoutine(string message, float seconds, Action onDone)
    {
        if (text != null) text.text = message;
        SetVisible(true);
        yield return new WaitForSeconds(Mathf.Max(0f, seconds));
        SetVisible(false);
        _running = null;
        onDone?.Invoke();
    }

    void SetVisible(bool visible)
    {
        if (root != null) root.SetActive(visible);
        else if (text != null) text.gameObject.SetActive(visible);
    }
}
