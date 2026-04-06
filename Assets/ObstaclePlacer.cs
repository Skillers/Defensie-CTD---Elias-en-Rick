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
    public TerrainDataStore terrainDataStore;

    [Header("Blocking biome (impassable)")]
    [Tooltip("Assign a BiomeSO with defaultMovementCost = int.MaxValue.")]
    public BiomeSO blockBiome;

    [Header("Visuals")]
    public Color blockColor = new Color(1f, 0.1f, 0.1f, 0.4f);
    public Color slowColor  = new Color(1f, 0.6f, 0.0f, 0.4f);

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
        Ray ray = Camera.main.ScreenPointToRay(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        if (Mathf.Abs(ray.direction.y) < 0.001f) return;
        float t = -ray.origin.y / ray.direction.y;
        Vector3 worldPos = ray.origin + ray.direction * t;

        Vector2Int center = terrainDataStore.WorldToGrid(worldPos);

        for (int dx = -1; dx <= 1; dx++)
        for (int dz = -1; dz <= 1; dz++)
        {
            int cx = center.x + dx;
            int cz = center.y + dz;
            if (!terrainDataStore.InBounds(cx, cz)) continue;

            if (block && blockBiome != null)
            {
                terrainDataStore.grid[cx, cz] = new BiomeCell { biome = blockBiome };
            }
            else if (!block)
            {
                var cell = terrainDataStore.grid[cx, cz];
                if (cell?.biome != null)
                    cell.biome.defaultMovementCost += slowCostIncrease;
            }
        }

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.position   = terrainDataStore.GridToWorld(center) + Vector3.up * 0.5f;
        cube.transform.localScale = new Vector3(3f, 1f, 3f);

        Destroy(cube.GetComponent<Collider>());

        cube.GetComponent<MeshRenderer>().material =
            CreateTransparentMaterial(block ? blockColor : slowColor);

        foreach (var mover in FindObjectsByType<UnitMover>(FindObjectsSortMode.None))
            mover.RequestPath();
    }

    static Material CreateTransparentMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.SetColor("_BaseColor", color);
            return mat;
        }

        shader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
        if (shader != null)
        {
            var mat = new Material(shader);
            mat.color = color;
            return mat;
        }

        var fallback = new Material(Shader.Find("Standard"));
        fallback.color = color;
        return fallback;
    }
}
