using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "CTD/Obstacle Inventory")]
public class ObstacleInventorySO : ScriptableObject
{
    public List<ObstacleSO> obstacles;
}
