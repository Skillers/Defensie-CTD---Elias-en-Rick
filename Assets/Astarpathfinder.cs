using System.Collections.Generic;
using UnityEngine;

public static class AStarPathfinder
{
    const float SQRT2 = 1.41421356f;

    class Node
    {
        public Vector2Int pos;
        public float gCost;          // float: diagonal steps cost √2 × terrainCost
        public float hCost;
        public float fCost => gCost + hCost;
        public Node  parent;
    }

    /// <summary>
    /// Returns a list of grid positions from start to goal (inclusive).
    /// Returns empty list if no path found.
    /// Supports 8-directional movement; diagonal steps cost √2 × terrain cost.
    /// unitSize: the squad footprint in cells (e.g. 5 = 5x5). Every candidate cell
    /// is rejected unless the full footprint fits without hitting an impassable tile.
    /// </summary>
    public static List<Vector2Int> FindPath(TerrainCell[,] grid, int gridWidth, int gridHeight,
                                            Vector2Int start, Vector2Int goal, int unitSize = 5)
    {
        var open   = new List<Node>();
        var closed = new HashSet<Vector2Int>();

        open.Add(new Node { pos = start, gCost = 0, hCost = Heuristic(start, goal) });

        while (open.Count > 0)
        {
            // Pick lowest fCost
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

                // Reject if the squad's full footprint doesn't fit at this cell
                if (!CanFit(grid, gridWidth, gridHeight, neighbour, unitSize)) continue;

                TerrainCell cell = grid[neighbour.x, neighbour.y];

                // Diagonal steps travel √2 further, so cost is √2 × terrain cost
                float stepCost = isDiagonal ? SQRT2 * cell.movementCost : cell.movementCost;
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

        return new List<Vector2Int>(); // no path
    }

    // Returns true only if every cell in the unitSize×unitSize footprint centred on
    // pos is in bounds and passable. Movement cost is still read from the centre cell.
    static bool CanFit(TerrainCell[,] grid, int w, int h, Vector2Int pos, int unitSize)
    {
        int half = unitSize / 2;
        for (int dx = -half; dx <= half; dx++)
        for (int dy = -half; dy <= half; dy++)
        {
            int nx = pos.x + dx;
            int ny = pos.y + dy;
            if (nx < 0 || nx >= w || ny < 0 || ny >= h) return false;
            if (grid[nx, ny].movementCost == int.MaxValue) return false;
        }
        return true;
    }

    // Octile distance — correct heuristic for 8-directional grids where
    // cardinal cost = 1 and diagonal cost = √2.
    static float Heuristic(Vector2Int a, Vector2Int b)
    {
        float dx = Mathf.Abs(a.x - b.x);
        float dy = Mathf.Abs(a.y - b.y);
        return (dx + dy) + (SQRT2 - 2f) * Mathf.Min(dx, dy);
    }

    // Returns each neighbour paired with whether the step is diagonal.
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
