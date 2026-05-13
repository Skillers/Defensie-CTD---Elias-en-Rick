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
    [SerializeField] float maxY = 250f;
    [SerializeField] float startDistance = 200f;
    [SerializeField] float startAngle = 45f;
    [SerializeField] float orbSize = 1.5f;
    [SerializeField] Color orbColor = Color.cyan;
    [SerializeField] Color edgeWallColor = new Color(0f, 0.5f, 1f, 1f);
    [SerializeField] float edgeWallHeight = 250f;
    [SerializeField] private TerrainDataStore terrainDataStore;

    private Camera _cam;
    private Vector3 _grabWorld;
    private bool _grabbing;
    private Vector3 _orbitFocal;
    private bool _orbiting;
    private GameObject _orbVisual;
    private GameObject[] _edgeWalls;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        if (terrainDataStore == null)
            terrainDataStore = FindFirstObjectByType<TerrainDataStore>();

        float rad = startAngle * Mathf.Deg2Rad;
        transform.position = new Vector3(0f, Mathf.Sin(rad) * startDistance, -Mathf.Cos(rad) * startDistance);
        transform.rotation = Quaternion.Euler(startAngle, 0f, 0f);

        CreateOrbVisual();
        CreateEdgeWalls();
    }

    private void OnDestroy()
    {
        if (_orbVisual != null) Destroy(_orbVisual);
        if (_edgeWalls != null)
            foreach (var w in _edgeWalls) if (w != null) Destroy(w);
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

    private void CreateEdgeWalls()
    {
        if (terrainDataStore == null) return;

        Vector3 center = terrainDataStore.transform.position;
        float ex = terrainDataStore.extentX;
        float ez = terrainDataStore.extentZ;
        float h = edgeWallHeight;

        _edgeWalls = new GameObject[4];
        _edgeWalls[0] = MakeEdgeWall("EdgeWallSouth", new Vector3(center.x, h * 0.5f, center.z - ez), Quaternion.Euler(0f, 180f, 0f), new Vector3(ex * 2f, h, 1f));
        _edgeWalls[1] = MakeEdgeWall("EdgeWallNorth", new Vector3(center.x, h * 0.5f, center.z + ez), Quaternion.identity,         new Vector3(ex * 2f, h, 1f));
        _edgeWalls[2] = MakeEdgeWall("EdgeWallWest",  new Vector3(center.x - ex, h * 0.5f, center.z), Quaternion.Euler(0f, -90f, 0f), new Vector3(ez * 2f, h, 1f));
        _edgeWalls[3] = MakeEdgeWall("EdgeWallEast",  new Vector3(center.x + ex, h * 0.5f, center.z), Quaternion.Euler(0f,  90f, 0f), new Vector3(ez * 2f, h, 1f));
    }

    private GameObject MakeEdgeWall(string name, Vector3 pos, Quaternion rot, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);
        go.transform.position = pos;
        go.transform.rotation = rot;
        go.transform.localScale = scale;
        var mr = go.GetComponent<MeshRenderer>();
        if (mr != null) mr.material.color = edgeWallColor;
        return go;
    }

    private void Update()
    {
        var mouse = Mouse.current;

        if (!mouse.middleButton.isPressed)
        {
            if (mouse.rightButton.wasPressedThisFrame)
                _grabbing = TryScreenToTerrain(mouse.position.ReadValue(), out _grabWorld);

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
                transform.position += transform.forward * (scroll * zoomSpeed);
        }

        HandleOrbit(mouse);

        if (!mouse.middleButton.isPressed)
        {
            HandleKeyboardPan();
            HandleKeyboardYaw();
        }

        ClampPosition();
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

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        if (!TryScreenToTerrain(screenCenter, out Vector3 focal)) return;

        transform.RotateAround(focal, Vector3.up, dir * keyboardYawSpeed * Time.deltaTime);
    }

    private bool TryScreenToTerrain(Vector2 screen, out Vector3 hit)
    {
        Ray r = _cam.ScreenPointToRay(screen);
        if (terrainDataStore != null && terrainDataStore.RaycastTerrain(r, out hit))
            return true;
        if (TryEdgeWallRaycast(r, out hit))
            return true;
        return TryRayToPlane(r, 0f, out hit);
    }

    private bool TryEdgeWallRaycast(Ray r, out Vector3 hit)
    {
        hit = Vector3.zero;
        if (terrainDataStore == null) return false;

        Vector3 c = terrainDataStore.transform.position;
        float ex = terrainDataStore.extentX;
        float ez = terrainDataStore.extentZ;
        float h = edgeWallHeight;

        float bestT = float.MaxValue;
        Vector3 bestHit = Vector3.zero;
        bool found = false;

        TryWallHit(r, true,  c.z - ez, +1f, c.x - ex, c.x + ex, h, ref bestT, ref bestHit, ref found);
        TryWallHit(r, true,  c.z + ez, -1f, c.x - ex, c.x + ex, h, ref bestT, ref bestHit, ref found);
        TryWallHit(r, false, c.x - ex, +1f, c.z - ez, c.z + ez, h, ref bestT, ref bestHit, ref found);
        TryWallHit(r, false, c.x + ex, -1f, c.z - ez, c.z + ez, h, ref bestT, ref bestHit, ref found);

        hit = bestHit;
        return found;
    }

    private void TryWallHit(Ray r, bool planePerpZ, float planeCoord, float normalSign, float minLat, float maxLat, float maxH, ref float bestT, ref Vector3 bestHit, ref bool found)
    {
        float rDir = planePerpZ ? r.direction.z : r.direction.x;
        float rOri = planePerpZ ? r.origin.z    : r.origin.x;
        if (Mathf.Abs(rDir) < 1e-6f) return;
        if (rDir * normalSign >= 0f) return; // back-face / parallel: ignore
        float t = (planeCoord - rOri) / rDir;
        if (t <= 0f || t >= bestT) return;
        Vector3 p = r.origin + r.direction * t;
        if (p.y < 0f || p.y > maxH) return;
        float lat = planePerpZ ? p.x : p.z;
        if (lat < minLat || lat > maxLat) return;
        bestT = t;
        bestHit = p;
        found = true;
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
        pos.y = Mathf.Clamp(pos.y, minY, maxY);

        if (terrainDataStore != null)
        {
            Vector3 mapCenter = terrainDataStore.transform.position;
            Vector3 fwd = transform.forward;

            float groundT = (fwd.y < -1e-4f) ? pos.y / -fwd.y : 0f;
            float dx = fwd.x * groundT;
            float dz = fwd.z * groundT;

            float minX = mapCenter.x - terrainDataStore.extentX - Mathf.Max(0f, dx);
            float maxX = mapCenter.x + terrainDataStore.extentX - Mathf.Min(0f, dx);
            float minZ = mapCenter.z - terrainDataStore.extentZ - Mathf.Max(0f, dz);
            float maxZ = mapCenter.z + terrainDataStore.extentZ - Mathf.Min(0f, dz);

            pos.x = Mathf.Clamp(pos.x, minX, maxX);
            pos.z = Mathf.Clamp(pos.z, minZ, maxZ);
        }

        transform.position = pos;
    }
}
