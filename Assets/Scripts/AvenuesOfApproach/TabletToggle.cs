using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Swaps between a small "mini" tablet (always visible, acts as the open button)
/// and a full-size "big" tablet (visible when the player wants to use it).
/// Clicking the mini opens the big and hides the mini; clicking the close button
/// (if wired) returns to the mini.
/// </summary>
public class TabletToggle : MonoBehaviour
{
    [Header("Roots")]
    [Tooltip("Small thumbnail tablet that's visible while the big tablet is closed. Hidden when the big tablet is open.")]
    [SerializeField] GameObject miniTablet;

    [Tooltip("Full-size tablet shown while the tool is open.")]
    [SerializeField] GameObject bigTablet;

    [Header("Buttons")]
    [Tooltip("Button on the mini tablet. Clicking it opens the big tablet.")]
    [SerializeField] Button openButton;

    [Tooltip("Buttons that close the big tablet back to the mini. Wire as many as you want — e.g. an X button on the panel and a transparent backdrop button for click-outside-to-close.")]
    [SerializeField] Button[] closeButtons;

    [Header("Initial State")]
    [SerializeField] bool startOpen = false;

    public bool IsOpen { get; private set; }

    void Start()
    {
        if (openButton != null) openButton.onClick.AddListener(Open);
        if (closeButtons != null)
            for (int i = 0; i < closeButtons.Length; i++)
                if (closeButtons[i] != null) closeButtons[i].onClick.AddListener(Close);
        ApplyState(startOpen);
    }

    void OnDestroy()
    {
        if (openButton != null) openButton.onClick.RemoveListener(Open);
        if (closeButtons != null)
            for (int i = 0; i < closeButtons.Length; i++)
                if (closeButtons[i] != null) closeButtons[i].onClick.RemoveListener(Close);
    }

    public void Open()   => ApplyState(true);
    public void Close()  => ApplyState(false);
    public void Toggle() => ApplyState(!IsOpen);

    void ApplyState(bool open)
    {
        IsOpen = open;
        if (bigTablet != null)  bigTablet.SetActive(open);
        if (miniTablet != null) miniTablet.SetActive(!open);
    }
}
