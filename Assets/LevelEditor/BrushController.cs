using UnityEngine;

public enum BrushType { Raise, Lower, Smooth }

public class BrushController : MonoBehaviour
{
    [Header("References")]
    public LevelEditorManager levelManager;
    public LevelEditorTerrain terrain;
    public Camera             editorCamera;

    [Header("Brush Settings")]
    public float     BrushRadius   = 3f;
    public float     BrushStrength = 0.1f;

    // Grid step matches marching cubes density field (2 cells per unit)
    const float GridStep = 0.5f;

    public BrushType ActiveBrush { get; set; } = BrushType.Raise;

    void Update()
    {
        if (!Input.GetMouseButton(0)) return;

        var ray = editorCamera.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit)) return;

        ApplyBrush(hit.point);
        terrain?.Rebuild();
    }

    void ApplyBrush(Vector3 worldPos)
    {
        int cx = Mathf.RoundToInt(worldPos.x / GridStep);
        int cy = Mathf.RoundToInt(worldPos.y / GridStep);
        int cz = Mathf.RoundToInt(worldPos.z / GridStep);
        int radiusCells = Mathf.CeilToInt(BrushRadius / GridStep);

        for (int dx = -radiusCells; dx <= radiusCells; dx++)
        for (int dy = -radiusCells; dy <= radiusCells; dy++)
        for (int dz = -radiusCells; dz <= radiusCells; dz++)
        {
            int x = cx + dx, y = cy + dy, z = cz + dz;
            var cell = levelManager.GetCell(x, y, z);
            if (cell == null) continue;

            float dist    = new Vector3(dx, dy, dz).magnitude * GridStep;
            float falloff = Mathf.Clamp01(1f - dist / BrushRadius);

            if      (ActiveBrush == BrushType.Raise)  RaiseCell(cell, falloff);
            else if (ActiveBrush == BrushType.Lower)  LowerCell(cell, falloff);
            else if (ActiveBrush == BrushType.Smooth) SmoothCell(x, y, z, cell, falloff);
        }
    }

    void RaiseCell(MapCell cell, float falloff)
    {
        cell.density = Mathf.Clamp(cell.density + BrushStrength * falloff * Time.deltaTime, -1f, 1f);
    }

    void LowerCell(MapCell cell, float falloff)
    {
        cell.density = Mathf.Clamp(cell.density - BrushStrength * falloff * Time.deltaTime, -1f, 1f);
    }

    void SmoothCell(int x, int y, int z, MapCell cell, float falloff)
    {
        float avg = NeighbourAverage(x, y, z);
        // Blend toward neighbour average proportional to strength and falloff
        cell.density = Mathf.Lerp(cell.density, avg, BrushStrength * falloff * Time.deltaTime);
    }

    float NeighbourAverage(int x, int y, int z)
    {
        float sum   = 0f;
        int   count = 0;

        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        for (int dz = -1; dz <= 1; dz++)
        {
            if (dx == 0 && dy == 0 && dz == 0) continue;
            var n = levelManager.GetCell(x + dx, y + dy, z + dz);
            if (n == null) continue;
            sum += n.density;
            count++;
        }

        return count > 0 ? sum / count : 0f;
    }
}
