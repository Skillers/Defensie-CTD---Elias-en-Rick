using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the Camera GameObject.
/// Arrow keys pan on the XZ plane.
/// Mouse wheel zooms by moving along the Y axis.
/// </summary>
public class CameraController : MonoBehaviour
{
    [Header("Pan")]
    public float panSpeed  = 50f;

    [Header("Zoom")]
    public float zoomSpeed = 10f;
    public float minSize   = 10f;
    public float maxSize   = 300f;

    void Update()
    {
        var kb = Keyboard.current;

        // ── Pan (arrow keys) ──────────────────────────────────────────────
        float x = 0f, z = 0f;
        if (kb.leftArrowKey.isPressed)  x = -1f;
        if (kb.rightArrowKey.isPressed) x =  1f;
        if (kb.downArrowKey.isPressed)  z = -1f;
        if (kb.upArrowKey.isPressed)    z =  1f;

        Vector3 pan = new Vector3(x, 0f, z).normalized * panSpeed * Time.deltaTime;
        transform.position += pan;

        // ── Zoom (mouse wheel) ────────────────────────────────────────────
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (scroll != 0f)
        {
            Camera cam = Camera.main;
            cam.orthographicSize = Mathf.Clamp(
                cam.orthographicSize - Mathf.Sign(scroll) * zoomSpeed,
                minSize, maxSize
            );
        }
    }
}
