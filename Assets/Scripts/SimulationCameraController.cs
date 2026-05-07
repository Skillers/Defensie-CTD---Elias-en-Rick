using UnityEngine;
using UnityEngine.InputSystem;

public class SimulationCameraController : MonoBehaviour
{
    [SerializeField] float panSpeed = 25f;
    [SerializeField] float zoomSpeed = 25f;
    [SerializeField] float maxZoom = 100f;
    [SerializeField] private Camera placementCamera;
    [SerializeField] private float minHeightAboveTerrain = 10f;
    [SerializeField] private LayerMask terrainLayerMask;

    private Vector2 _lastMousePos;

    private void Awake()
    {
        transform.rotation = Quaternion.Euler(90f, 0f, 0f);
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
            transform.position += new Vector3(-delta.x, 0f, -delta.y) * panSpeed * (transform.position.y / 220f) * Time.deltaTime;
            _lastMousePos = currentPos;
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (scroll != 0f)
        {
            float targetY = transform.position.y - (scroll * zoomSpeed * Time.deltaTime);
            targetY = Mathf.Min(targetY, maxZoom);

            float highestHitY = float.MinValue;
            Vector3[] corners = new Vector3[]
            {
                placementCamera.ScreenToWorldPoint(new Vector3(0, 0, 0)),
                placementCamera.ScreenToWorldPoint(new Vector3(Screen.width, 0, 0)),
                placementCamera.ScreenToWorldPoint(new Vector3(0, Screen.height, 0)),
                placementCamera.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, 0))
            };
            foreach (var corner in corners)
            {
                Vector3 origin = new Vector3(corner.x, transform.position.y, corner.z);
                if (Physics.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, Mathf.Infinity, terrainLayerMask))
                    highestHitY = Mathf.Max(highestHitY, hit.point.y);
            }
            if (highestHitY > float.MinValue)
            {
                float minY = highestHitY + minHeightAboveTerrain;
                targetY = Mathf.Max(targetY, minY);
            }

            transform.position = new Vector3(transform.position.x, targetY, transform.position.z);
        }

        ClampToTerrain();
    }

    private void ClampToTerrain()
    {
        Vector3 pos = transform.position;

        float highestHitY = float.MinValue;
        Vector3[] corners = new Vector3[]
        {
            placementCamera.ScreenToWorldPoint(new Vector3(0, 0, 0)),
            placementCamera.ScreenToWorldPoint(new Vector3(Screen.width, 0, 0)),
            placementCamera.ScreenToWorldPoint(new Vector3(0, Screen.height, 0)),
            placementCamera.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, 0))
        };
        foreach (var corner in corners)
        {
            Vector3 origin = new Vector3(corner.x, pos.y, corner.z);
            if (Physics.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, Mathf.Infinity, terrainLayerMask))
                highestHitY = Mathf.Max(highestHitY, hit.point.y);
        }
        if (highestHitY > float.MinValue)
            pos.y = Mathf.Max(pos.y, highestHitY + minHeightAboveTerrain);
        pos.y = Mathf.Min(pos.y, maxZoom);

        transform.position = pos;
    }
}
