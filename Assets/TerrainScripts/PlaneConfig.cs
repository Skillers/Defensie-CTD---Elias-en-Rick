using UnityEngine;

[CreateAssetMenu(fileName = "PlaneConfig", menuName = "Config/Plane Config")]
public class PlaneConfig : ScriptableObject
{
    [Header("Plane Extents (units from center)")]
    public float extentX = 10f;
    public float extentZ = 10f;

    [Header("Noise Settings")]
    public int seed = 0;
    public float noiseScale = 0.1f;
    public Vector2 noiseOffset = Vector2.zero;
    public float heightMultiplier = 0f;

}
