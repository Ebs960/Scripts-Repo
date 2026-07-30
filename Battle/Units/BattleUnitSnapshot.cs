public sealed class BattleUnitSnapshot
{
    public readonly int CampaignRuntimeId;
    public readonly CombatUnit SourceUnit;
    public readonly CombatUnitData UnitData;
    public readonly Civilization Owner;

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

        Experience = source != null ? source.experience : 0;
        Level = source != null ? source.level : 1;
    }
}
