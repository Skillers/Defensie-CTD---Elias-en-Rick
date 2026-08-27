using UnityEngine;

/// <summary>
/// Attach to each cylinder child.
/// Reads moveDirection from the parent UnitMover and instantly
/// rotates to face it — no smoothing, sharp turns.
///
/// Works on the XZ plane: rotates around Y axis.
/// </summary>
public class UnitFacer : MonoBehaviour
{
    UnitMover agent;

    void Awake()
    {
        // Walk up to find UnitMover on the parent
        agent = GetComponentInParent<UnitMover>();

        if (agent == null)
            Debug.LogError("UnitFacer: no UnitMover found in parent hierarchy.");
    }

    void Update()
    {
        if (agent == null) return;

        Vector3 dir = agent.moveDirection;
        if (dir.sqrMagnitude < 0.001f) return;

        // Instant snap — rotate around Y axis to face movement direction on XZ plane
        float angle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, angle, 0f);
    }
}