using UnityEngine;

[CreateAssetMenu(menuName = "Battle/Fortification Visual Profile")]
public sealed class BattleFortificationVisualProfile : ScriptableObject
{
    public GameObject wallPrefab;
    public GameObject gatePrefab;
    public GameObject breachedWallPrefab;
    public GameObject breachedGatePrefab;
    public GameObject strongpointPrefab;
    public GameObject impactVfxPrefab;
    [Tooltip("Used by procedural fallbacks, so materials remain visually distinct without prefabs.")]
    public Color fallbackColor = new(.38f, .25f, .12f);
}

[CreateAssetMenu(menuName = "Battle/Fortification Profile")]
public sealed class BattleFortificationProfile : ScriptableObject
{
    public string profileId;
    public string displayName;
    [Min(0)] public int fortificationTier;
    public BattleFortificationMaterial material;
    [Min(1)] public int wallHitPoints = 100;
    [Min(1)] public int gateHitPoints = 75;
    [Min(1)] public int strongpointHitPoints = 150;
    [Min(0)] public int defense = 10;
    [Min(0f)] public float siegeDamageTakenMultiplier = 1f;
    [Min(0f)] public float autoResolveDefenseMultiplier = 1.25f;
    public BattleFortificationVisualProfile visualProfile;

    public string Identity => string.IsNullOrWhiteSpace(profileId) ? name : profileId;
}
