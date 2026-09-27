using System.Collections.Generic;

public static class BattleObjectiveBuilder
{
    public static BattleObjective BuildObjective(BattleMap map)
    {
        return new BattleObjective
        {
            CellIndex = -1,
            Owner = BattleSide.Defender,
            Type = BattleObjectiveType.Elimination,
        };
    }

    private static bool SupportsAnyDomain(BattleCell cell) =>
        cell.SupportsLand || cell.SupportsNavalSurface || cell.SupportsUnderwater ||
        cell.SupportsAir || cell.SupportsOrbit || cell.SupportsSpace;
}
