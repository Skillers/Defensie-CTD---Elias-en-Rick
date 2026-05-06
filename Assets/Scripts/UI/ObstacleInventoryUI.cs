using UnityEngine;
using UnityEngine.UI;

public class ObstacleInventoryUI : MonoBehaviour
{
    [SerializeField] private ObstacleInventorySO inventory;
    [SerializeField] private GameObject buttonPrefab;
    [SerializeField] private Transform buttonContainer;

    private void Start()
    {
        for (int i = buttonContainer.childCount - 1; i >= 0; i--)
            Destroy(buttonContainer.GetChild(i).gameObject);

        foreach (ObstacleSO obstacle in inventory.obstacles)
        {
            var go = Instantiate(buttonPrefab, buttonContainer);
            var obstacleButton = go.GetComponent<PlacableObstacleButton>();
            obstacleButton.SetItem(obstacle.icon, obstacle.obstacleName);
            obstacleButton.SetObstacle(obstacle);
            var button = go.GetComponent<Button>();
            button.onClick.AddListener(() => obstacleButton.OnClicked());
        }
    }
}
