using UnityEngine;

public class StartEndPoints : MonoBehaviour
{
    public TerrainDataStore terrainDataStore;
    public PerlinNoisePlane    noisePlane;

    private const float EdgeMargin  = 10f;
    private const float SphereSize  = 2f;
    private const float Step        = 0.5f;
    private const float RoundStep   = 0.25f;

    // Public references for other scripts
    public Vector3 StartPoint { get; private set; }
    public Vector3 EndPoint   { get; private set; }

    private GameObject _startSphere;
    private GameObject _endSphere;

    void OnEnable()
    {
        if (noisePlane != null)
            noisePlane.OnGenerated += Place;
    }

    void OnDisable()
    {
        if (noisePlane != null)
            noisePlane.OnGenerated -= Place;
    }

    void Start()
    {
        if (noisePlane != null)
            noisePlane.OnGenerated += Place;

        // If noise is already ready, place immediately
        if (noisePlane != null && noisePlane.NoiseValues != null && noisePlane.NoiseValues.Length > 0)
            Place();
    }

    [ContextMenu("Replace")]
    public void Place()
    {
        if (terrainDataStore == null) { Debug.LogError("StartEndPoints: no TerrainDataStore assigned."); return; }

        float xMax =  terrainDataStore.extentX - EdgeMargin;
        float xMin = -terrainDataStore.extentX + EdgeMargin;
        float zMin = -terrainDataStore.extentZ + EdgeMargin;
        float zMax =  terrainDataStore.extentZ - EdgeMargin;

        if (xMax <= 0 || xMin >= 0)
        {
            Debug.LogError($"StartEndPoints: extents too small for a {EdgeMargin} unit edge margin.");
            return;
        }

        // Use seed so the same seed always produces the same Z positions
        var rng = new System.Random(terrainDataStore.seed);
        float startZ = zMin + (float)(rng.NextDouble() * (zMax - zMin));
        float endZ   = zMin + (float)(rng.NextDouble() * (zMax - zMin));

        StartPoint = new Vector3(xMax, HeightAt(xMax, startZ) + SphereSize * 0.5f, startZ);
        EndPoint   = new Vector3(xMin, HeightAt(xMin, endZ)   + SphereSize * 0.5f, endZ);

        PlaceSphere(ref _startSphere, "StartPoint", StartPoint, Color.red);
        PlaceSphere(ref _endSphere,   "EndPoint",   EndPoint,   Color.green);
    }

    // Returns the rounded terrain height at a world X,Z position
    private float HeightAt(float worldX, float worldZ)
    {
        if (noisePlane == null || noisePlane.NoiseValues == null) return 0f;

        int xi = Mathf.Clamp(Mathf.RoundToInt((worldX + terrainDataStore.extentX) / Step), 0, noisePlane.VertsX - 1);
        int zi = Mathf.Clamp(Mathf.RoundToInt((worldZ + terrainDataStore.extentZ) / Step), 0, noisePlane.VertsZ - 1);

        float noise   = noisePlane.GetValue(xi, zi);
        return Mathf.Round(noise * terrainDataStore.heightMultiplier / RoundStep) * RoundStep;
    }

    private void PlaceSphere(ref GameObject sphere, string label, Vector3 position, Color color)
    {
        if (sphere == null)
        {
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = label;
            sphere.transform.SetParent(transform);
            // Remove physics — purely visual markers
            Destroy(sphere.GetComponent<Collider>());

            var mat    = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            mat.color  = color;
            sphere.GetComponent<MeshRenderer>().sharedMaterial = mat;
        }

        sphere.transform.position   = position;
        sphere.transform.localScale = Vector3.one * SphereSize;
    }
}
