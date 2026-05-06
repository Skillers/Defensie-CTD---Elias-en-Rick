using UnityEngine;
using UnityEngine.InputSystem;

public class SimulationCameraController : MonoBehaviour
{
    [SerializeField] float panSpeed = 25f;
    [SerializeField] float zoomSpeed = 25f;
    [SerializeField] float minZoom = 5f;
    [SerializeField] float maxZoom = 100f;

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
            transform.position += new Vector3(-delta.x * panSpeed, 0f, -delta.y * panSpeed) * Time.deltaTime;
            _lastMousePos = currentPos;
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (scroll != 0f)
        {
            Vector3 pos = transform.position;
            pos.y = Mathf.Clamp(pos.y - scroll * zoomSpeed * Time.deltaTime, minZoom, maxZoom);
            transform.position = pos;
        }
    }
}
