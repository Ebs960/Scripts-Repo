using UnityEngine;

[System.Serializable]
public sealed class TacticalWeaponProfile
{
    public EquipmentData equipment;
    public BattleDomainMask targetDomains = BattleDomainMask.Land;
    public int minimumRange;
    public int maximumRange = 1;
    public bool usesRangedAttack;
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
    public BattleDomainMask sensorDomains = BattleDomainMask.None;
    public int stealth;
    public bool isTransport;
    public bool isCarrier;
    public int transportCapacity;
    public TacticalWeaponProfile[] weapons;

    public float highGroundMultiplier = 1f;
    public float coverEffectivenessMultiplier = 1f;
}
