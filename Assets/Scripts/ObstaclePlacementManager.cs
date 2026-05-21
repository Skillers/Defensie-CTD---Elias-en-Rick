using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public class ObstaclePlacementManager : MonoBehaviour
{
    public static ObstaclePlacementManager Instance { get; private set; }

    [Header("References & Layers")]
    [SerializeField]
    private Camera placementCamera;

    [SerializeField]
    private LayerMask terrainLayer;

    [Header("Materials")]
    [SerializeField]
    private Material previewMaterial;

    [SerializeField]
    private Material blockedPreviewMaterial;

    [Header("Collision & Tolerance")]
    [Tooltip("Allows slight overlap before blocking. 0.8 means the check box is 80% of the mesh/collider size.")]
    [SerializeField]
    [Range(0.1f, 1.0f)]
    private float collisionToleranceFactor = 0.8f;

    [Header("Manual Rotation")]
    [Tooltip("Rotation speed in degrees per second while holding the R key.")]
    [SerializeField]
    private float rotationSpeed = 90f;

    private ObstacleSO _selected;

    private List<PlacedObstacle> _placedObstacles = new List<PlacedObstacle>();

    private List<PlacedObstacle> _selectedObstacles = new List<PlacedObstacle>();

    private Vector2 _dragStart;

    private bool _isDragging;

    private GameObject _previewObject;

    private Vector3 _lineDragStartWorld;

    private bool _isDraggingLine;

    private bool _isPreviewColliding;

    private float _manualRotationAngle;

    private const float MinLineLength = 2f;

    // Pre-allocated non-alloc buffers and caches to maintain zero GC allocations in Update
    private readonly Collider[] _overlapBuffer = new Collider[16];

    private readonly List<BoxCollider> _boxColliderCache = new List<BoxCollider>();

    private readonly List<MeshFilter> _meshFilterCache = new List<MeshFilter>();

    private readonly List<Renderer> _rendererCache = new List<Renderer>();

    private readonly List<Collider> _colliderCache = new List<Collider>();

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
        _manualRotationAngle = 0f; // Reset rotation on new selection
    }

    public void Deselect()
    {
        ClearPreview();
        _selected = null;
    }

    public void ClearWorldSelection()
    {
        foreach (var p in _selectedObstacles)
            if (p != null)
                p.SetSelected(false);
        _selectedObstacles.Clear();
    }

    private void PreparePreviewChildren(int requiredCount)
    {
        if (_previewObject == null)
        {
            _previewObject = new GameObject("Preview");
        }

        int currentCount = _previewObject.transform.childCount;

        for (int i = requiredCount; i < currentCount; i++)
        {
            _previewObject.transform.GetChild(i).gameObject.SetActive(false);
        }

        for (int i = 0; i < requiredCount; i++)
        {
            GameObject child;
            if (i < currentCount)
            {
                child = _previewObject.transform.GetChild(i).gameObject;
                child.SetActive(true);
            }
            else
            {
                child = Instantiate(_selected.prefab, _previewObject.transform);
                DisableCollidersAndApplyPreviewMaterial(child);
            }
        }
    }

    private void UpdatePreview(Vector3 worldPos)
    {
        PreparePreviewChildren(1);
        _previewObject.SetActive(true);

        if (_selected.placementType == PlacementType.Point)
        {
            var child = _previewObject.transform.GetChild(0);
            child.position = worldPos;

            Ray ray = new Ray(worldPos + Vector3.up * 100f, Vector3.down);
            Quaternion yawRotation = Quaternion.Euler(0, _manualRotationAngle, 0) * _selected.prefab.transform.rotation;
            Quaternion slopeRotation = yawRotation;
            if (Physics.Raycast(ray, out RaycastHit hit, 200f, terrainLayer))
            {
                slopeRotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * yawRotation;
            }

            child.rotation = slopeRotation;
            child.localScale = _selected.prefab.transform.localScale;

            ConformChildrenToTerrain(child.gameObject, _selected.prefab);
        }
    }

    private void UpdatePreviewLine(Vector3 start, Vector3 end)
    {
        float distance = Vector3.Distance(start, end);
        if (distance < MinLineLength)
        {
            PreparePreviewChildren(1);
            var child = _previewObject.transform.GetChild(0);
            child.position = start;
            child.rotation = _selected.prefab.transform.rotation * Quaternion.Euler(0, _manualRotationAngle, 0);
            child.localScale = _selected.prefab.transform.localScale;
            
            ConformChildrenToTerrain(child.gameObject, _selected.prefab);
            return;
        }

        float naturalLength = GetPrefabLength(_selected.prefab);
        int segmentCount = Mathf.CeilToInt(distance / (2f * naturalLength));
        float segmentLength = distance / segmentCount;
        float scaleX = segmentLength / naturalLength;

        PreparePreviewChildren(segmentCount);

        for (int i = 0; i < segmentCount; i++)
        {
            float tStart = (float)i / segmentCount;
            float tEnd = (float)(i + 1) / segmentCount;

            Vector3 hStart = Vector3.Lerp(start, end, tStart);
            Vector3 hEnd = Vector3.Lerp(start, end, tEnd);

            Vector3 terrainStart = GetTerrainPoint(hStart);
            Vector3 terrainEnd = GetTerrainPoint(hEnd);

            Vector3 center = (terrainStart + terrainEnd) * 0.5f;
            Quaternion rotation = GetSegmentRotation(terrainStart, terrainEnd);

            var child = _previewObject.transform.GetChild(i);
            child.position = center;
            child.rotation = rotation;
            child.localScale = new Vector3(scaleX, 1f, 1f);

            ConformChildrenToTerrain(child.gameObject, _selected.prefab);
        }
    }

    private float GetLowestWorldYOfBounds(Transform target)
    {
        var meshFilter = target.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = target.GetComponentInChildren<MeshFilter>();
        }

        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            var bounds = meshFilter.sharedMesh.bounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;

            Vector3[] localCorners = new Vector3[8]
            {
                center + new Vector3(-extents.x, -extents.y, -extents.z),
                center + new Vector3(extents.x, -extents.y, -extents.z),
                center + new Vector3(-extents.x, extents.y, -extents.z),
                center + new Vector3(extents.x, extents.y, -extents.z),
                center + new Vector3(-extents.x, -extents.y, extents.z),
                center + new Vector3(extents.x, -extents.y, extents.z),
                center + new Vector3(-extents.x, extents.y, extents.z),
                center + new Vector3(extents.x, extents.y, extents.z)
            };

            float minWorldY = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 worldCorner = meshFilter.transform.TransformPoint(localCorners[i]);
                if (worldCorner.y < minWorldY)
                {
                    minWorldY = worldCorner.y;
                }
            }

            return minWorldY;
        }

        return target.position.y;
    }

    private void ConformChildrenToTerrain(GameObject parent, GameObject prefabRef)
    {
        int childCount = parent.transform.childCount;

        if (childCount == 0) return;

        _colliderCache.Clear();
        parent.GetComponentsInChildren<Collider>(true, _colliderCache);
        var disabledColliders = new List<Collider>();
        foreach (var col in _colliderCache)
        {
            if (col != null && col.enabled)
            {
                col.enabled = false;
                disabledColliders.Add(col);
            }
        }

        _colliderCache.Clear();

        try
        {
            Vector3 forwardOnPlane = Vector3.ProjectOnPlane(parent.transform.forward, Vector3.up).normalized;
            if (forwardOnPlane == Vector3.zero)
            {
                forwardOnPlane = parent.transform.forward;
            }

            Quaternion parentYaw = Quaternion.LookRotation(forwardOnPlane, Vector3.up);

            for (int i = 0; i < childCount; i++)
            {
                Transform child = parent.transform.GetChild(i);

                if (prefabRef != null && i < prefabRef.transform.childCount)
                {
                    child.localPosition = prefabRef.transform.GetChild(i).localPosition;
                    child.localRotation = prefabRef.transform.GetChild(i).localRotation;
                }

                Vector3 worldPos = child.position;
                Ray ray = new Ray(new Vector3(worldPos.x, worldPos.y + 100f, worldPos.z), Vector3.down);
                if (Physics.Raycast(ray, out RaycastHit hit, 200f, terrainLayer))
                {
                    Quaternion defaultWorldRotation = parentYaw *
                                                      (prefabRef != null && i < prefabRef.transform.childCount
                                                          ? prefabRef.transform.GetChild(i).localRotation
                                                          : child.localRotation);

                    child.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * defaultWorldRotation;
                    child.position = hit.point;

                    float lowestWorldY = GetLowestWorldYOfBounds(child);
                    float liftOffset = hit.point.y - lowestWorldY;
                    child.position = hit.point + Vector3.up * liftOffset;
                }
            }
        }
        finally
        {
            foreach (var col in disabledColliders)
            {
                if (col != null) col.enabled = true;
            }
        }
    }

    private void DisableCollidersAndApplyPreviewMaterial(GameObject go)
    {
        _colliderCache.Clear();
        go.GetComponentsInChildren<Collider>(true, _colliderCache);
        for (int i = 0; i < _colliderCache.Count; i++)
        {
            _colliderCache[i].enabled = false;
        }

        _colliderCache.Clear();

        _rendererCache.Clear();
        go.GetComponentsInChildren<Renderer>(true, _rendererCache);
        for (int i = 0; i < _rendererCache.Count; i++)
        {
            _rendererCache[i].sharedMaterial = previewMaterial;
        }

        _rendererCache.Clear();
    }

    private void UpdatePreviewMaterials(Material mat)
    {
        if (_previewObject == null) return;

        _rendererCache.Clear();
        _previewObject.GetComponentsInChildren<Renderer>(true, _rendererCache);
        for (int i = 0; i < _rendererCache.Count; i++)
        {
            Renderer r = _rendererCache[i];
            if (r.sharedMaterial != mat)
            {
                r.sharedMaterial = mat;
            }
        }

        _rendererCache.Clear();
    }

    private float GetPrefabLength(GameObject prefab)
    {
        var meshFilter = prefab.GetComponentInChildren<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            return meshFilter.sharedMesh.bounds.size.x;
        }

        return 1f;
    }

    private Vector3 GetTerrainPoint(Vector3 worldPos)
    {
        Ray ray = new Ray(new Vector3(worldPos.x, worldPos.y + 100f, worldPos.z), Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, 200f, terrainLayer))
        {
            return hit.point;
        }

        return worldPos;
    }

    private Quaternion GetSegmentRotation(Vector3 start, Vector3 end)
    {
        Vector3 direction = (end - start).normalized;

        if (direction == Vector3.zero) return Quaternion.identity;

        Vector3 center = (start + end) * 0.5f;
        Vector3 up = Vector3.up;

        Ray ray = new Ray(center + Vector3.up * 50f, Vector3.down);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, terrainLayer))
        {
            up = hit.normal;
        }

        Vector3 localZ = Vector3.Cross(direction, up).normalized;
        if (localZ == Vector3.zero) localZ = Vector3.forward;

        Vector3 localY = Vector3.Cross(localZ, direction).normalized;

        return Quaternion.LookRotation(localZ, localY);
    }

    private void ClearPreview()
    {
        if (_previewObject != null)
        {
            Destroy(_previewObject);
            _previewObject = null;
        }

        _isPreviewColliding = false;
    }

    private void Update()
    {
        if (_selected == null && Mouse.current.leftButton.wasReleasedThisFrame)
        {
            if (_isDragging)
                SelectObstaclesInRect(_dragStart, Mouse.current.position.ReadValue());
            _isDragging = false;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            return;

        if (Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Deselect();
            ClearWorldSelection();
        }

        // Handle smooth manual rotation while the "R" key is held down
        if (Keyboard.current.rKey.isPressed)
        {
            float angleDelta = rotationSpeed * Time.deltaTime;
            if (_selected != null)
            {
                // Allow rotation for point placements, or line placements when not actively dragging
                if (_selected.placementType == PlacementType.Point || 
                    (_selected.placementType == PlacementType.Line && !_isDraggingLine))
                {
                    _manualRotationAngle = (_manualRotationAngle + angleDelta) % 360f;
                }
            }
            else if (_selected == null && _selectedObstacles.Count > 0)
            {
                foreach (var placed in _selectedObstacles)
                {
                    if (placed != null)
                    {
                        RotatePlacedObstacle(placed, angleDelta);
                    }
                }
            }
        }

        if (_selected != null)
        {
            Ray ray = placementCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
            bool didHit = Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, terrainLayer);

            if (didHit)
            {
                if (_selected.placementType != PlacementType.Line)
                {
                    UpdatePreview(hit.point);
                }
            }
            else if (_previewObject != null && _selected.placementType != PlacementType.Line)
            {
                _previewObject.SetActive(false);
            }

            if (_selected.placementType == PlacementType.Point)
            {
                _isPreviewColliding = CheckPreviewCollision();
                UpdatePreviewMaterials(_isPreviewColliding ? blockedPreviewMaterial : previewMaterial);

                if (didHit && Mouse.current.leftButton.wasPressedThisFrame)
                {
                    if (!_isPreviewColliding)
                        PlaceObstacle(hit.point);
                }
            }
            else if (_selected.placementType == PlacementType.Line)
            {
                if (_previewObject == null)
                {
                    _previewObject = new GameObject("Preview");
                }

                if (_isDraggingLine && didHit)
                {
                    UpdatePreviewLine(_lineDragStartWorld, hit.point);
                }
                else if (!_isDraggingLine && didHit)
                {
                    PreparePreviewChildren(1);
                    var child = _previewObject.transform.GetChild(0);
                    child.position = hit.point;
                    child.rotation = _selected.prefab.transform.rotation * Quaternion.Euler(0, _manualRotationAngle, 0);
                    child.localScale = _selected.prefab.transform.localScale;
                    
                    ConformChildrenToTerrain(child.gameObject, _selected.prefab);
                }

                _isPreviewColliding = CheckPreviewCollision();
                UpdatePreviewMaterials(_isPreviewColliding ? blockedPreviewMaterial : previewMaterial);

                if (Mouse.current.leftButton.wasPressedThisFrame && didHit)
                {
                    if (!_isPreviewColliding)
                    {
                        _lineDragStartWorld = hit.point;
                        _isDraggingLine = true;
                    }
                }

                if (_isDraggingLine && Mouse.current.leftButton.wasReleasedThisFrame)
                {
                    if (didHit && !_isPreviewColliding)
                        PlaceLine(_lineDragStartWorld, hit.point);
                    _isDraggingLine = false;
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

            Ray clickRay = placementCamera.ScreenPointToRay(mousePos);
            if (Physics.Raycast(clickRay, out RaycastHit clickHit, Mathf.Infinity))
            {
                var obstacle = clickHit.collider.GetComponentInParent<PlacedObstacle>();
                if (obstacle != null)
                {
                    ClearWorldSelection();
                    _selectedObstacles.Add(obstacle);
                    obstacle.SetSelected(true);
                }
                else
                {
                    ClearWorldSelection();
                }
            }
        }

        if (mouse.leftButton.isPressed)
        {
            if (!_isDragging && Vector2.Distance(mousePos, _dragStart) > 10f)
                _isDragging = true;
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

    private void RotatePlacedObstacle(PlacedObstacle placed, float angle)
    {
        if (placed.obstacleSo.placementType == PlacementType.Line)
        {
            // Rotate the group root around world Vector3.up at its current pivot
            placed.transform.Rotate(Vector3.up, angle, Space.World);

            // Re-conform each child segment of the group
            int segmentCount = placed.transform.childCount;
            for (int i = 0; i < segmentCount; i++)
            {
                Transform segment = placed.transform.GetChild(i);
                Vector3 terrainPos = GetTerrainPoint(segment.position);
                segment.position = terrainPos;

                Ray ray = new Ray(terrainPos + Vector3.up * 50f, Vector3.down);
                if (Physics.Raycast(ray, out RaycastHit hit, 100f, terrainLayer))
                {
                    Vector3 segForward = Vector3.ProjectOnPlane(segment.forward, Vector3.up).normalized;
                    if (segForward != Vector3.zero)
                    {
                        Quaternion segYaw = Quaternion.LookRotation(segForward, Vector3.up);
                        segment.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * segYaw;
                    }
                }

                ConformChildrenToTerrain(segment.gameObject, placed.obstacleSo.prefab);
            }
        }
        else
        {
            // Rotate point obstacle around world Vector3.up at its pivot
            placed.transform.Rotate(Vector3.up, angle, Space.World);

            Vector3 terrainPos = GetTerrainPoint(placed.transform.position);
            placed.transform.position = terrainPos;

            Ray ray = new Ray(terrainPos + Vector3.up * 50f, Vector3.down);
            if (Physics.Raycast(ray, out RaycastHit hit, 100f, terrainLayer))
            {
                Vector3 forward = Vector3.ProjectOnPlane(placed.transform.forward, Vector3.up).normalized;
                if (forward == Vector3.zero) forward = placed.transform.forward;
                Quaternion yaw = Quaternion.LookRotation(forward, Vector3.up);
                placed.transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * yaw;
            }

            ConformChildrenToTerrain(placed.gameObject, placed.obstacleSo.prefab);
        }
    }

    private void PlaceObstacle(Vector3 position)
    {
        Ray ray = new Ray(position + Vector3.up * 100f, Vector3.down);
        Quaternion yawRotation = Quaternion.Euler(0, _manualRotationAngle, 0) * _selected.prefab.transform.rotation;
        Quaternion slopeRotation = yawRotation;
        if (Physics.Raycast(ray, out RaycastHit hit, 200f, terrainLayer))
        {
            slopeRotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * yawRotation;
        }

        var go = Instantiate(_selected.prefab, position, slopeRotation);

        ConformChildrenToTerrain(go, _selected.prefab);

        var placed = go.AddComponent<PlacedObstacle>();
        placed.obstacleSo = _selected;
        _placedObstacles.Add(placed);
    }

    private void PlaceLine(Vector3 start, Vector3 end)
    {
        float distance = Vector3.Distance(start, end);

        GameObject rootGo = new GameObject(_selected.obstacleName + "_PlacedGroup");
        rootGo.transform.position = start;

        var placedObstacle = rootGo.AddComponent<PlacedObstacle>();
        placedObstacle.obstacleSo = _selected;

        if (distance < MinLineLength)
        {
            var singleGo = Instantiate(_selected.prefab, start, _selected.prefab.transform.rotation * Quaternion.Euler(0, _manualRotationAngle, 0), rootGo.transform);
            ConformChildrenToTerrain(singleGo, _selected.prefab);
            placedObstacle.Initialize();
            _placedObstacles.Add(placedObstacle);
            return;
        }

        float naturalLength = GetPrefabLength(_selected.prefab);
        int segmentCount = Mathf.CeilToInt(distance / (2f * naturalLength));
        float segmentLength = distance / segmentCount;
        float scaleX = segmentLength / naturalLength;

        for (int i = 0; i < segmentCount; i++)
        {
            float tStart = (float)i / segmentCount;
            float tEnd = (float)(i + 1) / segmentCount;

            Vector3 hStart = Vector3.Lerp(start, end, tStart);
            Vector3 hEnd = Vector3.Lerp(start, end, tEnd);

            Vector3 terrainStart = GetTerrainPoint(hStart);
            Vector3 terrainEnd = GetTerrainPoint(hEnd);

            Vector3 center = (terrainStart + terrainEnd) * 0.5f;
            Quaternion rotation = GetSegmentRotation(terrainStart, terrainEnd);

            var segmentGo = Instantiate(_selected.prefab, center, rotation, rootGo.transform);
            segmentGo.transform.localScale = new Vector3(scaleX, 1f, 1f);

            ConformChildrenToTerrain(segmentGo, _selected.prefab);
        }

        placedObstacle.Initialize();
        _placedObstacles.Add(placedObstacle);
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

    #region Collision Detection Helpers (Zero-Allocation)

    private bool CheckPreviewCollision()
    {
        if (_previewObject == null || !_previewObject.activeSelf)
            return false;

        int childCount = _previewObject.transform.childCount;
        for (int i = 0; i < childCount; i++)
        {
            Transform child = _previewObject.transform.GetChild(i);

            if (!child.gameObject.activeSelf) continue;

            if (DoesHierarchyCollide(child))
            {
                return true;
            }
        }

        return false;
    }

    private Bounds TransformBounds(Transform source, Transform target, Bounds sourceBounds)
    {
        Vector3 center = sourceBounds.center;
        Vector3 extents = sourceBounds.extents;

        Vector3 c0 = source.TransformPoint(center + new Vector3(-extents.x, -extents.y, -extents.z));
        Vector3 c1 = source.TransformPoint(center + new Vector3(extents.x, -extents.y, -extents.z));
        Vector3 c2 = source.TransformPoint(center + new Vector3(-extents.x, extents.y, -extents.z));
        Vector3 c3 = source.TransformPoint(center + new Vector3(extents.x, extents.y, -extents.z));
        Vector3 c4 = source.TransformPoint(center + new Vector3(-extents.x, -extents.y, extents.z));
        Vector3 c5 = source.TransformPoint(center + new Vector3(extents.x, -extents.y, extents.z));
        Vector3 c6 = source.TransformPoint(center + new Vector3(-extents.x, extents.y, extents.z));
        Vector3 c7 = source.TransformPoint(center + new Vector3(extents.x, extents.y, extents.z));

        Bounds b = new Bounds(target.InverseTransformPoint(c0), Vector3.zero);
        b.Encapsulate(target.InverseTransformPoint(c1));
        b.Encapsulate(target.InverseTransformPoint(c2));
        b.Encapsulate(target.InverseTransformPoint(c3));
        b.Encapsulate(target.InverseTransformPoint(c4));
        b.Encapsulate(target.InverseTransformPoint(c5));
        b.Encapsulate(target.InverseTransformPoint(c6));
        b.Encapsulate(target.InverseTransformPoint(c7));

        return b;
    }

    private bool DoesHierarchyCollide(Transform t)
    {
        // 1. Check if the parent/root already has a manual collective BoxCollider
        BoxCollider manualRootCollider = t.GetComponent<BoxCollider>();
        if (manualRootCollider != null)
        {
            return CheckBoxCollision(t, manualRootCollider.center, manualRootCollider.size);
        }

        // 2. Fallback: Dynamically calculate consolidated bounds if no manual collider is on the root
        Bounds combinedLocalBounds = new Bounds();
        bool boundsInitialized = false;

        _meshFilterCache.Clear();
        t.GetComponentsInChildren<MeshFilter>(true, _meshFilterCache);

        for (int i = 0; i < _meshFilterCache.Count; i++)
        {
            MeshFilter mf = _meshFilterCache[i];

            if (mf.sharedMesh == null) continue;

            Bounds boundsInParentSpace = TransformBounds(mf.transform, t, mf.sharedMesh.bounds);

            if (!boundsInitialized)
            {
                combinedLocalBounds = boundsInParentSpace;
                boundsInitialized = true;
            }
            else
            {
                combinedLocalBounds.Encapsulate(boundsInParentSpace);
            }
        }

        _meshFilterCache.Clear();

        if (!boundsInitialized)
        {
            _boxColliderCache.Clear();
            t.GetComponentsInChildren<BoxCollider>(true, _boxColliderCache);

            for (int i = 0; i < _boxColliderCache.Count; i++)
            {
                BoxCollider box = _boxColliderCache[i];
                Bounds boundsInParentSpace = TransformBounds(box.transform, t, new Bounds(box.center, box.size));

                if (!boundsInitialized)
                {
                    combinedLocalBounds = boundsInParentSpace;
                    boundsInitialized = true;
                }
                else
                {
                    combinedLocalBounds.Encapsulate(boundsInParentSpace);
                }
            }

            _boxColliderCache.Clear();
        }

        if (boundsInitialized)
        {
            return CheckBoxCollision(t, combinedLocalBounds.center, combinedLocalBounds.size);
        }

        return false;
    }

    private bool CheckBoxCollision(Transform sourceTransform, Vector3 localCenter, Vector3 localSize)
    {
        Vector3 worldScale = sourceTransform.lossyScale;
        Vector3 scaledSize = new Vector3(
            localSize.x * Mathf.Abs(worldScale.x),
            localSize.y * Mathf.Abs(worldScale.y),
            localSize.z * Mathf.Abs(worldScale.z)
        );

        // Apply our generous tolerance factor to allow slight physical overlaps before blocking
        Vector3 halfExtents = (scaledSize * collisionToleranceFactor) * 0.5f;
        Vector3 worldCenter = sourceTransform.TransformPoint(localCenter);
        Quaternion worldRotation = sourceTransform.rotation;

        int numHits = Physics.OverlapBoxNonAlloc(
            worldCenter,
            halfExtents,
            _overlapBuffer,
            worldRotation,
            Physics.AllLayers,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < numHits; i++)
        {
            Collider hitCol = _overlapBuffer[i];

            if (hitCol == null) continue;

            // Only trigger collision if the object hit belongs to an already placed obstacle.
            var placed = hitCol.GetComponentInParent<PlacedObstacle>();
            if (placed != null)
            {
                return true;
            }
        }

        return false;
    }

    #endregion
}