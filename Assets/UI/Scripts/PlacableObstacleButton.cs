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
    // TODO: Fix
    [SerializeField] private Sprite defaultSprite;
    [SerializeField] private string defaultLabel = "Item";

    private void Awake()
    {
        // Apply defaults so the prefab looks correct on spawn
        // without requiring an explicit SetItem() call
        // SetItem(defaultSprite, defaultLabel);
    }

    private void OnValidate()
    {
        if (itemImage != null) itemImageObject.sprite = itemImage;
        if (itemLabel != null) itemLabelObject.text = itemLabel;
    }

    public void Setup(Sprite newSprite, string newLabelText)
    {
        itemImage = newSprite;
        itemLabel = newLabelText;
        OnValidate();
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

    public void OnClicked()
    {
        ObstaclePlacementManager.Instance.SelectObstacle(obstacle);
    }
}
