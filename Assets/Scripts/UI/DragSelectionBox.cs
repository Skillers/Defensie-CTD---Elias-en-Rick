using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class DragSelectionBox : MonoBehaviour
{
    [SerializeField] private RectTransform selectionBoxRect;

    private Image _image;

    private void Awake()
    {
        _image = selectionBoxRect.GetComponent<Image>();
    }

    private void Update()
    {
        bool isDragging = ObstaclePlacementManager.Instance.IsDragging;

        if (!isDragging)
        {
            SetImageAlpha(0);
            return;
        }

        SetImageAlpha(70f / 255f);

        Vector2 start = ObstaclePlacementManager.Instance.DragStart;
        Vector2 current = Mouse.current.position.ReadValue();

        float minX = Mathf.Min(start.x, current.x) / Screen.width;
        float minY = Mathf.Min(start.y, current.y) / Screen.height;
        float maxX = Mathf.Max(start.x, current.x) / Screen.width;
        float maxY = Mathf.Max(start.y, current.y) / Screen.height;

        selectionBoxRect.anchorMin = new Vector2(minX, minY);
        selectionBoxRect.anchorMax = new Vector2(maxX, maxY);
        selectionBoxRect.offsetMin = Vector2.zero;
        selectionBoxRect.offsetMax = Vector2.zero;
    }

    private void SetImageAlpha(float alpha)
    {
        var c = _image.color;
        c.a = alpha;
        _image.color = c;
        _image.raycastTarget = alpha > 0;
    }
}
