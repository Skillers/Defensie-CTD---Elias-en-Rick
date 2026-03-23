using UnityEngine;
 
public enum TerrainType
{
    Grass,
    Dirt,
    Sand
}
 
[System.Serializable]
public class TerrainCell
{
    public TerrainType type;
    public int movementCost;
    public Color color;
 
    public static TerrainCell Make(TerrainType t)
    {
        return t switch
        {
            TerrainType.Grass => new TerrainCell { type = t, movementCost = 2, color = new Color(0.33f, 0.65f, 0.25f) },
            TerrainType.Dirt  => new TerrainCell { type = t, movementCost = 3, color = new Color(0.60f, 0.40f, 0.18f) },
            TerrainType.Sand  => new TerrainCell { type = t, movementCost = 5, color = new Color(0.87f, 0.78f, 0.45f) },
            _                 => new TerrainCell { type = t, movementCost = 1, color = Color.white }
        };
    }
}