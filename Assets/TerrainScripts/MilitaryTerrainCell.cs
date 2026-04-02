using UnityEngine;

public enum MilitaryTerrainType
{
    Grass,          // cost 3  — open grassy area
    GrassyPlain,    // cost 3  — wide flat grass, easy movement
    Snow,           // cost 5  — flat snow, slippery
    SnowyHill,      // cost 8  — elevated snow, hard going
    Mud,            // cost 6  — wet and slow
    MuddyMountain,  // cost 9  — steep and wet, very hard
    DenseForest,    // cost 7  — near-impassable vegetation
    RockyTerrain,   // cost 7  — uneven rocky ground
}

[System.Serializable]
public class MilitaryTerrainCell
{
    public MilitaryTerrainType type;
    public int   movementCost;
    public Color color;

    public static MilitaryTerrainCell Make(MilitaryTerrainType t)
    {
        return t switch
        {
            MilitaryTerrainType.Grass          => new MilitaryTerrainCell { type = t, movementCost = 3, color = new Color(0.33f, 0.65f, 0.25f) },
            MilitaryTerrainType.GrassyPlain    => new MilitaryTerrainCell { type = t, movementCost = 3, color = new Color(0.55f, 0.78f, 0.35f) },
            MilitaryTerrainType.Snow           => new MilitaryTerrainCell { type = t, movementCost = 5, color = new Color(0.88f, 0.93f, 0.98f) },
            MilitaryTerrainType.SnowyHill      => new MilitaryTerrainCell { type = t, movementCost = 8, color = new Color(0.70f, 0.80f, 0.90f) },
            MilitaryTerrainType.Mud            => new MilitaryTerrainCell { type = t, movementCost = 6, color = new Color(0.38f, 0.27f, 0.16f) },
            MilitaryTerrainType.MuddyMountain  => new MilitaryTerrainCell { type = t, movementCost = 9, color = new Color(0.28f, 0.22f, 0.14f) },
            MilitaryTerrainType.DenseForest    => new MilitaryTerrainCell { type = t, movementCost = 7, color = new Color(0.10f, 0.38f, 0.12f) },
            MilitaryTerrainType.RockyTerrain   => new MilitaryTerrainCell { type = t, movementCost = 7, color = new Color(0.52f, 0.48f, 0.42f) },
            _                                  => new MilitaryTerrainCell { type = t, movementCost = 3, color = Color.white }
        };
    }
}
