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
    /// Signed slope in degrees for each outgoing direction in <see cref="Directions"/>
    /// (same index order). Positive = uphill, negative = downhill.
    /// Indices 0–7 are the cardinal/diagonal moves, 8–15 the knight (2:1) moves.
    /// Edge cells get 0 for out-of-bounds directions. Derived from rawHeight — not
    /// serialized; re-baked on load.
    /// </summary>
    public float[] slopeOutgoing;

    /// <summary>The biome assigned to this cell.</summary>
    public BiomeSO biome;

    // Direction offsets, matching the slopeOutgoing index order (dx, dz).
    // 0–7: 8-connected king moves. 8–15: knight moves (2 steps one axis, 1 the
    // other) — opens up shallower routes; step cost is the Euclidean length √5.
    public static readonly Vector2Int[] Directions =
    {
        new Vector2Int( 0,  1), // 0  N
        new Vector2Int( 1,  1), // 1  NE
        new Vector2Int( 1,  0), // 2  E
        new Vector2Int( 1, -1), // 3  SE
        new Vector2Int( 0, -1), // 4  S
        new Vector2Int(-1, -1), // 5  SW
        new Vector2Int(-1,  0), // 6  W
        new Vector2Int(-1,  1), // 7  NW

        new Vector2Int( 1,  2), // 8  NNE
        new Vector2Int( 2,  1), // 9  ENE
        new Vector2Int( 2, -1), // 10 ESE
        new Vector2Int( 1, -2), // 11 SSE
        new Vector2Int(-1, -2), // 12 SSW
        new Vector2Int(-2, -1), // 13 WSW
        new Vector2Int(-2,  1), // 14 WNW
        new Vector2Int(-1,  2), // 15 NNW
    };
}
