using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

public class PlacedObstacle : MonoBehaviour
{
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    [FormerlySerializedAs("obstacleSO")]
    public ObstacleSO obstacleSo;

    /// <summary>Grid cells this obstacle currently occupies. Owned by this instance; written by TerrainDataStore.RegisterObstacleCells.</summary>
    [System.NonSerialized] public List<Vector2Int> affectedCells = new List<Vector2Int>();

    private TerrainDataStore _registeredStore;

    private List<Renderer> _renderers = new List<Renderer>();
    private List<Material> _materialInstances = new List<Material>();
    private List<Color> _originalEmissions = new List<Color>();
    private bool _isInitialized = false;

    private void Awake()
    {
        Initialize();
    }

    public void Initialize()
    {
        if (_isInitialized) return;

        _renderers.Clear();
        _materialInstances.Clear();
        _originalEmissions.Clear();

        GetComponentsInChildren<Renderer>(true, _renderers);
        
        // If we are a root group and children are not instantiated yet, 
        // defer initialization until segment instantiation is complete
        if (_renderers.Count == 0) return;

        foreach (var r in _renderers)
        {
            if (r == null) continue;
            var mat = r.material;
            mat.EnableKeyword("_EMISSION");
            _materialInstances.Add(mat);
            
            Color emission = Color.black;
            if (mat.HasProperty(EmissionColor))
            {
                emission = mat.GetColor(EmissionColor);
            }
            _originalEmissions.Add(emission);
        }

        _isInitialized = true;
    }

    public void SetSelected(bool selected)
    {
        // Fallback safety in case initialization was missed
        if (!_isInitialized)
        {
            Initialize();
        }

        for (int i = 0; i < _materialInstances.Count; i++)
        {
            var mat = _materialInstances[i];
            if (mat == null) continue;

            if (selected)
            {
                if (mat.HasProperty(EmissionColor))
                {
                    mat.SetColor(EmissionColor, Color.cyan * 0.1f);
                }
            }
            else
            {
                if (mat.HasProperty(EmissionColor))
                {
                    mat.SetColor(EmissionColor, _originalEmissions[i]);
                }
            }
        }
    }

    /// <summary>Called by TerrainDataStore.RegisterObstacleCells so this obstacle can call back on destroy.</summary>
    public void OnRegistered(TerrainDataStore store)
    {
        _registeredStore = store;
    }

    private void OnDestroy()
    {
        if (_registeredStore != null)
        {
            _registeredStore.UnregisterObstacleCells(this);
        }
    }
}
