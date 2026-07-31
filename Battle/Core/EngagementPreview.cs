using System.Collections.Generic;
using UnityEngine;

public sealed class EngagementPreview
{
    public bool IsValid;
    public string RejectionReason;

    public int PlanetIndex;
    public int AnchorTile;
    public CombatUnit Attacker;
    public CombatUnit Defender;

    public EngagementMode Mode;
    public BattleTheater Theater;
    public int SpaceRegionId = -1;
    public bool AllowsManualBattle;
    public bool AllowsRetreat;
    public bool AllowsCancel;
    public int RandomSeed;

    public readonly List<BattleUnitSnapshot> AttackerUnits = new();
    public readonly List<BattleUnitSnapshot> DefenderUnits = new();
    public readonly List<BattleReinforcementGroup> Reinforcements = new();

    public BattleMap Map;
    public BattleObjective Objective;

    public Vector2 ApproachDirectionXZ;

    public int TotalUnits => AttackerUnits.Count + DefenderUnits.Count;
}
