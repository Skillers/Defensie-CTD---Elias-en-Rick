using UnityEngine;

public class PlacedObstacle : MonoBehaviour
{
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public ObstacleSO obstacleSO;

    private Renderer _renderer;
    private Material _materialInstance;
    private Color _originalColor;

    private void Awake()
    {
        _renderer = GetComponentInChildren<Renderer>();
        _materialInstance = _renderer.material;
        _originalColor = _materialInstance.GetColor(BaseColor);
    }

    public void SetSelected(bool selected)
    {
        _materialInstance.SetColor(BaseColor, selected ? Color.yellow : _originalColor);
    }
}
