using System.Collections.Generic;
using UnityEngine;

public static class AStarPathfinder
{
    private const float SQRT2 = 1.41421356f;

    private class Node
    {
        public Vector2Int pos;
        public float gCost;
        public float hCost;
        public float fCost => gCost + hCost;
        public Node parent;
    }

    /// <summary>Grid path from start to goal (inclusive), empty if none. totalCost converts to seconds via <see cref="CostToSeconds"/>.</summary>
    public static List<Vector2Int> FindPath(
        CellData[,] grid,
        int gridWidth,
        int gridHeight,
        Vector2Int start,
        Vector2Int goal,
        out float totalCost,
        int unitSize = 5,
        UnitTypeSO unitType = null,
        bool ignoreObstacles = false)
    {
        var open = new List<Node>();
        var closed = new HashSet<Vector2Int>();

        open.Add(new Node { pos = start, gCost = 0, hCost = Heuristic(start, goal) });

        while (open.Count > 0)
        {
            Node current = open[0];
            for (int i = 1; i < open.Count; i++)
            {
                if (open[i].fCost < current.fCost) current = open[i];
            }

            open.Remove(current);
            closed.Add(current.pos);

            if (current.pos == goal)
            {
                totalCost = current.gCost;

                return BuildPath(current);
            }

            CellData fromCell = grid[current.pos.x, current.pos.y];

            for (int d = 0; d < CellData.Directions.Length; d++)
            {
                Vector2Int delta     = CellData.Directions[d];
                Vector2Int neighbour = current.pos + delta;

                if (neighbour.x < 0 || neighbour.x >= gridWidth ||
                    neighbour.y < 0 || neighbour.y >= gridHeight) continue;
                if (closed.Contains(neighbour)) continue;
                if (!CanFit(grid, gridWidth, gridHeight, neighbour, unitSize, unitType)) continue;

                float slopeMultiplier =
                    ResolveSlopeMultiplier(fromCell, delta, unitType, out bool blocked);

                if (blocked) continue;

                // Charge every cell the step crosses; any blocked crossing kills the move.
                CellCrossing[] crossings = CellPathing.Crossings[d];
                float weightedCellCost   = 0f;
                bool  pathBlocked        = false;
                for (int c = 0; c < crossings.Length; c++)
                {
                    Vector2Int crossedPos = current.pos + crossings[c].offset;
                    if (crossedPos.x < 0 || crossedPos.x >= gridWidth ||
                        crossedPos.y < 0 || crossedPos.y >= gridHeight)
                    {
                        pathBlocked = true;
                        break;
                    }

                    CellData crossedCell = grid[crossedPos.x, crossedPos.y];

                    float biomeMultiplier = ResolveBiomeMultiplier(crossedCell, unitType, out bool biomeBlocked);
                    if (biomeBlocked) { pathBlocked = true; break; }

                    float obstacleMultiplier = ResolveObstacleMultiplier(crossedCell, unitType, out bool obstacleBlocked, ignoreObstacles);
                    if (obstacleBlocked) { pathBlocked = true; break; }

                    weightedCellCost += crossings[c].portion * biomeMultiplier * obstacleMultiplier;
                }
                if (pathBlocked) continue;

                float stepCost = CellPathing.StepLengths[d] * weightedCellCost * slopeMultiplier;
                float newG = current.gCost + stepCost;

                Node existing = open.Find(n => n.pos == neighbour);
                if (existing == null)
                {
                    open.Add(new Node
                    {
                        pos = neighbour,
                        gCost = newG,
                        hCost = Heuristic(neighbour, goal),
                        parent = current
                    });
                }
                else if (newG < existing.gCost)
                {
                    existing.gCost = newG;
                    existing.parent = current;
                }
            }
        }

        totalCost = 0f;

        return new List<Vector2Int>();
    }

    /// <summary>Converts an A* total cost into seconds of travel time. +Infinity when moveSpeed is non-positive.</summary>
    public static float CostToSeconds(float totalCost, float cellStep, float moveSpeed)
    {
        return moveSpeed > 0f ? totalCost * cellStep / moveSpeed : float.PositiveInfinity;
    }

    /// <summary>A* cost of walking an already-built path; +Infinity if any step is blocked. Thread-safe while the grid isn't mutated.</summary>
    public static float ComputePathCost(CellData[,] grid, int gridWidth, int gridHeight,
                                        List<Vector2Int> path, UnitTypeSO unitType,
                                        bool ignoreObstacles = false)
    {
        if (path == null || path.Count < 2) return 0f;

        float totalCost = 0f;
        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector2Int fromPos = path[i];
            Vector2Int delta   = path[i + 1] - fromPos;

            int dirIndex = GetDirectionIndex(delta);
            if (dirIndex < 0) return float.PositiveInfinity;

            if (fromPos.x < 0 || fromPos.x >= gridWidth ||
                fromPos.y < 0 || fromPos.y >= gridHeight) return float.PositiveInfinity;

            CellData fromCell = grid[fromPos.x, fromPos.y];

            float slopeMul = ResolveSlopeMultiplier(fromCell, delta, unitType, out bool slopeBlocked);
            if (slopeBlocked) return float.PositiveInfinity;

            CellCrossing[] crossings = CellPathing.Crossings[dirIndex];
            float weightedCellCost = 0f;
            for (int c = 0; c < crossings.Length; c++)
            {
                Vector2Int pos = fromPos + crossings[c].offset;
                if (pos.x < 0 || pos.x >= gridWidth ||
                    pos.y < 0 || pos.y >= gridHeight) return float.PositiveInfinity;

                CellData crossed = grid[pos.x, pos.y];
                float biomeMul = ResolveBiomeMultiplier(crossed, unitType, out bool biomeBlocked);
                if (biomeBlocked) return float.PositiveInfinity;
                float obstacleMul = ResolveObstacleMultiplier(crossed, unitType, out bool obstacleBlocked, ignoreObstacles);
                if (obstacleBlocked) return float.PositiveInfinity;
                weightedCellCost += crossings[c].portion * biomeMul * obstacleMul;
            }

            totalCost += CellPathing.StepLengths[dirIndex] * weightedCellCost * slopeMul;
        }
        return totalCost;
    }

    /// <summary>Slope multiplier for stepping from a cell in the given direction; blocked = true when the step is impossible.</summary>
    public static float ResolveSlopeMultiplier(
        CellData fromCell,
        Vector2Int delta,
        UnitTypeSO unitType,
        out bool blocked)
    {
        blocked = false;

        if (unitType == null) return 1f;
        if (fromCell.slopeOutgoing == null) return unitType.EvaluateSlope(0f, out blocked);

        int dirIndex = GetDirectionIndex(delta);

        if (dirIndex < 0) return unitType.EvaluateSlope(0f, out blocked);

        float slope = fromCell.slopeOutgoing[dirIndex];

        return unitType.EvaluateSlope(slope, out blocked);
    }

    /// <summary>Biome cost multiplier for entering the cell; blocked = true when entry is forbidden.</summary>
    public static float ResolveBiomeMultiplier(CellData cell, UnitTypeSO unitType, out bool blocked)
    {
        blocked = false;

        if (cell.biome == null) return 1f;

        CellEffectSpec resolved = cell.biome.ResolveEffect(unitType);

        switch (resolved.effect)
        {
            case CellEffect.Block:
                blocked = true;
                return 0f;
            case CellEffect.Slow:
                return Mathf.Max(0.0001f, resolved.costMultiplier);
            default:
                return 1f;
        }
    }

    /// <summary>Obstacle cost multiplier for entering the cell. Footprint and radius layers compose; the strongest radius source wins.</summary>
    public static float ResolveObstacleMultiplier(CellData cell, UnitTypeSO unitType, out bool blocked, bool ignoreObstacles = false)
    {
        blocked = false;
        if (ignoreObstacles) return 1f;

        float multiplier = 1f;

        if (cell.obstacle != null && cell.obstacle.obstacleSo != null)
        {
            CellEffectSpec resolved = cell.obstacle.obstacleSo.ResolveEffect(unitType);
            switch (resolved.effect)
            {
                case CellEffect.Block:
                    blocked = true;
                    return 0f;
                case CellEffect.Slow:
                    multiplier *= Mathf.Max(0.0001f, resolved.costMultiplier);
                    break;
            }
        }

        if (cell.radiusObstacles != null && cell.radiusObstacles.Count > 0)
        {
            CellEffectSpec strongest = default;
            bool hasAny = false;
            for (int i = 0; i < cell.radiusObstacles.Count; i++)
            {
                PlacedObstacle src = cell.radiusObstacles[i];
                if (src == null || src.obstacleSo == null) continue;
                CellEffectSpec spec = src.obstacleSo.ResolveRadiusEffect(unitType);
                if (!hasAny || IsStronger(spec, strongest))
                {
                    strongest = spec;
                    hasAny = true;
                }
            }

            if (hasAny)
            {
                switch (strongest.effect)
                {
                    case CellEffect.Block:
                        blocked = true;
                        return 0f;
                    case CellEffect.Slow:
                        multiplier *= Mathf.Max(0.0001f, strongest.costMultiplier);
                        break;
                }
            }
        }

        return multiplier;
    }

    private static bool IsStronger(CellEffectSpec a, CellEffectSpec b)
    {
        int rankA = EffectRank(a.effect);
        int rankB = EffectRank(b.effect);
        if (rankA != rankB) return rankA > rankB;
        if (a.effect == CellEffect.Slow) return a.costMultiplier > b.costMultiplier;
        return false;
    }

    private static int EffectRank(CellEffect e)
    {
        switch (e)
        {
            case CellEffect.Block: return 2;
            case CellEffect.Slow:  return 1;
            default:               return 0;
        }
    }

    /// <summary>Index of the delta in <see cref="CellData.Directions"/>, or -1 for invalid deltas.</summary>
    public static int GetDirectionIndex(Vector2Int delta)
    {
        for (int i = 0; i < CellData.Directions.Length; i++)
        {
            if (CellData.Directions[i] == delta) return i;
        }

        return -1;
    }

    private static bool CanFit(CellData[,] grid, int w, int h, Vector2Int pos, int unitSize, UnitTypeSO unitType)
    {
        int half = unitSize / 2;
        for (int dx = -half; dx <= half; dx++)
        for (int dy = -half; dy <= half; dy++)
        {
            int nx = pos.x + dx;
            int ny = pos.y + dy;

            if (nx < 0 || nx >= w || ny < 0 || ny >= h) return false;
            CellData cell = grid[nx, ny];

            if (cell.biome == null) return false;
            if (cell.biome.ResolveEffect(unitType).effect == CellEffect.Block) return false;
        }

        return true;
    }

    private static float Heuristic(Vector2Int a, Vector2Int b)
    {
        float dx = Mathf.Abs(a.x - b.x);
        float dy = Mathf.Abs(a.y - b.y);

        return dx + dy + (SQRT2 - 2f) * Mathf.Min(dx, dy);
    }

    private static List<Vector2Int> BuildPath(Node endNode)
    {
        var path = new List<Vector2Int>();
        Node current = endNode;
        while (current != null)
        {
            path.Add(current.pos);
            current = current.parent;
        }

        path.Reverse();

        return path;
    }
}