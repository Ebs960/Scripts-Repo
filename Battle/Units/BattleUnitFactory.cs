using System.Collections.Generic;

public static class BattleUnitFactory
{
    public static List<BattleUnitState> CreateStates(List<BattleUnitSnapshot> attackerSnapshots, List<BattleUnitSnapshot> defenderSnapshots)
    {
        var result = new List<BattleUnitState>(attackerSnapshots.Count + defenderSnapshots.Count);
        int nextId = 1;

        for (int i = 0; i < attackerSnapshots.Count; i++)
        {
            result.Add(Create(nextId++, attackerSnapshots[i], BattleSide.Attacker));
        }

        for (int i = 0; i < defenderSnapshots.Count; i++)
        {
            result.Add(Create(nextId++, defenderSnapshots[i], BattleSide.Defender));
        }

        return result;
    }

    public static void AppendReserves(List<BattleUnitState> states, List<BattleReinforcementGroup> groups)
    {
        int nextId = states.Count + 1;
        for (int g = 0; g < groups.Count; g++)
        for (int i = 0; i < groups[g].Units.Count; i++)
        {
            var state = Create(nextId++, groups[g].Units[i], groups[g].Side);
            state.IsReserve = true;
            state.CellIndex = -1;
            state.ReinforcementGroupId = groups[g].ReinforcementGroupId;
            states.Add(state);
        }
    }

    private static BattleUnitState Create(int id, BattleUnitSnapshot snap, BattleSide side)
    {
        return new BattleUnitState
        {
            UnitId = id,
            Snapshot = snap,
            Side = side,
            CellIndex = -1,
            CurrentHealth = snap.StartingHealth,
            CurrentMovePoints = snap.TacticalMovePoints,
            CurrentActionPoints = snap.TacticalActionPoints,
            HasMoved = false,
            HasActed = false,
            IsDefending = false,
            IsWaiting = false,
            IsReserve = false,
            HasRetreated = false,
            IsDead = snap.StartingHealth <= 0,
            DepthBand = snap.Domain == BattleDomain.Underwater ? BattleDepthBand.Shallow : BattleDepthBand.Surface,
        };
    }
}
