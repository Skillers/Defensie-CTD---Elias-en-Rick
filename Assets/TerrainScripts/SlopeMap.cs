using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SlopeMap : MonoBehaviour
{
    public PerlinNoisePlane    noisePlane;
    public TerrainConfigHolder configHolder;
    private PlaneConfig config => configHolder?.config;

    private const float Step      = 0.5f;
    private const float RoundStep = 0.25f;

    // Per-node slope magnitude in degrees [0..90] — used for visualisation
    public float[] SlopeAngles { get; private set; }
    // Raw heights — used for directional slope queries
    public float[] Heights     { get; private set; }
    public int     VertsX      { get; private set; }
    public int     VertsZ      { get; private set; }

    // Magnitude only — how steep is this node (no direction)
    public float GetSlope(int x, int z) => SlopeAngles[z * VertsX + x];

    // Signed slope in degrees from node (x1,z1) → (x2,z2)
    // Positive = uphill, Negative = downhill
    public float GetDirectionalSlope(int x1, int z1, int x2, int z2)
    {
        float h1       = Heights[z1 * VertsX + x1];
        float h2       = Heights[z2 * VertsX + x2];
        float dx       = (x2 - x1) * Step;
        float dz       = (z2 - z1) * Step;
        float distance = Mathf.Sqrt(dx * dx + dz * dz);
        return Mathf.Atan2(h2 - h1, distance) * Mathf.Rad2Deg;
    }

    // Generation is driven by MapGenerator — no auto-subscribe or auto-start.

    public void Generate()
    {
        if (noisePlane == null || noisePlane.NoiseValues == null) return;

        VertsX = noisePlane.VertsX;
        VertsZ = noisePlane.VertsZ;

        Heights     = BuildHeights();
        SlopeAngles = ComputeSlopes(Heights);

        BuildMesh();
        ApplySlopeTexture(SlopeAngles);
    }

    // Raw heights (no rounding) so slope gradients are smooth
    private float[] BuildHeights()
    {
        float heightMult = config.heightMultiplier;
        var   heights    = new float[VertsX * VertsZ];

        for (int z = 0; z < VertsZ; z++)
        for (int x = 0; x < VertsX; x++)
            heights[z * VertsX + x] = noisePlane.GetValue(x, z) * heightMult;

        return heights;
    }

    // Central-difference gradient → slope angle in degrees per node
    private float[] ComputeSlopes(float[] heights)
    {
        var slopes = new float[VertsX * VertsZ];

        for (int z = 0; z < VertsZ; z++)
        for (int x = 0; x < VertsX; x++)
        {
            float h     = heights[z * VertsX + x];
            float left  = x > 0          ? heights[ z      * VertsX + (x - 1)] : h;
            float right = x < VertsX - 1 ? heights[ z      * VertsX + (x + 1)] : h;
            float down  = z > 0          ? heights[(z - 1) * VertsX +  x      ] : h;
            float up    = z < VertsZ - 1 ? heights[(z + 1) * VertsX +  x      ] : h;

            float dX = (right - left) / (2f * Step);
            float dZ = (up    - down) / (2f * Step);

            slopes[z * VertsX + x] = Mathf.Atan(Mathf.Sqrt(dX * dX + dZ * dZ)) * Mathf.Rad2Deg;
        }
        return slopes;
    }

    private void BuildMesh()
    {
        int stepsX = VertsX - 1;
        int stepsZ = VertsZ - 1;

        var vertices  = new Vector3[VertsX * VertsZ];
        var uvs       = new Vector2[VertsX * VertsZ];

        for (int z = 0; z < VertsZ; z++)
        for (int x = 0; x < VertsX; x++)
        {
            int i = z * VertsX + x;
            vertices[i] = new Vector3(-config.extentX + x * Step, 0f, -config.extentZ + z * Step);
            uvs[i]      = new Vector2((float)x / stepsX, (float)z / stepsZ);
        }

        var triangles = new int[stepsX * stepsZ * 6];
        int t = 0;
        for (int z = 0; z < stepsZ; z++)
        for (int x = 0; x < stepsX; x++)
        {
            int bl = z * VertsX + x;
            triangles[t++] = bl;
            triangles[t++] = bl + VertsX;
            triangles[t++] = bl + 1;
            triangles[t++] = bl + 1;
            triangles[t++] = bl + VertsX;
            triangles[t++] = bl + VertsX + 1;
        }

        var mesh = new Mesh { name = "SlopeMap" };
        if (VertsX * VertsZ > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices  = vertices;
        mesh.uv        = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        GetComponent<MeshFilter>().mesh = mesh;

        var mr     = GetComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (mr.sharedMaterial == null)
            mr.sharedMaterial = new Material(shader);
    }

    private void ApplySlopeTexture(float[] slopes)
    {
        float maxAngle = 0f;
        foreach (float s in slopes)
            if (s > maxAngle) maxAngle = s;
        if (maxAngle < 0.001f) maxAngle = 1f;

        var tex = new Texture2D(VertsX, VertsZ, TextureFormat.RGB24, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp
        };

        var pixels = new Color[slopes.Length];
        for (int i = 0; i < slopes.Length; i++)
        {
            float t = Mathf.Clamp01(slopes[i] / maxAngle);
            pixels[i] = Color.Lerp(Color.white, Color.red, t);
        }

        tex.SetPixels(pixels);
        tex.Apply();

        GetComponent<MeshRenderer>().sharedMaterial.mainTexture = tex;
    }
}
