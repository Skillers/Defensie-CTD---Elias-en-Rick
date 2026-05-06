using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class ObstaclePlacementManager : MonoBehaviour
{
    public static ObstaclePlacementManager Instance { get; private set; }

    [SerializeField] private Camera placementCamera;
    [SerializeField] private LayerMask terrainLayer;

    private ObstacleSO _selected;
    private List<PlacedObstacle> _placedObstacles = new List<PlacedObstacle>();
    private List<PlacedObstacle> _selectedObstacles = new List<PlacedObstacle>();
    private Vector2 _dragStart;
    private bool _isDragging;

    public bool IsDragging => _isDragging;
    public Vector2 DragStart => _dragStart;

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

    public void ClearWorldSelection()
    {
        foreach (var p in _selectedObstacles)
            if (p != null) p.SetSelected(false);
        _selectedObstacles.Clear();
    }

    private void Update()
    {
        if (_selected != null)
        {
            if (!Mouse.current.leftButton.wasPressedThisFrame) return;

            Ray ray = placementCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            bool didHit = Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, terrainLayer);
            if (didHit)
                PlaceObstacle(hit.point);
            return;
        }

        var mouse = Mouse.current;
        var mousePos = mouse.position.ReadValue();

        if (mouse.leftButton.wasPressedThisFrame)
        {
            _dragStart = mousePos;
            _isDragging = false;
        }

        if (mouse.leftButton.isPressed)
        {
            if (!_isDragging && Vector2.Distance(mousePos, _dragStart) > 10f)
            {
                _isDragging = true;
            }
        }

        if (mouse.leftButton.wasReleasedThisFrame)
        {
            if (_isDragging)
                SelectObstaclesInRect(_dragStart, mousePos);
            _isDragging = false;
        }

        if (Keyboard.current.deleteKey.wasPressedThisFrame)
        {
            foreach (var p in _selectedObstacles)
            {
                if (p == null) continue;
                _placedObstacles.Remove(p);
                Destroy(p.gameObject);
            }
            _selectedObstacles.Clear();
        }
    }

    private void PlaceObstacle(Vector3 position)
    {
        var go = Instantiate(_selected.prefab, position, Quaternion.identity);
        var placed = go.AddComponent<PlacedObstacle>();
        placed.obstacleSO = _selected;
        _placedObstacles.Add(placed);
    }

    private void SelectObstaclesInRect(Vector2 screenStart, Vector2 screenEnd)
    {
        ClearWorldSelection();
        float minX = Mathf.Min(screenStart.x, screenEnd.x);
        float minY = Mathf.Min(screenStart.y, screenEnd.y);
        float maxX = Mathf.Max(screenStart.x, screenEnd.x);
        float maxY = Mathf.Max(screenStart.y, screenEnd.y);
        var rect = new Rect(minX, minY, maxX - minX, maxY - minY);
        foreach (var placed in _placedObstacles)
        {
            if (placed == null) continue;
            var screenPos = (Vector2)placementCamera.WorldToScreenPoint(placed.transform.position);
            if (rect.Contains(screenPos))
            {
                _selectedObstacles.Add(placed);
                placed.SetSelected(true);
            }
        }
    }
}
