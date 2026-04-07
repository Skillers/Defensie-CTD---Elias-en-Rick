using UnityEngine;

public class StartEndPoints : MonoBehaviour
{
    public TerrainDataStore terrainDataStore;

    private const float EdgeMargin  = 10f;
    private const float SphereSize  = 2f;

    // Public references for other scripts
    public Vector3 StartPoint { get; private set; }
    public Vector3 EndPoint   { get; private set; }

    private GameObject _startSphere;
    private GameObject _endSphere;

    void OnEnable()
    {
        if (terrainDataStore != null)
            terrainDataStore.OnGridReady += Place;
    }

    void OnDisable()
    {
        if (terrainDataStore != null)
            terrainDataStore.OnGridReady -= Place;
    }

    void Start()
    {
        if (terrainDataStore != null)
            terrainDataStore.OnGridReady += Place;

        // If grid is already ready, place immediately
        if (terrainDataStore != null && terrainDataStore.grid != null)
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
        if (terrainDataStore == null || terrainDataStore.grid == null) return 0f;

        Vector2Int g = terrainDataStore.WorldToGrid(new Vector3(worldX, 0f, worldZ));
        return terrainDataStore.grid[g.x, g.y].roundedHeight;
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
