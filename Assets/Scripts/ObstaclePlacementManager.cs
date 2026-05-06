using UnityEngine;

public class ObstaclePlacementManager : MonoBehaviour
{
    public static ObstaclePlacementManager Instance { get; private set; }

    [SerializeField] private Camera placementCamera;
    [SerializeField] private LayerMask terrainLayer;

    private ObstacleSO _selected;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void SelectObstacle(ObstacleSO obstacle)
    {
        _selected = obstacle;
    }

    private void Update()
    {
        if (_selected == null) return;
        if (!Input.GetMouseButtonDown(0)) return;

        Ray ray = placementCamera.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, terrainLayer))
        {
            PlaceObstacle(hit.point);
        }
    }

    private void PlaceObstacle(Vector3 position)
    {
        Instantiate(_selected.prefab, position, Quaternion.identity);
    }
}
