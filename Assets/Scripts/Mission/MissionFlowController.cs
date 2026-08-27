using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Owns the prep → play transition: the Play button fires <see cref="OnPlay"/> once and swaps the phase labels.</summary>
public class MissionFlowController : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Ends the prep phase. Hidden once the mission starts.")]
    [SerializeField] Button playButton;

    [Header("Phase Labels")]
    [Tooltip("Optional main banner text.")]
    [SerializeField] TMP_Text mainText;
    [Tooltip("Optional sub-banner text. Hidden after Play.")]
    [SerializeField] TMP_Text subText;

    [SerializeField] string prepMainText = "Preparation phase";
    [TextArea]
    [SerializeField] string prepSubText  = "Press the button when you are ready for the enemy to engage.";
    [SerializeField] string playMainText = "Enemy engaged";

    public bool HasStarted { get; private set; }

    /// <summary>Fires once when prep ends. Late subscribers are invoked immediately so a missed event can't deadlock the spawn flow.</summary>
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
