using System.Collections.Generic;

/// <summary>Deterministic topology-only flanking support. Presentation/facing never affects combat.</summary>
public static class BattleFlankingResolver
{
    public static int CountSupportingDirections(BattleSession session, BattleUnitState attacker, BattleUnitState defender)
    {
        if(session?.Map==null||attacker==null||defender==null||attacker.Domain!=BattleDomain.Land||defender.Domain!=BattleDomain.Land)return 0;
        var defenderCell=session.Map.GetCell(defender.CellIndex);if(defenderCell?.NeighborIndices==null)return 0;
        var directions=new HashSet<int>();
        for(int direction=0;direction<defenderCell.NeighborIndices.Length;direction++)
        {
            int neighbor=defenderCell.NeighborIndices[direction];
            for(int i=0;i<session.Units.Count;i++)
            {
                var ally=session.Units[i];
                if(ally==null||ally==attacker||ally.Side!=attacker.Side||ally.Domain!=BattleDomain.Land||!ally.IsAliveAndActive||ally.IsEmbarked||ally.CellIndex!=neighbor)continue;
                if(ally.Snapshot?.Weapons==null||ally.Snapshot.Weapons.Count==0)continue;
                directions.Add(direction);break;
            }
        }
        return directions.Count;
    }
}
