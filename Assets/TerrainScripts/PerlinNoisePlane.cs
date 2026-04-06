using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PerlinNoisePlane : MonoBehaviour
{
    public TerrainConfigHolder configHolder;
    public PlaneConfig config => configHolder?.config;

    // 2 vertices per unit = 0.5 step
    private const float Step      = 0.5f;
    private const float RoundStep = 0.25f;

    // Fired after every generation — MarchingCubesTerrain listens to this
    public event System.Action OnGenerated;

    // Stored noise values [0..1] per vertex, row-major (z * vertsX + x)
    public float[] NoiseValues { get; private set; }
    public int VertsX { get; private set; }
    public int VertsZ { get; private set; }

    // Get the noise value at grid coordinates
    public float GetValue(int x, int z) => NoiseValues[z * VertsX + x];

    // Override a single point and immediately refresh the mesh + texture
    public void SetValue(int x, int z, float value)
    {
        NoiseValues[z * VertsX + x] = value;
        RebuildFromStoredValues();
    }

    // Generation is driven by MapGenerator — no auto-start.

    [ContextMenu("Regenerate")]
    public void Generate()
    {
        if (configHolder == null) { Debug.LogError("PerlinNoisePlane: no TerrainConfigHolder assigned."); return; }
        if (config == null) { Debug.LogError("PerlinNoisePlane: TerrainConfigHolder has no PlaneConfig."); return; }

        int stepsX = Mathf.RoundToInt(config.extentX * 2f / Step);
        int stepsZ = Mathf.RoundToInt(config.extentZ * 2f / Step);
        int vertsX = stepsX + 1;
        int vertsZ = stepsZ + 1;

        VertsX = vertsX;
        VertsZ = vertsZ;
        NoiseValues = SampleNoise(vertsX, vertsZ);
        float[] noise = NoiseValues;

        var vertices = new Vector3[vertsX * vertsZ];
        var uvs      = new Vector2[vertsX * vertsZ];

        for (int z = 0; z < vertsZ; z++)
        for (int x = 0; x < vertsX; x++)
        {
            int i    = z * vertsX + x;
            float wx = -config.extentX + x * Step;
            float wz = -config.extentZ + z * Step;
            vertices[i] = new Vector3(wx, 0f, wz);
            uvs[i]      = new Vector2((float)x / stepsX, (float)z / stepsZ);
        }

        var triangles = new int[stepsX * stepsZ * 6];
        int t = 0;
        for (int z = 0; z < stepsZ; z++)
        for (int x = 0; x < stepsX; x++)
        {
            int bl = z * vertsX + x;
            triangles[t++] = bl;
            triangles[t++] = bl + vertsX;
            triangles[t++] = bl + 1;
            triangles[t++] = bl + 1;
            triangles[t++] = bl + vertsX;
            triangles[t++] = bl + vertsX + 1;
        }

        var mesh = new Mesh { name = "PerlinPlane" };
        if (vertsX * vertsZ > 65535)
            mesh.indexFormat = IndexFormat.UInt32;

        mesh.vertices  = vertices;
        mesh.uv        = uvs;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();

        GetComponent<MeshFilter>().mesh = mesh;

        var mr     = GetComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (mr.sharedMaterial == null)
            mr.sharedMaterial = new Material(shader);

        ApplyNoiseToTexture(noise, vertsX, vertsZ);
        OnGenerated?.Invoke();
    }

    // Called by PlaneConfig.OnValidate — updates heights and texture without rebuilding triangles
    public void ApplyNoise()
    {
        if (config == null) return;
        var mesh = GetComponent<MeshFilter>().sharedMesh;
        if (mesh == null) { Generate(); return; }

        int vertsX = Mathf.RoundToInt(config.extentX * 2f / Step) + 1;
        int vertsZ = Mathf.RoundToInt(config.extentZ * 2f / Step) + 1;

        if (mesh.vertexCount != vertsX * vertsZ) { Generate(); return; }

        VertsX = vertsX;
        VertsZ = vertsZ;
        NoiseValues = SampleNoise(vertsX, vertsZ);

        ApplyNoiseToTexture(NoiseValues, vertsX, vertsZ);
        OnGenerated?.Invoke();
    }

    // Kept for backwards compatibility
    public void ApplyNoiseTexture() => ApplyNoise();

    // Applies whatever is currently in NoiseValues to the mesh and texture
    public void RebuildFromStoredValues()
    {
        if (NoiseValues == null) return;
        ApplyNoiseToTexture(NoiseValues, VertsX, VertsZ);
        OnGenerated?.Invoke();
    }

    // Returns a flat array of Perlin values [0..1] for the given grid
    private float[] SampleNoise(int vertsX, int vertsZ)
    {
        var   rng   = new System.Random(config.seed);
        float seedX = (float)(rng.NextDouble() * 10000.0);
        float seedZ = (float)(rng.NextDouble() * 10000.0);

        var noise = new float[vertsX * vertsZ];
        for (int z = 0; z < vertsZ; z++)
        for (int x = 0; x < vertsX; x++)
        {
            float wx = -config.extentX + x * Step;
            float wz = -config.extentZ + z * Step;
            noise[z * vertsX + x] = Mathf.PerlinNoise(
                (wx + config.noiseOffset.x) * config.noiseScale + seedX,
                (wz + config.noiseOffset.y) * config.noiseScale + seedZ);
        }
        return noise;
    }

    private void ApplyNoiseToTexture(float[] noise, int vertsX, int vertsZ)
    {
        var tex = new Texture2D(vertsX, vertsZ, TextureFormat.RGB24, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp
        };

        // Round to the same 0.25 steps MC uses so colors visually match terrain heights
        var pixels = new Color[noise.Length];
        for (int i = 0; i < noise.Length; i++)
        {
            float v = Mathf.Round(noise[i] / RoundStep) * RoundStep;
            pixels[i] = new Color(v, v, v);
        }

        tex.SetPixels(pixels);
        tex.Apply();

        GetComponent<MeshRenderer>().sharedMaterial.mainTexture = tex;
    }
}
