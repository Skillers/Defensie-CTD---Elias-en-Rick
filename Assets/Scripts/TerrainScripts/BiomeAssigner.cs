using UnityEngine;

/// <summary>
/// Assigns a default biome to every cell in the grid.
/// Called by MapGenerator after heights and slopes have been baked.
/// </summary>
public class BiomeAssigner : MonoBehaviour
{
    [Tooltip("Default biome applied to the entire grid (e.g. Grass).")]
    public BiomeSO defaultBiome;

    public void Assign(CellData[,] grid, int width, int height)
    {
        if (defaultBiome == null)
        {
            Debug.LogWarning("BiomeAssigner: no default biome assigned.");
            return;
        }

        int count = 0;
        for (int x = 0; x < width; x++)
        for (int z = 0; z < height; z++)
        {
            grid[x, z].biome = defaultBiome;
            count++;
        }
        Debug.Log($"BiomeAssigner: assigned '{defaultBiome.biomeName}' (color: {defaultBiome.color}) to {count} cells.");
    }
}
