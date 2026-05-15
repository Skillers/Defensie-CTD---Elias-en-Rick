using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public class SimulationCameraController : MonoBehaviour
{
    [SerializeField] float zoomSpeed = 8f;
    [SerializeField] float zoomSmoothness = 12f;
    [SerializeField] float keyboardPanSpeed = 0.5f;
    [SerializeField] float keyboardYawSpeed = 60f;
    [SerializeField] float yawSpeed = 0.3f;
    [SerializeField] float pitchSpeed = 0.3f;
    [SerializeField] float minAngle = 10f;
    [SerializeField] float maxAngle = 89f;
    [SerializeField] float minY = 0f;
    [SerializeField] float groundClearance = 5f;
    [SerializeField] float startDistance = 200f;
    [SerializeField] float startAngle = 45f;
    [SerializeField] float orbSize = 1.5f;
    [SerializeField] Color orbColor = Color.cyan;
    [SerializeField] Color focalOrbColor = new Color(0.65f, 0.25f, 0.95f, 1f);
    [SerializeField] bool showFocalOrb = true;
    [SerializeField] Color lastTerrainOrbColor = new Color(1f, 0.85f, 0.1f, 1f);
    [SerializeField] bool showLastTerrainOrb = true;
    [SerializeField] Color focalEdgeOrbColor = new Color(0.3f, 0.95f, 0.4f, 1f);
    [SerializeField] bool showFocalEdgeOrb = true;
    [SerializeField] Color yAlignedOrbColor = new Color(1f, 0.5f, 0.1f, 1f);
    [SerializeField] bool showYAlignedOrb = true;
    [SerializeField] bool autoRecenter = true;
    [SerializeField] float recenterBaseSpeed = 3f;
    [SerializeField] float recenterDistanceFactor = 1f;
    [SerializeField] float recenterDeadZone = 0.5f;
    [SerializeField] float yAlignedDistanceFactor = 2f;
    [SerializeField] private TerrainDataStore terrainDataStore;

    private Camera _cam;
    private Vector3 _grabWorld;
    private bool _grabbing;
    private float _pendingZoom;
    private Vector3 _orbitFocal;
    private bool _orbiting;
    private GameObject _orbVisual;
    private GameObject _focalOrb;
    private GameObject _lastTerrainOrb;
    private GameObject _focalEdgeOrb;
    private GameObject _yAlignedOrb;
    private bool _groundFollowing;

    public enum TerrainPointSource { None, LastTerrain, FocalEdge }

    public Vector3 FocalPoint { get; private set; }
    public Vector3 LastTerrainPoint { get; private set; }
    public bool HasLastTerrainPoint { get; private set; }
    public Vector3 FocalEdgePoint { get; private set; }
    public bool HasFocalEdgePoint { get; private set; }
    public Vector3 ClosestTerrainPoint { get; private set; }
    public TerrainPointSource ClosestTerrainSource { get; private set; }
    public Vector3 YAlignedFocalPoint { get; private set; }
    public bool HasYAlignedFocalPoint { get; private set; }

    public bool ShowFocalOrb
    {
        get => showFocalOrb;
        set
        {
            showFocalOrb = value;
            if (_focalOrb != null) _focalOrb.SetActive(value);
        }
    }

    public void ToggleFocalOrb() => ShowFocalOrb = !ShowFocalOrb;

    public bool ShowLastTerrainOrb
    {
        get => showLastTerrainOrb;
        set
        {
            showLastTerrainOrb = value;
            if (_lastTerrainOrb != null) _lastTerrainOrb.SetActive(value && HasLastTerrainPoint);
        }
    }

    public void ToggleLastTerrainOrb() => ShowLastTerrainOrb = !ShowLastTerrainOrb;

    public bool ShowFocalEdgeOrb
    {
        get => showFocalEdgeOrb;
        set
        {
            showFocalEdgeOrb = value;
            if (_focalEdgeOrb != null) _focalEdgeOrb.SetActive(value && HasFocalEdgePoint);
        }
    }

    public void ToggleFocalEdgeOrb() => ShowFocalEdgeOrb = !ShowFocalEdgeOrb;

    public bool ShowYAlignedOrb
    {
        get => showYAlignedOrb;
        set
        {
            showYAlignedOrb = value;
            if (_yAlignedOrb != null) _yAlignedOrb.SetActive(value && HasYAlignedFocalPoint);
        }
    }

    public void ToggleYAlignedOrb() => ShowYAlignedOrb = !ShowYAlignedOrb;

    public bool AutoRecenter { get => autoRecenter; set => autoRecenter = value; }

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        if (terrainDataStore == null)
            terrainDataStore = FindFirstObjectByType<TerrainDataStore>();

        float rad = startAngle * Mathf.Deg2Rad;
        transform.position = new Vector3(0f, Mathf.Sin(rad) * startDistance, -Mathf.Cos(rad) * startDistance);
        transform.rotation = Quaternion.Euler(startAngle, 0f, 0f);

        CreateOrbVisual();
        CreateFocalOrb();
        CreateLastTerrainOrb();
        CreateFocalEdgeOrb();
        CreateYAlignedOrb();
    }

    private void OnDestroy()
    {
        if (_orbVisual != null) Destroy(_orbVisual);
        if (_focalOrb != null) Destroy(_focalOrb);
        if (_lastTerrainOrb != null) Destroy(_lastTerrainOrb);
        if (_focalEdgeOrb != null) Destroy(_focalEdgeOrb);
        if (_yAlignedOrb != null) Destroy(_yAlignedOrb);
    }

    private void CreateOrbVisual()
    {
        _orbVisual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _orbVisual.name = "CameraOrbitFocal";
        var col = _orbVisual.GetComponent<Collider>();
        if (col != null) Destroy(col);
        _orbVisual.transform.localScale = Vector3.one * orbSize;
        var mr = _orbVisual.GetComponent<MeshRenderer>();
        if (mr != null) mr.material.color = orbColor;
        _orbVisual.SetActive(false);
    }

    private void CreateFocalOrb()
    {
        _focalOrb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _focalOrb.name = "CameraScreenFocal";
        var col = _focalOrb.GetComponent<Collider>();
        if (col != null) Destroy(col);
        _focalOrb.transform.localScale = Vector3.one * orbSize;
        var mr = _focalOrb.GetComponent<MeshRenderer>();
        if (mr != null) mr.material.color = focalOrbColor;
        _focalOrb.SetActive(showFocalOrb);
    }

    private void CreateLastTerrainOrb()
    {
        _lastTerrainOrb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _lastTerrainOrb.name = "CameraLastTerrainPoint";
        var col = _lastTerrainOrb.GetComponent<Collider>();
        if (col != null) Destroy(col);
        _lastTerrainOrb.transform.localScale = Vector3.one * orbSize;
        var mr = _lastTerrainOrb.GetComponent<MeshRenderer>();
        if (mr != null) mr.material.color = lastTerrainOrbColor;
        _lastTerrainOrb.SetActive(false);
    }

    private void CreateFocalEdgeOrb()
    {
        _focalEdgeOrb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _focalEdgeOrb.name = "CameraFocalEdgePoint";
        var col = _focalEdgeOrb.GetComponent<Collider>();
        if (col != null) Destroy(col);
        _focalEdgeOrb.transform.localScale = Vector3.one * orbSize;
        var mr = _focalEdgeOrb.GetComponent<MeshRenderer>();
        if (mr != null) mr.material.color = focalEdgeOrbColor;
        _focalEdgeOrb.SetActive(false);
    }

    private void CreateYAlignedOrb()
    {
        _yAlignedOrb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _yAlignedOrb.name = "CameraYAlignedFocalPoint";
        var col = _yAlignedOrb.GetComponent<Collider>();
        if (col != null) Destroy(col);
        _yAlignedOrb.transform.localScale = Vector3.one * orbSize;
        var mr = _yAlignedOrb.GetComponent<MeshRenderer>();
        if (mr != null) mr.material.color = yAlignedOrbColor;
        _yAlignedOrb.SetActive(false);
    }

    private void OnValidate()
    {
        if (_focalOrb != null) _focalOrb.SetActive(showFocalOrb);
        if (_lastTerrainOrb != null) _lastTerrainOrb.SetActive(showLastTerrainOrb && HasLastTerrainPoint);
        if (_focalEdgeOrb != null) _focalEdgeOrb.SetActive(showFocalEdgeOrb && HasFocalEdgePoint);
        if (_yAlignedOrb != null) _yAlignedOrb.SetActive(showYAlignedOrb && HasYAlignedFocalPoint);
    }

    private void UpdateFocalPoint()
    {
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Ray r = _cam.ScreenPointToRay(screenCenter);

        if (terrainDataStore != null && terrainDataStore.RaycastTerrain(r, out Vector3 terrainHit))
        {
            FocalPoint = terrainHit;
            LastTerrainPoint = terrainHit;
            HasLastTerrainPoint = true;
        }
        else if (TryRayToPlane(r, 0f, out Vector3 planeHit))
        {
            FocalPoint = planeHit;
        }

        if (_focalOrb != null) _focalOrb.transform.position = FocalPoint;

        if (_lastTerrainOrb != null && HasLastTerrainPoint)
        {
            _lastTerrainOrb.transform.position = LastTerrainPoint;
            _lastTerrainOrb.SetActive(showLastTerrainOrb);
        }

        UpdateFocalEdgePoint();
        UpdateClosestTerrainPoint();
        UpdateYAlignedFocalPoint();
    }

    private void UpdateYAlignedFocalPoint()
    {
        bool valid = false;

        if (ClosestTerrainSource != TerrainPointSource.None)
        {
            Vector3 origin = transform.position;
            Vector3 dir = transform.forward;
            if (dir.y < -1e-6f)
            {
                float t = (ClosestTerrainPoint.y - origin.y) / dir.y;
                if (t > 0f)
                {
                    YAlignedFocalPoint = origin + dir * t;
                    valid = true;
                }
            }
        }

        HasYAlignedFocalPoint = valid;

        if (_yAlignedOrb == null) return;
        if (valid)
        {
            _yAlignedOrb.transform.position = YAlignedFocalPoint;
            _yAlignedOrb.SetActive(showYAlignedOrb);
        }
        else
        {
            _yAlignedOrb.SetActive(false);
        }
    }

    private void UpdateClosestTerrainPoint()
    {
        if (HasLastTerrainPoint && HasFocalEdgePoint)
        {
            float dLast = SqXZ(LastTerrainPoint, FocalPoint);
            float dEdge = SqXZ(FocalEdgePoint, FocalPoint);
            if (dLast <= dEdge)
            {
                ClosestTerrainPoint = LastTerrainPoint;
                ClosestTerrainSource = TerrainPointSource.LastTerrain;
            }
            else
            {
                ClosestTerrainPoint = FocalEdgePoint;
                ClosestTerrainSource = TerrainPointSource.FocalEdge;
            }
        }
        else if (HasLastTerrainPoint)
        {
            ClosestTerrainPoint = LastTerrainPoint;
            ClosestTerrainSource = TerrainPointSource.LastTerrain;
        }
        else if (HasFocalEdgePoint)
        {
            ClosestTerrainPoint = FocalEdgePoint;
            ClosestTerrainSource = TerrainPointSource.FocalEdge;
        }
        else
        {
            ClosestTerrainSource = TerrainPointSource.None;
        }
    }

    private static float SqXZ(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }

    private void UpdateFocalEdgePoint()
    {
        Vector3 source = HasYAlignedFocalPoint ? YAlignedFocalPoint : FocalPoint;
        if (TryEdgeCrossingTowardCenter(source, out Vector3 edge))
        {
            FocalEdgePoint = edge;
            HasFocalEdgePoint = true;
        }
        else
        {
            HasFocalEdgePoint = false;
        }

        if (_focalEdgeOrb == null) return;

        if (HasFocalEdgePoint)
        {
            _focalEdgeOrb.transform.position = FocalEdgePoint;
            _focalEdgeOrb.SetActive(showFocalEdgeOrb);
        }
        else
        {
            _focalEdgeOrb.SetActive(false);
        }
    }

    private bool TryEdgeCrossingTowardCenter(Vector3 from, out Vector3 hit)
    {
        hit = Vector3.zero;
        if (terrainDataStore == null) return false;

        Vector3 c = terrainDataStore.transform.position;
        float minX = c.x - terrainDataStore.extentX;
        float maxX = c.x + terrainDataStore.extentX;
        float minZ = c.z - terrainDataStore.extentZ;
        float maxZ = c.z + terrainDataStore.extentZ;

        if (from.x >= minX && from.x <= maxX && from.z >= minZ && from.z <= maxZ) return false;

        float dx = c.x - from.x;
        float dz = c.z - from.z;
        if (Mathf.Abs(dx) < 1e-6f && Mathf.Abs(dz) < 1e-6f) return false;

        float bestT = float.MaxValue;
        float bestX = 0f, bestZ = 0f;

        if (Mathf.Abs(dx) > 1e-6f)
        {
            TryAxisEdge(true,  minX, from, dx, dz, minZ, maxZ, ref bestT, ref bestX, ref bestZ);
            TryAxisEdge(true,  maxX, from, dx, dz, minZ, maxZ, ref bestT, ref bestX, ref bestZ);
        }
        if (Mathf.Abs(dz) > 1e-6f)
        {
            TryAxisEdge(false, minZ, from, dx, dz, minX, maxX, ref bestT, ref bestX, ref bestZ);
            TryAxisEdge(false, maxZ, from, dx, dz, minX, maxX, ref bestT, ref bestX, ref bestZ);
        }

        if (bestT == float.MaxValue) return false;

        float terrainY = terrainDataStore.GetRoundedHeight(new Vector3(bestX, 0f, bestZ));
        hit = new Vector3(bestX, terrainY, bestZ);
        return true;
    }

    private void TryAxisEdge(bool xAxis, float edgeCoord, Vector3 from, float dx, float dz, float lateralMin, float lateralMax, ref float bestT, ref float bestX, ref float bestZ)
    {
        float dir = xAxis ? dx : dz;
        float ori = xAxis ? from.x : from.z;
        float t = (edgeCoord - ori) / dir;
        if (t <= 0f || t > 1f || t >= bestT) return;
        float lateral = xAxis ? (from.z + dz * t) : (from.x + dx * t);
        if (lateral < lateralMin || lateral > lateralMax) return;
        bestT = t;
        if (xAxis) { bestX = edgeCoord; bestZ = lateral; }
        else       { bestX = lateral;   bestZ = edgeCoord; }
    }

    private void Update()
    {
        var mouse = Mouse.current;

        if (!mouse.middleButton.isPressed)
        {
            if (mouse.rightButton.wasPressedThisFrame)
            {
                Ray grabRay = _cam.ScreenPointToRay(mouse.position.ReadValue());
                _grabbing = terrainDataStore != null && terrainDataStore.RaycastTerrain(grabRay, out _grabWorld);
                _groundFollowing = false;
            }

            if (mouse.rightButton.isPressed && _grabbing)
            {
                Ray r = _cam.ScreenPointToRay(mouse.position.ReadValue());
                if (TryRayToPlane(r, _grabWorld.y, out Vector3 currentWorld))
                {
                    Vector3 delta = _grabWorld - currentWorld;
                    transform.position += new Vector3(delta.x, 0f, delta.z);
                }
            }
        }

        if (!mouse.rightButton.isPressed)
            _grabbing = false;

        if (!mouse.middleButton.isPressed)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
            {
                if (scroll < 0f) _groundFollowing = false;
                _pendingZoom += scroll * zoomSpeed;
            }
        }

        if (Mathf.Abs(_pendingZoom) > 1e-4f)
        {
            float step = _pendingZoom * (1f - Mathf.Exp(-zoomSmoothness * Time.deltaTime));
            transform.position += transform.forward * step;
            _pendingZoom -= step;
        }

        HandleOrbit(mouse);

        if (!mouse.middleButton.isPressed)
        {
            HandleKeyboardPan();
            HandleKeyboardYaw();
        }

        ApplyAutoRecenter();
        ClampPosition();
        UpdateFocalPoint();
        if (ApplyDistanceCap()) UpdateFocalPoint();
    }

    private bool ApplyDistanceCap()
    {
        if (terrainDataStore == null) return false;
        if (ClosestTerrainSource == TerrainPointSource.None) return false;
        if (!HasYAlignedFocalPoint) return false;

        Vector3 mapCenter = terrainDataStore.transform.position;
        float cx = ClosestTerrainPoint.x - mapCenter.x;
        float cz = ClosestTerrainPoint.z - mapCenter.z;
        float capDist = yAlignedDistanceFactor * Mathf.Sqrt(cx * cx + cz * cz);

        float dx = YAlignedFocalPoint.x - ClosestTerrainPoint.x;
        float dz = YAlignedFocalPoint.z - ClosestTerrainPoint.z;
        float distSq = dx * dx + dz * dz;
        if (distSq <= capDist * capDist) return false;

        float dist = Mathf.Sqrt(distSq);
        float pull = 1f - capDist / dist;
        float shiftX = -dx * pull;
        float shiftZ = -dz * pull;

        Vector3 pos = transform.position;
        pos.x += shiftX;
        pos.z += shiftZ;
        transform.position = pos;

        if (_grabbing)
        {
            _grabWorld.x += shiftX;
            _grabWorld.z += shiftZ;
        }

        return true;
    }

    private void ApplyAutoRecenter()
    {
        if (!autoRecenter) return;
        if (_orbiting) return;
        if (HasKeyboardYawInput()) return;
        if (!HasYAlignedFocalPoint) return;

        float dx = ClosestTerrainPoint.x - YAlignedFocalPoint.x;
        float dz = ClosestTerrainPoint.z - YAlignedFocalPoint.z;
        float distSq = dx * dx + dz * dz;
        if (distSq < recenterDeadZone * recenterDeadZone) return;

        float dist = Mathf.Sqrt(distSq);
        float speed = recenterBaseSpeed + dist * recenterDistanceFactor;
        float step = Mathf.Min(speed * Time.deltaTime, dist);
        float scale = step / dist;

        float moveX = dx * scale;
        float moveZ = dz * scale;

        Vector3 pos = transform.position;
        pos.x += moveX;
        pos.z += moveZ;
        transform.position = pos;

        if (_grabbing)
        {
            _grabWorld.x += moveX;
            _grabWorld.z += moveZ;
        }
    }

    private bool HasKeyboardYawInput()
    {
        var kb = Keyboard.current;
        if (kb == null) return false;
        return kb.eKey.isPressed || kb.qKey.isPressed;
    }

    private void HandleOrbit(Mouse mouse)
    {
        if (mouse.middleButton.wasPressedThisFrame)
        {
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            _orbiting = TryScreenToTerrain(screenCenter, out _orbitFocal);
            if (_orbiting && _orbVisual != null)
            {
                _orbVisual.transform.position = _orbitFocal;
                _orbVisual.SetActive(true);
            }
            _groundFollowing = false;
        }

        if (!mouse.middleButton.isPressed)
        {
            _orbiting = false;
            if (_orbVisual != null) _orbVisual.SetActive(false);
            return;
        }

        if (!_orbiting) return;

        Vector2 d = mouse.delta.ReadValue();
        if (d == Vector2.zero) return;

        if (d.x != 0f)
            transform.RotateAround(_orbitFocal, Vector3.up, d.x * yawSpeed);

        if (d.y != 0f)
        {
            float currentPitch = transform.eulerAngles.x;
            if (currentPitch > 180f) currentPitch -= 360f;

            float newPitch = Mathf.Clamp(currentPitch + d.y * pitchSpeed, minAngle, maxAngle);
            float actualDelta = newPitch - currentPitch;
            if (Mathf.Abs(actualDelta) > 0.0001f)
                transform.RotateAround(_orbitFocal, transform.right, actualDelta);
        }
    }

    private void HandleKeyboardPan()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        Vector2 input = Vector2.zero;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    input.y += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  input.y -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) input.x += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  input.x -= 1f;

        if (input == Vector2.zero) return;
        if (input.sqrMagnitude > 1f) input.Normalize();

        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        fwd.Normalize();
        Vector3 right = transform.right;
        right.y = 0f;
        right.Normalize();

        Vector3 move = right * input.x + fwd * input.y;
        transform.position += move * keyboardPanSpeed * transform.position.y * Time.deltaTime;
    }

    private void HandleKeyboardYaw()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        float dir = 0f;
        if (kb.eKey.isPressed) dir += 1f;
        if (kb.qKey.isPressed) dir -= 1f;
        if (dir == 0f) return;

        float angle = dir * keyboardYawSpeed * Time.deltaTime;

        if (_groundFollowing)
        {
            transform.Rotate(0f, angle, 0f, Space.World);
            return;
        }

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (!TryScreenToTerrain(screenCenter, out Vector3 focal)) return;

        transform.RotateAround(focal, Vector3.up, angle);
    }

    private bool TryScreenToTerrain(Vector2 screen, out Vector3 hit)
    {
        Ray r = _cam.ScreenPointToRay(screen);
        if (terrainDataStore != null && terrainDataStore.RaycastTerrain(r, out hit))
            return true;
        return TryRayToPlane(r, 0f, out hit);
    }

    private bool TryRayToPlane(Ray r, float planeY, out Vector3 hit)
    {
        hit = Vector3.zero;
        if (Mathf.Abs(r.direction.y) < 1e-6f) return false;
        float t = (planeY - r.origin.y) / r.direction.y;
        if (t <= 0f) return false;
        hit = r.origin + r.direction * t;
        return true;
    }

    private void ClampPosition()
    {
        Vector3 pos = transform.position;
        float startY = pos.y;
        pos.y = Mathf.Max(pos.y, minY);

        if (terrainDataStore != null)
        {
            float terrainY = terrainDataStore.GetRawHeight(pos);
            float floorY = terrainY + groundClearance;
            if (_groundFollowing)
            {
                pos.y = floorY;
            }
            else if (pos.y < floorY)
            {
                pos.y = floorY;
                _groundFollowing = true;
            }
        }

        if (_grabbing) _grabWorld.y += pos.y - startY;

        transform.position = pos;
    }
}
