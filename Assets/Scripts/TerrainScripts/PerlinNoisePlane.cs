using UnityEngine;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PerlinNoisePlane : MonoBehaviour
{
    public TerrainDataStore terrainDataStore;

    // Stored noise values [0..1] per vertex, in a flat 2d array (z * vertsX + x)
    public float[] NoiseValues { get; private set; }
    public int VertsX { get; private set; }
    public int VertsZ { get; private set; }

    public float GetValue(int x, int z) => NoiseValues[z * VertsX + x];

    public bool GenerateNoise()
    {
        if (terrainDataStore == null) { Debug.LogError("PerlinNoisePlane: no TerrainDataStore assigned."); return false; }

        int stepsX = Mathf.RoundToInt(terrainDataStore.extentX * 2f / terrainDataStore.step);
        int stepsZ = Mathf.RoundToInt(terrainDataStore.extentZ * 2f / terrainDataStore.step);

        VertsX = stepsX + 1;
        VertsZ = stepsZ + 1;
        NoiseValues = SampleNoise(VertsX, VertsZ);

        return true;
    }

    public void BuildVisualSmooth()
    {
        if (NoiseValues == null || terrainDataStore == null) return;

        var mf = GetComponent<MeshFilter>();
        mf.sharedMesh = BuildQuad();

        var tex = new Texture2D(VertsX, VertsZ, TextureFormat.RGB24, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp
        };

        var pixels = new Color[NoiseValues.Length];
        for (int i = 0; i < NoiseValues.Length; i++)
        {
            float v = NoiseValues[i];
            pixels[i] = new Color(v, v, v);
        }

        tex.SetPixels(pixels);
        tex.Apply();

        var mr = GetComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (mr.sharedMaterial == null)
            mr.sharedMaterial = new Material(shader);

        mr.sharedMaterial.mainTexture = tex;
    }

    [Header("Visual Settings")]
    [Range(0.05f, 1f)]
    public float heightStepFraction = 0.4f;

    public void BuildVisual()
    {
        if (terrainDataStore == null) return;
        if (terrainDataStore.grid == null)
        {
            Debug.LogWarning("PerlinNoisePlane.BuildVisual: grid is null — call after TerrainDataStore.SetGrid().");
            return;
        }

        int w = terrainDataStore.GridWidth;
        int h = terrainDataStore.GridHeight;

        var mf = GetComponent<MeshFilter>();
        mf.sharedMesh = BuildQuad();

        // Number of distinct height bands for the strategic map look
        int layers = Mathf.Max(1, Mathf.RoundToInt(terrainDataStore.heightMultiplier * heightStepFraction));
        float mapStep = terrainDataStore.heightMultiplier / layers;
        float heightMult = Mathf.Max(0.0001f, terrainDataStore.heightMultiplier);

        var tex = new Texture2D(w, h, TextureFormat.RGB24, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode   = TextureWrapMode.Clamp
        };

        var pixels = new Color[w * h];
        for (int z = 0; z < h; z++)
        for (int x = 0; x < w; x++)
        {
            float raw = terrainDataStore.grid[x, z].rawHeight;
            float rounded = Mathf.Round(raw / mapStep) * mapStep;
            float v = Mathf.Clamp01(rounded / heightMult);
            pixels[z * w + x] = new Color(v, v, v);
        }

        tex.SetPixels(pixels);
        tex.Apply();

        var mr = GetComponent<MeshRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        if (mr.sharedMaterial == null)
            mr.sharedMaterial = new Material(shader);

        mr.sharedMaterial.mainTexture = tex;
    }

    Mesh BuildQuad()
    {
        float extX = terrainDataStore.extentX;
        float extZ = terrainDataStore.extentZ;

        var mesh = new Mesh { name = "PerlinQuad" };
        mesh.vertices = new Vector3[]
        {
            new Vector3(-extX, 0f, -extZ),
            new Vector3( extX, 0f, -extZ),
            new Vector3( extX, 0f,  extZ),
            new Vector3(-extX, 0f,  extZ),
        };
        mesh.uv = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(1f, 1f),
            new Vector2(0f, 1f),
        };
        mesh.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
        mesh.RecalculateNormals();
        return mesh;
    }

    private float[] SampleNoise(int vertsX, int vertsZ)
    {
        var   rng   = new System.Random(terrainDataStore.seed);
        float seedX = (float)(rng.NextDouble()*10f);
        float seedZ = (float)(rng.NextDouble()*10f);
        Debug.Log(seedX + ", " + seedZ);
        var noise = new float[vertsX * vertsZ];
        for (int z = 0; z < vertsZ; z++)
        for (int x = 0; x < vertsX; x++)
        {
            float wx = -terrainDataStore.extentX + x * terrainDataStore.step;
            float wz = -terrainDataStore.extentZ + z * terrainDataStore.step;
            noise[z * vertsX + x] = Mathf.PerlinNoise(
                (wx + terrainDataStore.noiseOffset.x) * terrainDataStore.noiseScale + seedX + 10000.0f,
                (wz + terrainDataStore.noiseOffset.y) * terrainDataStore.noiseScale + seedZ + 10000.0f);
        }

        return noise;
    }
}
