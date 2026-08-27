using UnityEngine;

/// <summary>One cell a step passes through, and how much of the step's cost it pays.</summary>
public struct CellCrossing
{
    public Vector2Int offset;
    public float portion;
}

/// <summary>For every movement direction: which cells a step passes through, and how long that step is.</summary>
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

        // Straight or diagonal step: only the destination is entered.
        if (absX <= 1 && absZ <= 1)
            return new[] { new CellCrossing { offset = delta, portion = 1f } };

        // Knight move: three cells are entered, each paying a third of the cost.
        int signX = delta.x > 0 ? 1 : (delta.x < 0 ? -1 : 0);
        int signZ = delta.y > 0 ? 1 : (delta.y < 0 ? -1 : 0);

        Vector2Int interA = new Vector2Int(signX * (absX - 1), signZ * (absZ - 1));
        Vector2Int interB = new Vector2Int(signX, signZ);

        return new[]
        {
            new CellCrossing { offset = interA, portion = 1f / 3f },
            new CellCrossing { offset = interB, portion = 1f / 3f },
            new CellCrossing { offset = delta,  portion = 1f / 3f },
        };
    }
}
