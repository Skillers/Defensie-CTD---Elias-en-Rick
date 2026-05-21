using System.Collections.Generic;
using UnityEngine;

public enum PlacementType { Point, Line }
public enum ObstacleEffect { None, Fix, Disrupt, Block, Turn }

[CreateAssetMenu(fileName = "NewObstacle", menuName = "CTD/Obstacle")]
public class ObstacleSO : ScriptableObject
{
    [Header("UI")]
    public string obstacleName;
    public Sprite icon;
    public GameObject prefab;
    
    [Header("Placement")]
    public PlacementType placementType;
    
    [Header("Pathfinding")]
    public List<ObstacleEffect> obstacleEffects = new List<ObstacleEffect>();
}
