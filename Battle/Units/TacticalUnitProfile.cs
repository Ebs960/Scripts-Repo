using UnityEngine;

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

    public float highGroundMultiplier = 1f;
    public float coverEffectivenessMultiplier = 1f;
}
