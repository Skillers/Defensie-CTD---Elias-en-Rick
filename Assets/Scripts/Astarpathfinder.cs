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

    /// <summary>
    ///     Returns a list of grid positions from start to goal (inclusive).
    ///     Returns empty list if no path found.
    ///     <paramref name="totalCost" /> is set to the goal node's accumulated A* gCost on success,
    ///     or 0 when no path exists. Multiply by cell step and divide by move speed to get seconds —
    ///     see <see cref="CostToSeconds" />.
    ///     Supports 8-directional movement; diagonal steps cost sqrt(2) * terrain cost.
    ///     Step cost is also multiplied by the unit's slope rule for the outgoing direction
    ///     of the cell being stepped from. A blocked slope rule prunes the neighbour entirely.
    ///     unitSize: the squad footprint in cells (e.g. 5 = 5x5).
    ///     unitType: optional unit type for resolving per-biome movement costs and slope rules.
    /// </summary>
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

                // Walk every cell the step physically crosses (excluding the start).
                // For cardinal / single-diagonal that's just the destination; for
                // knight moves it's the two intermediates plus the destination,
                // each charged 1/3 of the step. Any blocked crossing kills the move.
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

    /// <summary>
    ///     Converts an A* total cost (sum of per-step weighted cell distances) into seconds
    ///     of travel time for a unit moving at <paramref name="moveSpeed" /> world units per second
    ///     on a grid with <paramref name="cellStep" /> world units between adjacent cells.
    ///     Returns +infinity when moveSpeed is non-positive.
    /// </summary>
    public static float CostToSeconds(float totalCost, float cellStep, float moveSpeed)
    {
        return moveSpeed > 0f ? totalCost * cellStep / moveSpeed : float.PositiveInfinity;
    }

    /// <summary>
    ///     Computes the total A* cost of walking an already-built <paramref name="path" />.
    ///     Mirrors <see cref="FindPath" />'s per-step formula:
    ///         stepCost = StepLengths[d] × Σ(portion × biomeMul × obstacleMul) × slopeMul
    ///     summed across all crossed cells (cardinal/diagonal: destination only; knight:
    ///     two intermediates + destination, each ⅓). Returns +infinity if any step is
    ///     blocked (slope/biome/obstacle) or crosses off-grid. Useful for re-pricing a
    ///     path that's been mutated by shortcut / bevel passes after the initial A* ran.
    ///     Safe to call on a worker thread as long as the grid isn't being mutated.
    /// </summary>
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

    /// <summary>
    ///     Returns the slope multiplier for stepping from <paramref name="fromCell" /> in the given delta direction.
    ///     Sets <paramref name="blocked" /> to true if the unit cannot make this step.
    ///     Falls back to 1f when no unit type is supplied or slope data is missing.
    /// </summary>
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

    /// <summary>
    ///     Returns the biome cost multiplier for entering <paramref name="cell" /> as the given
    ///     <paramref name="unitType" />. Sets <paramref name="blocked" /> to true if the resolved
    ///     biome effect forbids entry. Returns 1f when the cell has no biome or the resolved
    ///     effect has no cost impact.
    /// </summary>
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

    /// <summary>
    ///     Returns the obstacle cost multiplier for entering <paramref name="cell" /> as the given
    ///     <paramref name="unitType" />. Sets <paramref name="blocked" /> to true if the resolved
    ///     effect forbids entry. Returns 1f when no obstacle is registered or the resolved effect
    ///     has no cost impact. Composes footprint and radius layers multiplicatively; among
    ///     overlapping radius sources, the strongest single effect contributes.
    ///     When <paramref name="ignoreObstacles" /> is true, the entire obstacle layer (footprint
    ///     and radius) is skipped: returns 1f with blocked=false. Used to derive the obstacle-free
    ///     time estimate that catch-up A* rerouting approximates at runtime.
    /// </summary>
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

    /// <summary>
    ///     Maps an 8-neighbour delta (each component in {-1, 0, 1}, not both zero) to the
    ///     index in <see cref="CellData.Directions" />. Returns -1 for invalid deltas.
    /// </summary>
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