using UnityEngine;

public class PlacedObstacle : MonoBehaviour
{
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    public ObstacleSO obstacleSO;

    private Renderer _renderer;
    private Material _materialInstance;
    private Color _originalColor;
    private Color _originalEmission;

    private void Awake()
    {
        _renderer = GetComponentInChildren<Renderer>();
        _materialInstance = _renderer.material;
        _originalColor = _materialInstance.GetColor(BaseColor);
        _materialInstance.EnableKeyword("_EMISSION");
        _originalEmission = _materialInstance.GetColor(EmissionColor);
    }

    public void SetSelected(bool selected)
    {
        if (selected)
        {
            _materialInstance.SetColor(EmissionColor, Color.cyan * 0.1f);
        }
        else
        {
            _materialInstance.SetColor(EmissionColor, _originalEmission);
        }
    }
}
