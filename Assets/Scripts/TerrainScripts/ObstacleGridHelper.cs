using UnityEngine;

public class ObstacleGridHelper
{
    public Bounds GetPartLocalBounds(GameObject part, Vector3 refPosition, Quaternion refRotation)
    {
        Vector3[] worldCorners = GetPartWorldObbCorners(part);

        if (worldCorners == null) return new Bounds();

        Bounds localBounds = new();
        Quaternion invRot = Quaternion.Inverse(refRotation);
        bool first = true;

        foreach (Vector3 wc in worldCorners)
        {
            Vector3 lc = invRot * (wc - refPosition);
            if (first)
            {
                localBounds = new Bounds(lc, Vector3.zero);
                first = false;
            }
            else
            {
                localBounds.Encapsulate(lc);
            }
        }

        return localBounds;
    }

    private Vector3[] GetPartWorldObbCorners(GameObject part)
    {
        // 1. BoxCollider
        var boxCol = part.GetComponent<BoxCollider>();
        if (boxCol != null)
        {
            Vector3 c = boxCol.center;
            Vector3 e = boxCol.size * 0.5f;

            return GetTransformedCorners(part.transform, c, e);
        }

        // 2. SphereCollider
        var sphereCol = part.GetComponent<SphereCollider>();
        if (sphereCol != null)
        {
            Vector3 c = sphereCol.center;
            Vector3 e = Vector3.one * sphereCol.radius;

            return GetTransformedCorners(part.transform, c, e);
        }

        // 3. CapsuleCollider
        var capsuleCol = part.GetComponent<CapsuleCollider>();
        if (capsuleCol != null)
        {
            Vector3 c = capsuleCol.center;
            float radius = capsuleCol.radius;
            float h = capsuleCol.height * 0.5f;
            Vector3 e = Vector3.one * radius;
            if (capsuleCol.direction == 0)
            {
                e.x = h;
            }
            else if (capsuleCol.direction == 1)
            {
                e.y = h;
            }
            else if (capsuleCol.direction == 2) e.z = h;

            return GetTransformedCorners(part.transform, c, e);
        }

        // 4. MeshFilter
        var meshFilter = part.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            Bounds b = meshFilter.sharedMesh.bounds;

            return GetTransformedCorners(part.transform, b.center, b.extents);
        }

        // Fallback to world bounds from Renderer or Collider
        var fallbackBounds = new Bounds();
        bool hasFallback = false;

        var partRenderer = part.GetComponent<Renderer>();
        if (partRenderer != null)
        {
            fallbackBounds = partRenderer.bounds;
            hasFallback = true;
        }
        else
        {
            var col = part.GetComponent<Collider>();
            if (col != null)
            {
                fallbackBounds = col.bounds;
                hasFallback = true;
            }
        }

        if (hasFallback)
        {
            return GetBoundsCorners(fallbackBounds);
        }

        return null;
    }

    private static Vector3[] GetTransformedCorners(Transform t, Vector3 center, Vector3 extents)
    {
        Vector3[] localCorners =
        {
            new(center.x + extents.x, center.y + extents.y, center.z + extents.z),
            new(center.x + extents.x, center.y + extents.y, center.z - extents.z),
            new(center.x + extents.x, center.y - extents.y, center.z + extents.z),
            new(center.x + extents.x, center.y - extents.y, center.z - extents.z),
            new(center.x - extents.x, center.y + extents.y, center.z + extents.z),
            new(center.x - extents.x, center.y + extents.y, center.z - extents.z),
            new(center.x - extents.x, center.y - extents.y, center.z + extents.z),
            new(center.x - extents.x, center.y - extents.y, center.z - extents.z)
        };
        var worldCorners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            worldCorners[i] = t.TransformPoint(localCorners[i]);
        }

        return worldCorners;
    }

    public static Quaternion ExtractYawRotation(Quaternion rot)
    {
        Vector3 forward = rot * Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.001f)
        {
            forward = rot * Vector3.up;
            forward.y = 0f;
        }

        if (forward.sqrMagnitude > 0.001f)
        {
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        return Quaternion.identity;
    }

    public static Vector3[] GetBoundsCorners(Bounds b)
    {
        Vector3 c = b.center;
        Vector3 e = b.extents;

        return new Vector3[]
        {
            new(c.x + e.x, c.y + e.y, c.z + e.z),
            new(c.x + e.x, c.y + e.y, c.z - e.z),
            new(c.x + e.x, c.y - e.y, c.z + e.z),
            new(c.x + e.x, c.y - e.y, c.z - e.z),
            new(c.x - e.x, c.y + e.y, c.z + e.z),
            new(c.x - e.x, c.y + e.y, c.z - e.z),
            new(c.x - e.x, c.y - e.y, c.z + e.z),
            new(c.x - e.x, c.y - e.y, c.z - e.z)
        };
    }

    /// <summary>
    /// Registers extra cells between a part and its nearest forward/backward neighbour
    /// by stepping along the part's local Z-axis in grid-step increments.
    /// Call this after the normal per-part registration pass.
    /// </summary>
    public static void FillSegmentGapCells(
        GameObject part,
        TerrainDataStore store,
        PlacedObstacle po)
    {
        Transform parent = part.transform.parent;
        if (parent == null) return;

        Vector3 partPos = part.transform.position;
        Vector3 forward = part.transform.forward;

        // Collect siblings with their signed projection along local Z
        var siblings = new System.Collections.Generic.List<(Transform t, float proj)>();
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform sibling = parent.GetChild(i);
            if (sibling.gameObject == part) continue;
            float proj = Vector3.Dot(sibling.position - partPos, forward);
            siblings.Add((sibling, proj));
        }

        if (siblings.Count == 0) return;

        // Closest forward neighbour
        float closestFwdProj = float.MaxValue;
        Transform fwdNeighbour = null;
        // Closest backward neighbour
        float closestBwdProj = float.MaxValue;
        Transform bwdNeighbour = null;

        foreach (var (t, proj) in siblings)
        {
            if (proj > 0f && proj < closestFwdProj) { closestFwdProj = proj; fwdNeighbour = t; }
            if (proj < 0f && -proj < closestBwdProj) { closestBwdProj = -proj; bwdNeighbour = t; }
        }

        Vector2Int fromCell = store.WorldToGrid(partPos);

        if (fwdNeighbour != null)
        {
            // Fill only up to the midpoint cell
            Vector2Int toCell = store.WorldToGrid(
                Vector3.Lerp(partPos, fwdNeighbour.position, 0.5f));
            BresenhamFill(fromCell, toCell, store, po);
        }

        if (bwdNeighbour != null)
        {
            Vector2Int toCell = store.WorldToGrid(
                Vector3.Lerp(partPos, bwdNeighbour.position, 0.5f));
            BresenhamFill(fromCell, toCell, store, po);
        }
    }

    private static void BresenhamFill(
        Vector2Int from,
        Vector2Int to,
        TerrainDataStore store,
        PlacedObstacle po)
    {
        // Integer Bresenham — visits every grid cell on the line, no gaps
        int x = from.x, z = from.y;
        int dx = Mathf.Abs(to.x - from.x), dz = Mathf.Abs(to.y - from.y);
        int sx = from.x < to.x ? 1 : -1;
        int sz = from.y < to.y ? 1 : -1;
        int err = dx - dz;

        while (true)
        {
            if (store.InBounds(x, z))
            {
                var cell = new Vector2Int(x, z);
                if (!po.affectedCells.Contains(cell))
                {
                    store.grid[x, z].obstacle = po;
                    po.affectedCells.Add(cell);
                }
            }

            if (x == to.x && z == to.y) break;

            int e2 = 2 * err;
            if (e2 > -dz) { err -= dz; x += sx; }
            if (e2 <  dx) { err += dx; z += sz; }
        }
    }

    private static void FillAlongAxis(
        Vector3 origin,
        Vector3 dir,
        float maxDist,
        float gridStep,
        TerrainDataStore store,
        PlacedObstacle po)
    {
        if (maxDist <= 0f) return;

        for (float d = gridStep; d <= maxDist + gridStep * 0.5f; d += gridStep)
        {
            Vector3 worldPos = origin + dir * d;
            Vector2Int cell = store.WorldToGrid(worldPos);

            if (!store.InBounds(cell.x, cell.y)) continue;
            if (po.affectedCells.Contains(cell)) continue;

            store.grid[cell.x, cell.y].obstacle = po;
            po.affectedCells.Add(cell);
        }
    }
}