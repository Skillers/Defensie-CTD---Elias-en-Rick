using UnityEngine;

/// <summary>
/// Serializable DTO for a full level save. Written to JSON on disk.
/// JsonUtility can't serialize 2D arrays, Nullable&lt;T&gt;, or ScriptableObject
/// references, so the grid is flattened and biomes are stored by name.
/// </summary>
[System.Serializable]
public class SaveData
{
    // Plane extents
    public float extentX;
    public float extentZ;

    // Grid settings
    public float step;
    public float roundStep;

    // Noise settings
    public int seed;
    public float noiseScale;
    public Vector2 noiseOffset;
    public float heightMultiplier;

    // Grid dimensions and flattened cells (row-major: index = x * height + z)
    public int gridWidth;
    public int gridHeight;
    public CellDataDto[] cells;

    // Start / End flags (JsonUtility can't serialize nullables)
    public bool hasStart;
    public int startX;
    public int startZ;

    public bool hasEnd;
    public int endX;
    public int endZ;
}

/// <summary>
/// Serializable mirror of CellData. Stores the biome by name so it can be
/// resolved back to a BiomeSO asset at load time via the registry.
/// </summary>
[System.Serializable]
public class CellDataDto
{
    public float rawHeight;
    public float roundedHeight;
    public float[] slopeOutgoing;
    public string biomeName;
}
