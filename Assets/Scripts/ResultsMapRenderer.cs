using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a flat top-down <see cref="Texture2D"/> of a saved level. One pixel per
/// grid cell, colored by that cell's <see cref="BiomeSO.color"/>. Predicted and
/// actual paths from <see cref="UnitPathPlan"/>s are stamped on top — actual drawn
/// last so it wins where the two overlap.
/// Pixel (x, y) corresponds to grid cell (x, y) — gz is the texture's vertical axis,
/// matching the world layout where z runs "up" on the minimap.
/// </summary>
public static class ResultsMapRenderer
{
    public static Texture2D BuildMapTexture(
        SaveData save,
        IReadOnlyDictionary<string, BiomeSO> biomeLookup,
        IReadOnlyList<UnitPathPlan> plans,
        Color predictedColor,
        Color actualColor,
        int pathThickness,
        Color fallbackBiomeColor,
        Color startMarkerColor,
        Color endMarkerColor,
        int markerRadius)
    {
        if (save == null || save.cells == null || save.gridWidth <= 0 || save.gridHeight <= 0)
        {
            Debug.LogError("ResultsMapRenderer: save data is missing or empty.");
            return null;
        }

        int w = save.gridWidth;
        int h = save.gridHeight;
        Color32[] pixels = new Color32[w * h];

        // Pass 1: biome colors.
        for (int x = 0; x < w; x++)
        {
            for (int z = 0; z < h; z++)
            {
                CellDataDto dto = save.cells[x * h + z];
                Color color = fallbackBiomeColor;
                if (dto != null && !string.IsNullOrEmpty(dto.biomeName)
                    && biomeLookup != null
                    && biomeLookup.TryGetValue(dto.biomeName, out BiomeSO biome)
                    && biome != null)
                    color = biome.color;
                pixels[PixelIndex(x, z, w)] = color;
            }
        }

        // Pass 2: predicted paths, then actual on top so overlaps show "actual".
        if (plans != null)
        {
            foreach (var plan in plans)
                StampPath(pixels, w, h, plan != null ? plan.path : null, predictedColor, pathThickness);
            foreach (var plan in plans)
                StampPath(pixels, w, h, plan != null ? plan.actualPath : null, actualColor, pathThickness);
        }

        // Pass 3: start / end markers from the save (drawn last so they're never hidden
        // by a path that ends on the same cell).
        if (save.hasStart)
            StampCircle(pixels, w, h, save.startX, save.startZ, markerRadius, startMarkerColor);
        if (save.hasEnd)
            StampCircle(pixels, w, h, save.endX, save.endZ, markerRadius, endMarkerColor);

        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Point;
        tex.wrapMode   = TextureWrapMode.Clamp;
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    static void StampPath(Color32[] pixels, int w, int h, IReadOnlyList<Vector2Int> path,
                          Color color, int thickness)
    {
        if (path == null || path.Count == 0) return;

        Color32 c32 = color;
        int radius = Mathf.Max(0, (thickness - 1) / 2);

        for (int i = 0; i < path.Count; i++)
        {
            Vector2Int cell = path[i];
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    int nx = cell.x + dx;
                    int nz = cell.y + dz;
                    if (nx < 0 || nx >= w || nz < 0 || nz >= h) continue;
                    pixels[PixelIndex(nx, nz, w)] = c32;
                }
            }
        }
    }

    static void StampCircle(Color32[] pixels, int w, int h, int cx, int cz, int radius, Color color)
    {
        if (radius < 0) return;
        Color32 c32 = color;
        int r2 = radius * radius;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (dx * dx + dz * dz > r2) continue;
                int nx = cx + dx;
                int nz = cz + dz;
                if (nx < 0 || nx >= w || nz < 0 || nz >= h) continue;
                pixels[PixelIndex(nx, nz, w)] = c32;
            }
        }
    }

    static int PixelIndex(int x, int z, int width) => z * width + x;
}
