using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public class SimulationCameraController : MonoBehaviour
{
    [SerializeField] float zoomSpeed = 8f;
    [SerializeField] float keyboardPanSpeed = 0.5f;
    [SerializeField] float yawSpeed = 0.3f;
    [SerializeField] float pitchSpeed = 0.3f;
    [SerializeField] float minAngle = 10f;
    [SerializeField] float maxAngle = 89f;
    [SerializeField] float minY = 0f;
    [SerializeField] float maxY = 250f;
    [SerializeField] float startDistance = 200f;
    [SerializeField] float startAngle = 45f;
    [SerializeField] float panBottomBuffer = 50f;
    [SerializeField] float orbSize = 1.5f;
    [SerializeField] Color orbColor = Color.cyan;
    [SerializeField] private TerrainDataStore terrainDataStore;

    private Camera _cam;
    private Vector3 _grabWorld;
    private bool _grabbing;
    private Vector3 _orbitFocal;
    private bool _orbiting;
    private GameObject _orbVisual;

    private void Awake()
    {
        _cam = GetComponent<Camera>();
        if (terrainDataStore == null)
            terrainDataStore = FindFirstObjectByType<TerrainDataStore>();

        float rad = startAngle * Mathf.Deg2Rad;
        transform.position = new Vector3(0f, Mathf.Sin(rad) * startDistance, -Mathf.Cos(rad) * startDistance);
        transform.rotation = Quaternion.Euler(startAngle, 0f, 0f);

        CreateOrbVisual();
    }

    private void OnDestroy()
    {
        if (_orbVisual != null) Destroy(_orbVisual);
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
            HandleKeyboardPan();

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

        transform.position += new Vector3(input.x, 0f, input.y) * keyboardPanSpeed * transform.position.y * Time.deltaTime;
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
        pos.y = Mathf.Clamp(pos.y, minY, maxY);

        if (terrainDataStore != null && !_orbiting)
        {
            Vector3 mapCenter = terrainDataStore.transform.position;
            pos.x = Mathf.Clamp(pos.x, mapCenter.x - terrainDataStore.extentX, mapCenter.x + terrainDataStore.extentX);
            pos.z = Mathf.Clamp(pos.z, mapCenter.z - terrainDataStore.extentZ - panBottomBuffer, mapCenter.z + terrainDataStore.extentZ);
        }

        transform.position = pos;
    }
}
