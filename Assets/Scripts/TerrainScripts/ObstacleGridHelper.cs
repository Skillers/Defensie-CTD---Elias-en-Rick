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
}