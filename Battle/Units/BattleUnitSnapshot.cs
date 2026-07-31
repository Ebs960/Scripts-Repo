using System.Collections.Generic;

public sealed class BattleUnitSnapshot
{
    public readonly int CampaignRuntimeId;
    public readonly CombatUnit SourceUnit;
    public readonly CombatUnitData UnitData;
    public readonly Civilization Owner;
    public readonly string FormationId;

    public readonly int StartingCampaignTile;
    public readonly TileLayer StartingLayer;
    public readonly int StartingStackSlot;

    public readonly int StartingHealth;
    public readonly int MaximumHealth;

    public readonly int MeleeAttack;
    public readonly int RangedAttack;
    public readonly int Defense;
    public readonly float Range;

    public readonly int TacticalMovePoints;
    public readonly int TacticalActionPoints;

    public readonly TacticalUnitProfile TacticalProfile;
    public readonly BattleDomain Domain;
    public readonly List<TacticalWeaponProfile> Weapons = new();

    public readonly int Experience;
    public readonly int Level;

    public BattleUnitSnapshot(
        CombatUnit source,
        TacticalUnitProfile tacticalProfile,
        int tacticalMovePoints,
        int tacticalActionPoints)
    {
        SourceUnit = source;
        UnitData = source != null ? source.data : null;
        Owner = source != null ? source.owner : null;

        CampaignRuntimeId = source != null && source.gameObject != null ? source.gameObject.GetRuntimeId() : 0;
        FormationId = source != null ? source.EnsureMilitaryFormationIdentity() : string.Empty;

        StartingCampaignTile = source != null ? source.currentTileIndex : -1;
        StartingLayer = source != null ? source.currentLayer : TileLayer.Surface;
        StartingStackSlot = source != null ? source.stackSlot : -1;

        StartingHealth = source != null ? source.currentHealth : 0;
        MaximumHealth = source != null ? source.MaxHealth : 1;

        MeleeAttack = source != null ? source.CurrentMeleeAttack : 0;
        RangedAttack = source != null ? source.CurrentRangedAttack : 0;
        Defense = source != null ? source.CurrentDefense : 0;
        Range = source != null ? source.CurrentRange : 1f;

        TacticalProfile = tacticalProfile;
        Domain = BattleDomainResolver.Resolve(source);
        TacticalMovePoints = tacticalMovePoints;
        TacticalActionPoints = tacticalActionPoints;

        AddWeaponProfiles(source, tacticalProfile);

        Experience = source != null ? source.experience : 0;
        Level = source != null ? source.level : 1;
    }

    private void AddWeaponProfiles(CombatUnit source, TacticalUnitProfile profile)
    {
        if (profile?.weapons != null)
        {
            for (int i = 0; i < profile.weapons.Length; i++)
                if (profile.weapons[i] != null)
                    Weapons.Add(profile.weapons[i]);
        }

        if (Weapons.Count > 0 || source == null)
            return;

        BattleDomainMask fallbackTargets = profile != null ? profile.targetDomains : BattleDomainResolver.ToMask(Domain);
        if (source.Weapon != null)
        {
            Weapons.Add(new TacticalWeaponProfile
            {
                equipment = source.Weapon,
                targetDomains = InferTargetDomains(source.Weapon, fallbackTargets),
                maximumRange = 1,
                usesRangedAttack = false,
            });
        }
        if (source.ProjectileWeapon != null)
        {
            Weapons.Add(new TacticalWeaponProfile
            {
                equipment = source.ProjectileWeapon,
                targetDomains = InferTargetDomains(source.ProjectileWeapon, fallbackTargets),
                minimumRange = profile != null ? profile.minimumRange : 0,
                maximumRange = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.FloorToInt(Range)),
                usesRangedAttack = true,
            });
        }
        if (Weapons.Count == 0)
        {
            Weapons.Add(new TacticalWeaponProfile
            {
                targetDomains = fallbackTargets,
                maximumRange = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.FloorToInt(Range)),
                usesRangedAttack = Range > 1f,
            });
        }
    }

    private static BattleDomainMask InferTargetDomains(EquipmentData equipment, BattleDomainMask fallback)
    {
        if (equipment == null)
            return fallback;

        BattleDomainMask mask = BattleDomainMask.None;
        if (equipment.groundAttackBonus != 0f || equipment.attackBonus != 0f || equipment.meleeAttackBonus != 0f || equipment.rangedAttackBonus != 0f)
            mask |= BattleDomainMask.Land | BattleDomainMask.NavalSurface;
        if (equipment.underwaterAttackBonus != 0f) mask |= BattleDomainMask.Underwater;
        if (equipment.airAttackBonus != 0f) mask |= BattleDomainMask.Air;
        if (equipment.spaceAttackBonus != 0f) mask |= BattleDomainMask.Orbit | BattleDomainMask.Space;
        return mask == BattleDomainMask.None ? fallback : mask;
    }
}
