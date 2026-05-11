using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach to the root Button GameObject of the ItemButton prefab.
/// Exposes image and label so variants can be configured in the Inspector
/// or set at runtime via SetItem().
/// </summary>
public class PlacableObstacleButton : MonoBehaviour
{
    [Header("Content")]
    [SerializeField] private ObstacleSO obstacle;
    [SerializeField] private Sprite itemImage;
    [SerializeField] private string itemLabel;
    
    [Header("References")]
    [SerializeField] private Image itemImageObject;
    [SerializeField] private TextMeshProUGUI itemLabelObject;

    [Header("Config")]
    [SerializeField] private Sprite defaultSprite;
    [SerializeField] private string defaultLabel = "Item";

    private Button _button;
    private ColorBlock _originalColors;
    private bool _isSelected = false;

    private void Awake()
    {
        _button = GetComponent<Button>();
        
        // Let Unity's built-in system handle hover/click colors automatically
        _button.transition = Selectable.Transition.ColorTint;
        
        // Store original colors from the prefab
        _originalColors = _button.colors;
    }

    private void Start()
    {
        // Apply defaults so the prefab looks correct on spawn
        if (itemImageObject != null && itemImageObject.sprite == null)
            SetItem(defaultSprite, defaultLabel);
    }

    private void OnValidate()
    {
        // Update visuals when editing in Inspector
        if (itemImageObject != null && itemImage != null)
            itemImageObject.sprite = itemImage;
        if (itemLabelObject != null && itemLabel != null)
            itemLabelObject.text = itemLabel;
    }

    /// <summary>
    /// Set the button's displayed sprite and label text at runtime.
    /// </summary>
    public void SetItem(Sprite sprite, string label)
    {
        if (itemImageObject != null)
        {
            itemImageObject.sprite = sprite;
            itemImageObject.enabled = sprite != null;
        }

        if (itemLabelObject != null)
            itemLabelObject.text = label;
    }

    public void SetObstacle(ObstacleSO obstacleData)
    {
        obstacle = obstacleData;
        if (obstacleData != null)
        {
            SetItem(obstacleData.icon, obstacleData.obstacleName);
        }
    }

    public void OnClicked()
    {
        if (ObstaclePlacementManager.Instance != null && obstacle != null)
        {
            ObstaclePlacementManager.Instance.SelectObstacle(obstacle);
        }
        
        // Optional: Add visual feedback for selection
        SetSelected(true);
    }

    /// <summary>
    /// Set this button as selected (changes color to selected color)
    /// Call this from the manager when an obstacle is selected
    /// </summary>
    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        
        if (selected)
        {
            // Override the button's color to show selection
            // Create a modified ColorBlock with the selected color as normal
            ColorBlock selectedColors = _originalColors;
            selectedColors.normalColor = _originalColors.selectedColor;
            _button.colors = selectedColors;
        }
        else
        {
            // Restore original colors
            _button.colors = _originalColors;
        }
    }

    /// <summary>
    /// Reset selection state (call when a different obstacle is selected)
    /// </summary>
    public void ResetSelection()
    {
        if (_isSelected)
        {
            _isSelected = false;
            _button.colors = _originalColors;
        }
    }
}
