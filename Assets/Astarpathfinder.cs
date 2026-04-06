using System.Collections.Generic;
using UnityEngine;

public static class AStarPathfinder
{
    const float SQRT2 = 1.41421356f;

    class Node
    {
        public Vector2Int pos;
        public float gCost;
        public float hCost;
        public float fCost => gCost + hCost;
        public Node  parent;
    }

    /// <summary>
    /// Returns a list of grid positions from start to goal (inclusive).
    /// Returns empty list if no path found.
    /// Supports 8-directional movement; diagonal steps cost sqrt(2) * terrain cost.
    /// unitSize: the squad footprint in cells (e.g. 5 = 5x5).
    /// unitType: optional unit type for resolving per-biome movement costs.
    /// </summary>
    public static List<Vector2Int> FindPath(BiomeCell[,] grid, int gridWidth, int gridHeight,
                                            Vector2Int start, Vector2Int goal,
                                            int unitSize = 5, UnitTypeSO unitType = null)
    {
        var open   = new List<Node>();
        var closed = new HashSet<Vector2Int>();

        open.Add(new Node { pos = start, gCost = 0, hCost = Heuristic(start, goal) });

        while (open.Count > 0)
        {
            Node current = open[0];
            for (int i = 1; i < open.Count; i++)
                if (open[i].fCost < current.fCost) current = open[i];

            open.Remove(current);
            closed.Add(current.pos);

            if (current.pos == goal)
                return BuildPath(current);

            foreach (var (neighbour, isDiagonal) in GetNeighbours(current.pos, gridWidth, gridHeight))
            {
                if (closed.Contains(neighbour)) continue;
                if (!CanFit(grid, gridWidth, gridHeight, neighbour, unitSize)) continue;

                var cell = grid[neighbour.x, neighbour.y];
                int moveCost = cell?.biome != null ? cell.biome.GetMovementCost(unitType) : 3;

                float stepCost = isDiagonal ? SQRT2 * moveCost : moveCost;
                float newG     = current.gCost + stepCost;

                Node existing = open.Find(n => n.pos == neighbour);
                if (existing == null)
                {
                    open.Add(new Node
                    {
                        pos    = neighbour,
                        gCost  = newG,
                        hCost  = Heuristic(neighbour, goal),
                        parent = current
                    });
                }
                else if (newG < existing.gCost)
                {
                    existing.gCost  = newG;
                    existing.parent = current;
                }
            }
        }

        return new List<Vector2Int>();
    }

    static bool CanFit(BiomeCell[,] grid, int w, int h, Vector2Int pos, int unitSize)
    {
        int half = unitSize / 2;
        for (int dx = -half; dx <= half; dx++)
        for (int dy = -half; dy <= half; dy++)
        {
            int nx = pos.x + dx;
            int ny = pos.y + dy;
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) return false;
            var cell = grid[nx, ny];
            if (cell?.biome == null) return false;
            if (cell.biome.defaultMovementCost == int.MaxValue) return false;
        }
        return true;
    }

    static float Heuristic(Vector2Int a, Vector2Int b)
    {
        float dx = Mathf.Abs(a.x - b.x);
        float dy = Mathf.Abs(a.y - b.y);
        return (dx + dy) + (SQRT2 - 2f) * Mathf.Min(dx, dy);
    }

    static IEnumerable<(Vector2Int pos, bool isDiagonal)> GetNeighbours(Vector2Int pos, int w, int h)
    {
        for (int dx = -1; dx <= 1; dx++)
        for (int dy = -1; dy <= 1; dy++)
        {
            if (dx == 0 && dy == 0) continue;

            var n = new Vector2Int(pos.x + dx, pos.y + dy);
            if (n.x >= 0 && n.x < w && n.y >= 0 && n.y < h)
                yield return (n, dx != 0 && dy != 0);
        }
    }

    static List<Vector2Int> BuildPath(Node endNode)
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
