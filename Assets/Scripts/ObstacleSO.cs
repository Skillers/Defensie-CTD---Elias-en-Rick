using UnityEngine;

[CreateAssetMenu(fileName = "NewObstacle", menuName = "CTD/Obstacle")]
public class ObstacleSO : ScriptableObject
{
    public string obstacleName;
    public Sprite icon;
    public GameObject prefab;
}
