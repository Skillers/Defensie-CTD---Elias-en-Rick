using UnityEngine;

public class PlacedObstacle : MonoBehaviour
{
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    public ObstacleSO obstacleSO;

    private Renderer _renderer;

    private void Awake()
    {
        _renderer = GetComponentInChildren<Renderer>();
    }

    public void SetSelected(bool selected)
    {
        _renderer.material.SetColor(BaseColor, selected ? Color.yellow : Color.white);
    }
}
