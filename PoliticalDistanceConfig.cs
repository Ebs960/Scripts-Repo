using UnityEngine;

[CreateAssetMenu(fileName = "PoliticalDistanceConfig", menuName = "Config/PoliticalDistanceConfig")]
public class PoliticalDistanceConfig : ScriptableObject
{
    [Header("Distance thresholds")]
    [Tooltip("Minimum tile distance before penalties start applying")]
    public int minDistanceThreshold = 16;
    [Tooltip("Distance value used to treat cross-planet holdings as far away")]
    public int maxCrossPlanetDistance = 150;
    [Tooltip("Max BFS depth when searching tile distances")]
    public int maxSearchDepth = 150;

    [Header("Governor penalty tuning")]
    [Tooltip("Penalty per tile beyond the threshold applied to governor opinion")]
    public float governorPenaltyPerTile = 0.6f;
    [Tooltip("Maximum total governor opinion penalty")]
    public float governorPenaltyCap = 25f;

    [Header("Vassal liberty tuning")]
    [Tooltip("Liberty desire per tile beyond the threshold for vassals")]
    public float vassalLibertyPerTile = 0.08f;
    [Tooltip("Maximum per-turn liberty pressure from distance")]
    public float vassalLibertyCap = 4f;
}
