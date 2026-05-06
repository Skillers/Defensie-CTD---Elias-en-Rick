using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class ObstaclePlacementManager : MonoBehaviour
{
    public static ObstaclePlacementManager Instance { get; private set; }

    [SerializeField] private Camera placementCamera;
    [SerializeField] private LayerMask terrainLayer;
    [SerializeField] private Material previewMaterial;

    private ObstacleSO _selected;
    private List<PlacedObstacle> _placedObstacles = new List<PlacedObstacle>();
    private List<PlacedObstacle> _selectedObstacles = new List<PlacedObstacle>();
    private Vector2 _dragStart;
    private bool _isDragging;
    private GameObject _previewObject;
    private GameObject _linePreviewObject;
    private Vector3 _lineDragStartWorld;
    private bool _isDraggingLine;

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
        ClearPreview();
        _selected = obstacle;
    }

    public void Deselect()
    {
        ClearPreview();
        _selected = null;
    }

    public void ClearWorldSelection()
    {
        foreach (var p in _selectedObstacles)
            if (p != null) p.SetSelected(false);
        _selectedObstacles.Clear();
    }

    private void UpdatePreview(Vector3 worldPos)
    {
        if (_previewObject == null)
        {
            _previewObject = Instantiate(_selected.prefab);
            _previewObject.name = "Preview";
            var col = _previewObject.GetComponentInChildren<Collider>();
            if (col != null) col.enabled = false;
            var r = _previewObject.GetComponentInChildren<Renderer>();
            if (r != null) r.material = previewMaterial;
        }
        _previewObject.SetActive(true);
        _previewObject.transform.position = worldPos;
    }

    private void ClearPreview()
    {
        Destroy(_previewObject);
        _previewObject = null;
        Destroy(_linePreviewObject);
        _linePreviewObject = null;
    }

    private void UpdateLinePreview(Vector3 start, Vector3 end)
    {
        if (_linePreviewObject == null)
        {
            _linePreviewObject = new GameObject("LinePreview");
            _linePreviewObject.AddComponent<MeshFilter>().mesh = Resources.GetBuiltinResource<Mesh>("Cylinder.fbx");
            var mr = _linePreviewObject.AddComponent<MeshRenderer>();
            mr.material = previewMaterial;
        }
        float distance = Vector3.Distance(start, end);
        _linePreviewObject.SetActive(distance > 0.5f);
        if (distance > 0.5f)
        {
            _linePreviewObject.transform.position = (start + end) * 0.5f;
            _linePreviewObject.transform.localScale = new Vector3(0.3f, distance / 2f, 0.3f);
            _linePreviewObject.transform.rotation = Quaternion.FromToRotation(Vector3.up, (end - start).normalized);
        }
    }

    private void Update()
    {
        if (_selected != null)
        {
            Ray ray = placementCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            bool didHit = Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, terrainLayer);

            if (didHit)
                UpdatePreview(hit.point);
            else if (_previewObject != null)
                _previewObject.SetActive(false);

            if (_selected.placementType == PlacementType.Point)
            {
                if (didHit && Mouse.current.leftButton.wasPressedThisFrame)
                    PlaceObstacle(hit.point);
            }
            else if (_selected.placementType == PlacementType.Line)
            {
                if (Mouse.current.leftButton.wasPressedThisFrame && didHit)
                {
                    _lineDragStartWorld = hit.point;
                    _isDraggingLine = true;
                }

                if (_isDraggingLine && Mouse.current.leftButton.isPressed && didHit)
                    UpdateLinePreview(_lineDragStartWorld, hit.point);

                if (_isDraggingLine && Mouse.current.leftButton.wasReleasedThisFrame)
                {
                    if (didHit && Vector3.Distance(_lineDragStartWorld, hit.point) > 0.5f)
                        PlaceLine(_lineDragStartWorld, hit.point);
                    _isDraggingLine = false;
                    Destroy(_linePreviewObject);
                    _linePreviewObject = null;
                }
            }
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
                _isDragging = true;
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

    private void PlaceLine(Vector3 start, Vector3 end)
    {
        float distance = Vector3.Distance(start, end);
        var go = Instantiate(_selected.prefab, (start + end) * 0.5f,
            Quaternion.FromToRotation(Vector3.up, (end - start).normalized));
        go.transform.localScale = new Vector3(0.3f, distance / 2f, 0.3f);
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
