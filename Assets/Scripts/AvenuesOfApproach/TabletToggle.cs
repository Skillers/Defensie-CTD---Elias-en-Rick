using UnityEngine;
using UnityEngine.UI;
    
///<summary>Swaps between the mini tablet (closed state) and the big tablet (open state).</summary>
public class TabletToggle : MonoBehaviour
{
    [Header("GameObjects")]
    [Tooltip("The Small Button like Ui Tablet.")]
    [SerializeField] GameObject miniTablet;

    [Tooltip("Full-size information Tablet.")]
    [SerializeField] GameObject bigTablet;

 
    [Tooltip("Button Component on the mini tablet. Clicking it opens the big tablet.")]
    [SerializeField] Button openButton;

    [Tooltip("Buttons that close the big tablet.")]
    [SerializeField] Button[] closeButtons;
    
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
