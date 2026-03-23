using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Attach to any GameObject in the scene.
/// Q — places a blocking obstacle (3x3 tiles become impassable).
/// E — places a slowing obstacle  (3x3 tiles get +5 movement cost).
/// Both are visualised as a transparent 3x3 cube on the terrain.
/// The cube is placed under the screen-centre crosshair.
/// </summary>
public class ObstaclePlacer : MonoBehaviour
{
    [Header("References")]
    public VoronoiMap voronoiMap;

    [Header("Visuals")]
    public Color blockColor = new Color(1f, 0.1f, 0.1f, 0.4f);   // transparent red
    public Color slowColor  = new Color(1f, 0.6f, 0.0f, 0.4f);   // transparent orange

    [Header("Cost")]
    public int slowCostIncrease = 5;

    void Update()
    {
        var kb = Keyboard.current;
        if (kb.qKey.wasPressedThisFrame) Place(block: true);
        if (kb.eKey.wasPressedThisFrame) Place(block: false);
    }

    void Place(bool block)
    {
        // ── Find terrain position under screen centre ─────────────────────
        Ray ray = Camera.main.ScreenPointToRay(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        // Intersect with Y = 0 plane
        if (Mathf.Abs(ray.direction.y) < 0.001f) return;
        float t = -ray.origin.y / ray.direction.y;
        Vector3 worldPos = ray.origin + ray.direction * t;

        Vector2Int center = voronoiMap.WorldToGrid(worldPos);

        // ── Update 3x3 grid cells ─────────────────────────────────────────
        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            int cx = center.x + dx;
            int cz = center.y + dz;
            if (!voronoiMap.InBounds(cx, cz)) continue;

            if (block)
                voronoiMap.grid[cx, cz].movementCost = int.MaxValue;
            else
                voronoiMap.grid[cx, cz].movementCost += slowCostIncrease;
        }

        // ── Spawn transparent cube ────────────────────────────────────────
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position   = voronoiMap.GridToWorld(center) + Vector3.up * 0.5f;
        cube.transform.localScale = new Vector3(3f, 1f, 3f);

        // Remove collider — we don't want it interfering with raycasts
        Destroy(cube.GetComponent<Collider>());

        cube.GetComponent<MeshRenderer>().material =
            CreateTransparentMaterial(block ? blockColor : slowColor);

        // ── Trigger repath on all squads ──────────────────────────────────
        foreach (var mover in FindObjectsByType<UnitMover>(FindObjectsSortMode.None))
            mover.RequestPath();
    }

    static Material CreateTransparentMaterial(Color color)
    {
        // URP: Particles/Unlit supports transparency and reads _BaseColor
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            return mat;
        }

        // Built-in fallback
        shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.color = color;
            return mat;
        }

        // Last resort — won't be transparent but at least shows something
        var fallback = new Material(Shader.Find("Standard"));
        fallback.color = color;
        return fallback;
    }
}
