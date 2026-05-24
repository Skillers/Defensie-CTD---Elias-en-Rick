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
        UnitTypeSO unitType = null)
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

            foreach ((Vector2Int neighbour, bool isDiagonal) in GetNeighbours(current.pos, gridWidth, gridHeight))
            {
                if (closed.Contains(neighbour)) continue;
                if (!CanFit(grid, gridWidth, gridHeight, neighbour, unitSize)) continue;

                float slopeMultiplier =
                    ResolveSlopeMultiplier(fromCell, neighbour - current.pos, unitType, out bool blocked);

                if (blocked) continue;

                CellData cell = grid[neighbour.x, neighbour.y];
                int moveCost = cell.biome != null ? cell.biome.GetMovementCost(unitType) : 3;

                float obstacleMultiplier = ResolveObstacleMultiplier(cell, unitType, out bool obstacleBlocked);
                if (obstacleBlocked) continue;

                float effectiveCost = moveCost * obstacleMultiplier;
                float stepCost = (isDiagonal ? SQRT2 * effectiveCost : effectiveCost) * slopeMultiplier;
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
    ///     Returns the obstacle cost multiplier for entering <paramref name="cell" /> as the given
    ///     <paramref name="unitType" />. Sets <paramref name="blocked" /> to true if the resolved
    ///     effect forbids entry. Returns 1f when no obstacle is registered or the resolved effect
    ///     has no cost impact.
    /// </summary>
    public static float ResolveObstacleMultiplier(CellData cell, UnitTypeSO unitType, out bool blocked)
    {
        blocked = false;

        if (cell.obstacle == null) return 1f;

        ObstacleSO obstacleSo = cell.obstacle.obstacleSo;
        if (obstacleSo == null) return 1f;

        ObstacleUnitEffect resolved = obstacleSo.ResolveEffect(unitType);

        switch (resolved.effect)
        {
            case ObstacleEffect.Block:
                blocked = true;
                return 0f;
            case ObstacleEffect.Slow:
                return Mathf.Max(0.0001f, resolved.costMultiplier);
            default:
                return 1f;
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

    private static bool CanFit(CellData[,] grid, int w, int h, Vector2Int pos, int unitSize)
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
            if (cell.biome.defaultMovementCost == int.MaxValue) return false;
        }

        return true;
    }

    private static float Heuristic(Vector2Int a, Vector2Int b)
    {
        float dx = Mathf.Abs(a.x - b.x);
        float dy = Mathf.Abs(a.y - b.y);

        return dx + dy + (SQRT2 - 2f) * Mathf.Min(dx, dy);
    }

    private static IEnumerable<(Vector2Int pos, bool isDiagonal)> GetNeighbours(Vector2Int pos, int w, int h)
    {
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;

            var n = new Vector2Int(pos.x + dx, pos.y + dy);
            if (n.x >= 0 && n.x < w && n.y >= 0 && n.y < h)
            {
                yield return (n, dx != 0 && dy != 0);
            }
        }
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