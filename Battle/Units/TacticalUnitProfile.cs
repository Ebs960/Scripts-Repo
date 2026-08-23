using UnityEngine;

[System.Serializable]
public sealed class TacticalWeaponProfile
{
    public EquipmentData equipment;
    public BattleDomainMask targetDomains = BattleDomainMask.Land;
    public int minimumRange;
    public int maximumRange = 1;
    public bool usesRangedAttack;
    [Min(0.01f)] public float attackMultiplier = 1f;
    [Min(0f), Tooltip("Damage multiplier against tactical structures.")]
    public float fortificationDamageMultiplier = 1f;
    public bool usesIndirectFire;
    [Header("Presentation (optional)")]
    [Tooltip("Tactical-only projectile override. The weapon ProjectileData visual is used when this is empty.")]
    public GameObject tacticalProjectilePrefab;
    public GameObject tacticalImpactPrefab;
    [Min(0.01f)] public float tacticalProjectileSpeed;
    [Min(0f)] public float tacticalArcHeight;
    public Vector3 tacticalProjectileScale = Vector3.one;
    [Tooltip("-1 means unlimited tactical ammunition.")] public int ammunition = -1;
    [Min(0)] public int cooldownRounds;
}

[CreateAssetMenu(menuName = "Data/Tactical Unit Profile")]
public sealed class TacticalUnitProfile : ScriptableObject
{
    public BattleRole role = BattleRole.LineInfantry;

    public int tacticalMovePoints = 3;
    public int tacticalActionPoints = 1;

    public bool exertsZoneOfControl = true;
    public bool ignoresZoneOfControl;
    public bool canMoveAfterAttacking;
    public bool canAttackAfterMoving = true;

    public bool canCrossCliffs;
    public bool ignoresRiverPenalty;
    public bool ignoresForestMovementPenalty;

    public bool usesDirectFire = true;
    public bool usesIndirectFire;
    public int minimumRange;

    [Header("Multi-domain combat")]
    [Tooltip("Domains this unit's primary tactical weapon can target.")]
    public BattleDomainMask targetDomains = BattleDomainMask.Land;
    public int sensorRange;
    [Min(0)] public int activeSensorRangeBonus = 2;
    public BattleDomainMask sensorDomains = BattleDomainMask.None;
    public int stealth;
    public bool isTransport;
    public bool isCarrier;
    public int transportCapacity;
    [Tooltip("Rounds an aircraft can remain launched; -1 means unlimited.")] public int tacticalFuelRounds = -1;
    public TacticalWeaponProfile[] weapons;

    public float highGroundMultiplier = 1f;
    public float coverEffectivenessMultiplier = 1f;
}
