using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public class SimulationCameraController : MonoBehaviour
{
    [SerializeField] float zoomSpeed = 8f;
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
    [SerializeField] private TerrainDataStore terrainDataStore;

    private Camera _cam;
    private Vector3 _grabWorld;
    private bool _grabbing;
    private Vector3 _orbitFocal;
    private bool _orbiting;
    private GameObject _orbVisual;
    private GameObject _focalOrb;
    private GameObject _lastTerrainOrb;
    private bool _groundFollowing;

    public Vector3 FocalPoint { get; private set; }
    public Vector3 LastTerrainPoint { get; private set; }
    public bool HasLastTerrainPoint { get; private set; }

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
    }

    private void OnDestroy()
    {
        if (_orbVisual != null) Destroy(_orbVisual);
        if (_focalOrb != null) Destroy(_focalOrb);
        if (_lastTerrainOrb != null) Destroy(_lastTerrainOrb);
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

    private void OnValidate()
    {
        if (_focalOrb != null) _focalOrb.SetActive(showFocalOrb);
        if (_lastTerrainOrb != null) _lastTerrainOrb.SetActive(showLastTerrainOrb && HasLastTerrainPoint);
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
                transform.position += transform.forward * (scroll * zoomSpeed);
            }
        }

        HandleOrbit(mouse);

        if (!mouse.middleButton.isPressed)
        {
            HandleKeyboardPan();
            HandleKeyboardYaw();
        }

        ClampPosition();
        UpdateFocalPoint();
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
