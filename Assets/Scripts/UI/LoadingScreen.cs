using System;
using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Full-screen overlay shown from scene start until the terrain finishes building.
/// Visible by default in Awake. Subscribes to <see cref="GameTerrainBuilder.OnBuildProgress"/>
/// (if wired) to tick the percentage text. The animated dots cycle between
/// <see cref="minDots"/> and <see cref="maxDots"/> via the {2} placeholder in
/// <see cref="progressFormat"/>. <see cref="Hide"/> waits out a minimum display
/// time so a fast load doesn't flash, then invokes the supplied callback.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Recommended. A child GameObject containing the panel + text. Toggled with the screen. " +
             "Wire this to a child so this script's GameObject stays active and its coroutines keep running. " +
             "If null, the script's own GameObject is toggled instead.")]
    [SerializeField] GameObject root;

    [Header("Progress Display")]
    [Tooltip("Optional. The scene's GameTerrainBuilder. When wired, the screen reflects its OnBuildProgress.")]
    [SerializeField] GameTerrainBuilder gameTerrainBuilder;

    [Tooltip("Optional. Text rewritten on every progress tick and dot tick. Leave null to skip live updates.")]
    [SerializeField] TMP_Text progressText;

    [Tooltip("Format applied to progressText. {0} = integer percentage 0..100, {1} = status message, {2} = animated dots.")]
    [SerializeField] string progressFormat = "Loading{2} {0}%";

    [Header("Dots Animation")]
    [Tooltip("How often the dot count advances. The full cycle takes (max-min+1) intervals.")]
    [SerializeField] float dotIntervalSeconds = 0.4f;
    [Tooltip("Minimum dots shown in the cycle.")]
    [SerializeField] int minDots = 1;
    [Tooltip("Maximum dots shown in the cycle.")]
    [SerializeField] int maxDots = 3;

    [Header("Behaviour")]
    [Tooltip("Minimum time the screen stays visible after Awake, even if Hide() is called sooner. " +
             "Prevents the loading screen from just flashing on fast loads.")]
    [SerializeField] float minDisplaySeconds = 0.5f;

    float _shownAt;
    float _progress;
    string _message = string.Empty;
    int _dotCount;
    float _nextDotTickAt;

    void Awake()
    {
        SetVisible(true);
        _shownAt = Time.time;
        _dotCount = ClampedMinDots();
        _nextDotTickAt = Time.time + Mathf.Max(0.05f, dotIntervalSeconds);

        // Subscribe in Awake so we catch the very first ReportProgress(0%, "Starting build...")
        // GameTerrainBuilder fires from BuildRoutine.
        if (gameTerrainBuilder != null)
            gameTerrainBuilder.OnBuildProgress += HandleBuildProgress;

        WriteProgress();
    }

    void OnDestroy()
    {
        if (gameTerrainBuilder != null)
            gameTerrainBuilder.OnBuildProgress -= HandleBuildProgress;
    }

    void Update()
    {
        if (Time.time < _nextDotTickAt) return;
        AdvanceDots();
        _nextDotTickAt = Time.time + Mathf.Max(0.05f, dotIntervalSeconds);
        WriteProgress();
    }

    /// <summary>
    /// Hides the loading screen and invokes <paramref name="onHidden"/> once it's actually hidden.
    /// If less than <see cref="minDisplaySeconds"/> has elapsed, waits the remaining time first.
    /// </summary>
    public void Hide(Action onHidden = null)
    {
        StartCoroutine(HideRoutine(onHidden));
    }

    IEnumerator HideRoutine(Action onHidden)
    {
        float remaining = minDisplaySeconds - (Time.time - _shownAt);
        if (remaining > 0f) yield return new WaitForSeconds(remaining);
        SetVisible(false);
        onHidden?.Invoke();
    }

    void HandleBuildProgress(float progress, string message)
    {
        _progress = progress;
        _message = message ?? string.Empty;
        WriteProgress();
    }

    void AdvanceDots()
    {
        int min = ClampedMinDots();
        int max = Mathf.Max(min, maxDots);
        _dotCount = _dotCount >= max ? min : _dotCount + 1;
    }

    int ClampedMinDots() => Mathf.Max(0, minDots);

    void WriteProgress()
    {
        if (progressText == null) return;
        int percent = Mathf.RoundToInt(Mathf.Clamp01(_progress) * 100f);
        string dots = new string('.', _dotCount);
        progressText.text = string.Format(progressFormat, percent, _message, dots);
    }

    void SetVisible(bool visible)
    {
        if (root != null) root.SetActive(visible);
        else gameObject.SetActive(visible);
    }
}
