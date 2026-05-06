using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class ObstacleInventoryUI : MonoBehaviour
{
    [SerializeField] private ObstacleInventorySO inventory;
    [SerializeField] private GameObject buttonPrefab;
    [SerializeField] private Transform buttonContainer;

    private PlacableObstacleButton _currentSelected;

    private void ClearSelection()
    {
        ObstaclePlacementManager.Instance.Deselect();
        _currentSelected?.SetSelected(false);
        _currentSelected = null;
    }

    private void Start()
    {
        for (int i = buttonContainer.childCount - 1; i >= 0; i--)
            Destroy(buttonContainer.GetChild(i).gameObject);

        foreach (ObstacleSO obstacle in inventory.obstacles)
        {
            GameObject go = Instantiate(buttonPrefab, buttonContainer);
            var obstacleButton = go.GetComponent<PlacableObstacleButton>();
            obstacleButton.SetItem(obstacle.icon, obstacle.obstacleName);
            obstacleButton.SetObstacle(obstacle);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(() =>
            {
                if (_currentSelected == obstacleButton)
                {
                    ClearSelection();
                    return;
                }
                _currentSelected?.SetSelected(false);
                _currentSelected = obstacleButton;
                _currentSelected.SetSelected(true);
                obstacleButton.OnClicked();
            });
        }
    }

    private void Update()
    {
        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ClearSelection();
        }
    }
}
