using UnityEngine;
using UnityEngine.InputSystem;

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

    public void Deselect()
    {
        _selected = null;
    }

    private void Update()
    {
        if (_selected == null) return;
        if (!Mouse.current.leftButton.wasPressedThisFrame) return;

        Ray ray = placementCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        bool didHit = Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, terrainLayer);
        if (didHit)
        {
            PlaceObstacle(hit.point);
        }
    }

    private void PlaceObstacle(Vector3 position)
    {
        Instantiate(_selected.prefab, position, Quaternion.identity);
    }
}
