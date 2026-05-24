using UnityEngine;

/// <summary>
/// One cell the unit physically crosses on a step in some direction, plus that
/// cell's share of the step's Euclidean length (portions sum to 1).
/// </summary>
public struct CellCrossing
{
    public Vector2Int offset;
    public float portion;
}

/// <summary>
/// Geometric helpers for the 16 movement directions in <see cref="CellData.Directions"/>.
/// For each direction index it knows:
///   • <see cref="Crossings"/>: the cells the step's straight line passes through
///     (excluding the start cell), with each cell's fractional share of the step.
///   • <see cref="StepLengths"/>: the step's Euclidean length in grid units.
///
/// The pathfinder and the unit speed calculation iterate Crossings so every cell
/// the unit actually moves through gets its biome / obstacle cost charged, and a
/// blocked intermediate cancels the whole step. Pre-computed once at type init.
/// </summary>
public static class CellPathing
{
    public static readonly CellCrossing[][] Crossings  = ComputeCrossings();
    public static readonly float[]          StepLengths = ComputeStepLengths();

    static CellCrossing[][] ComputeCrossings()
    {
        int n = CellData.Directions.Length;
        var result = new CellCrossing[n][];
        for (int i = 0; i < n; i++)
            result[i] = CrossingsFor(CellData.Directions[i]);
        return result;
    }

    static float[] ComputeStepLengths()
    {
        int n = CellData.Directions.Length;
        var result = new float[n];
        for (int i = 0; i < n; i++)
        {
            Vector2Int d = CellData.Directions[i];
            result[i] = Mathf.Sqrt(d.x * d.x + d.y * d.y);
        }
        return result;
    }

    static CellCrossing[] CrossingsFor(Vector2Int delta)
    {
        int absX = Mathf.Abs(delta.x);
        int absZ = Mathf.Abs(delta.y);

        // Cardinal (|dx|+|dz|=1) or single-step diagonal (|dx|=|dz|=1):
        // the destination cell is the only one the step enters.
        if (absX <= 1 && absZ <= 1)
            return new[] { new CellCrossing { offset = delta, portion = 1f } };

        // Knight (2:1) move: the line from start to dest crosses 4 cells in 1/4
        // pieces. Excluding the start, we charge the two intermediates and the
        // destination 1/3 each (so the three cells together account for the full
        // step cost). Direction-agnostic via sign(dx), sign(dz).
        int signX = delta.x > 0 ? 1 : (delta.x < 0 ? -1 : 0);
        int signZ = delta.y > 0 ? 1 : (delta.y < 0 ? -1 : 0);

        // "Along the long axis": the cell halfway along the 2-cell side.
        Vector2Int interA = new Vector2Int(signX * (absX - 1), signZ * (absZ - 1));
        // The diagonal cell off the start.
        Vector2Int interB = new Vector2Int(signX, signZ);

        return new[]
        {
            new CellCrossing { offset = interA, portion = 1f / 3f },
            new CellCrossing { offset = interB, portion = 1f / 3f },
            new CellCrossing { offset = delta,  portion = 1f / 3f },
        };
    }
}
