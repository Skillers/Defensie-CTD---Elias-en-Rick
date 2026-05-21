using UnityEngine;

/// <summary>
/// Pre-computed data for a single terrain grid cell.
/// </summary>
[System.Serializable]
public struct CellData
{
    /// <summary>Non-rounded Perlin height (noise * heightMultiplier).</summary>
    public float rawHeight;

    /// <summary>Height snapped to 0.25 increments (matches marching cubes).</summary>
    public float roundedHeight;

    /// <summary>
    /// Signed slope in degrees for each of the 8 outgoing directions.
    /// Positive = uphill, negative = downhill.
    /// Index order: N(0), NE(1), E(2), SE(3), S(4), SW(5), W(6), NW(7).
    /// Edge cells get 0 for out-of-bounds directions.
    /// </summary>
    public float[] slopeOutgoing;

    /// <summary>The biome assigned to this cell.</summary>
    public BiomeSO biome;
    
    public ObstacleSO obstacle;

    // Direction offsets matching the index order (dx, dz)
    public static readonly Vector2Int[] Directions =
    {
        new Vector2Int( 0,  1), // N
        new Vector2Int( 1,  1), // NE
        new Vector2Int( 1,  0), // E
        new Vector2Int( 1, -1), // SE
        new Vector2Int( 0, -1), // S
        new Vector2Int(-1, -1), // SW
        new Vector2Int(-1,  0), // W
        new Vector2Int(-1,  1), // NW
    };
}
