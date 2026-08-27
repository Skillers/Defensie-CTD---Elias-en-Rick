using UnityEngine;

/// <summary>
/// JSON DTO for a full level save. JsonUtility can't serialize 2D arrays, nullables or
/// ScriptableObject references, so the grid is flattened and biomes are stored by name.
/// </summary>
[System.Serializable]
public class SaveData
{
    public float extentX;
    public float extentZ;

    public float step;
    public float roundStep;

    public int seed;
    public float noiseScale;
    public Vector2 noiseOffset;
    public float heightMultiplier;

    // Flattened cells: index = x * gridHeight + z.
    public int gridWidth;
    public int gridHeight;
    public CellDataDto[] cells;

    public bool hasStart;
    public int startX;
    public int startZ;

    public bool hasEnd;
    public int endX;
    public int endZ;

    public AvenueDto[] avenues;
}

/// <summary>Serializable mirror of CellData; the biome is stored by name.</summary>
[System.Serializable]
public class CellDataDto
{
    public float rawHeight;
    public float roundedHeight;
    public string biomeName;
}

/// <summary>Serializable mirror of an AvenueOfApproach. Waypoints are parallel int arrays for JsonUtility's sake.</summary>
[System.Serializable]
public class AvenueDto
{
    public int index;
    public string title;
    public int[] waypointXs;
    public int[] waypointZs;
}
