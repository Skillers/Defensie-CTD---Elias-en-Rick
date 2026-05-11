using UnityEngine;
using UnityEngine.InputSystem;

public class SimulationCameraController : MonoBehaviour
{
    [SerializeField] float panSpeed = 25f;
    [SerializeField] float panHeightScale = 220f;
    [SerializeField] float zoomSpeed = 25f;
    [SerializeField] float minY = 0f;
    [SerializeField] float maxY = 250f;
    [SerializeField] float startDistance = 200f;
    [SerializeField] float startAngle = 45f;
    [SerializeField] float panBottomBuffer = 50f;
    [SerializeField] private TerrainDataStore terrainDataStore;

    private Vector2 _lastMousePos;

    private void Awake()
    {
        if (terrainDataStore == null)
            terrainDataStore = FindFirstObjectByType<TerrainDataStore>();

        float rad = startAngle * Mathf.Deg2Rad;
        transform.position = new Vector3(0f, Mathf.Sin(rad) * startDistance, -Mathf.Cos(rad) * startDistance);
        transform.rotation = Quaternion.Euler(startAngle, 0f, 0f);
    }

    private void Update()
    {
        var mouse = Mouse.current;

        if (mouse.middleButton.wasPressedThisFrame)
            _lastMousePos = mouse.position.ReadValue();

        if (mouse.middleButton.isPressed)
        {
            Vector2 currentPos = mouse.position.ReadValue();
            Vector2 delta = currentPos - _lastMousePos;
            transform.position += new Vector3(-delta.x, 0f, -delta.y) * panSpeed * (transform.position.y / panHeightScale);
            _lastMousePos = currentPos;
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (scroll != 0f)
            transform.position += Vector3.down * (scroll * zoomSpeed);

        ClampPosition();
    }

    private void ClampPosition()
    {
        Vector3 pos = transform.position;
        pos.y = Mathf.Clamp(pos.y, minY, maxY);

        if (terrainDataStore != null)
        {
            Vector3 mapCenter = terrainDataStore.transform.position;
            pos.x = Mathf.Clamp(pos.x, mapCenter.x - terrainDataStore.extentX, mapCenter.x + terrainDataStore.extentX);
            pos.z = Mathf.Clamp(pos.z, mapCenter.z - terrainDataStore.extentZ - panBottomBuffer, mapCenter.z + terrainDataStore.extentZ);
        }

        transform.position = pos;
    }
}
