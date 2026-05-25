using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Owns the mission's prep → play transition. While <see cref="HasStarted"/> is false
/// the player can prep freely (place obstacles, study the path); pressing the assigned
/// <see cref="playButton"/> fires <see cref="OnPlay"/> once, which is what
/// <see cref="UnitSpawner"/> waits on before instantiating units and starting the timer.
/// Idempotent: repeat clicks after the first are ignored, and the button is disabled
/// so the player can't accidentally re-fire it. Also drives a pair of optional TMP_Text
/// labels (main + sub) that swap between configurable prep / play strings on each phase.
/// </summary>
public class MissionFlowController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Button the player clicks to end the prep phase. Hidden once the mission starts (GameObject disabled).")]
    [SerializeField] Button playButton;

    [Header("Phase Labels")]
    [Tooltip("Optional. Main banner text. Swaps between prepMainText and playMainText on the prep/play transition. Wire to the existing 'Obstacle placement mode' text.")]
    [SerializeField] TMP_Text mainText;
    [Tooltip("Optional. Sub-banner text under mainText. Shows prepSubText during prep and is hidden (GameObject disabled) after Play.")]
    [SerializeField] TMP_Text subText;

    [Tooltip("Main label shown while the player is prepping (before Play).")]
    [SerializeField] string prepMainText = "Preparation phase";
    [Tooltip("Sub label shown while the player is prepping. Hint about what the Play button does.")]
    [TextArea]
    [SerializeField] string prepSubText  = "Press the button when you are ready for the enemy to engage.";
    [Tooltip("Main label shown after Play. Reflects the unit being live on the field.")]
    [SerializeField] string playMainText = "Enemy engaged";

    /// <summary>True once <see cref="StartMission"/> has fired (via the button or programmatically).</summary>
    public bool HasStarted { get; private set; }

    /// <summary>
    /// Fires once when the prep phase ends. Late subscribers (added after the click)
    /// are invoked immediately so the spawn flow doesn't deadlock on a missed event.
    /// </summary>
    public event Action OnPlay
    {
        add
        {
            _onPlay += value;
            if (HasStarted) value?.Invoke();
        }
        remove { _onPlay -= value; }
    }
    Action _onPlay;

    void Awake()
    {
        if (playButton != null) playButton.onClick.AddListener(StartMission);
        ApplyLabels(HasStarted);
    }

    /// <summary>Ends the prep phase. Safe to call repeatedly — only the first call notifies subscribers.</summary>
    public void StartMission()
    {
        if (HasStarted) return;
        HasStarted = true;
        if (playButton != null) playButton.gameObject.SetActive(false);
        ApplyLabels(true);
        _onPlay?.Invoke();
    }

    void ApplyLabels(bool started)
    {
        if (mainText != null) mainText.text = started ? playMainText : prepMainText;
        if (subText != null)
        {
            if (!started) subText.text = prepSubText;
            subText.gameObject.SetActive(!started);
        }
    }
}
