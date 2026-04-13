using UnityEngine;
using UnityEngine.InputSystem;

public class BrushController : MonoBehaviour
{
    [Header("References")]
    public TerrainDataStore       terrainDataStore;
    public MarchingCubesTerrain   marchingCubes;
    public Camera                 editorCamera;

    [Header("Brush Settings")]
    public float BrushRadius   = 3f;
    public float BrushStrength = 0.1f;

    void Update()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        bool lower = mouse.leftButton.isPressed;
        bool raise = mouse.rightButton.isPressed;
        if (!lower && !raise) return;

        var ray = editorCamera.ScreenPointToRay(mouse.position.ReadValue());
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        Vector2Int center = terrainDataStore.WorldToGrid(hit.point);
        int radiusCells = Mathf.CeilToInt(BrushRadius / terrainDataStore.step);
        float sign = raise ? 1f : -1f;

        for (int dx = -radiusCells; dx <= radiusCells; dx++)
        for (int dz = -radiusCells; dz <= radiusCells; dz++)
        {
            int gx = center.x + dx;
            int gz = center.y + dz;
            if (!terrainDataStore.InBounds(gx, gz)) continue;

            float dist    = new Vector2(dx, dz).magnitude * terrainDataStore.step;
            if (dist > BrushRadius) continue;
            float falloff = 1f - dist / BrushRadius;

            terrainDataStore.grid[gx, gz].rawHeight += sign * BrushStrength * falloff * Time.deltaTime;
        }

        marchingCubes.Generate();
    }
}
