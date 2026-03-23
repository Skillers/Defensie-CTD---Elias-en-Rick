using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to the Flag GameObject.
/// WASD moves the flag on the XZ plane.
/// </summary>
public class FlagMover : MonoBehaviour
{
    public float moveSpeed = 20f;

    void Update()
    {
        var kb = Keyboard.current;

        float x = 0f, z = 0f;
        if (kb.aKey.isPressed) x = -1f;
        if (kb.dKey.isPressed) x =  1f;
        if (kb.sKey.isPressed) z = -1f;
        if (kb.wKey.isPressed) z =  1f;

        Vector3 dir = new Vector3(x, 0f, z).normalized;
        transform.position += dir * moveSpeed * Time.deltaTime;
    }
}
