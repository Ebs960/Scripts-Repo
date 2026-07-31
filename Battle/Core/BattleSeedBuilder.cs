public static class BattleSeedBuilder
{
    public static int Build(CombatUnit attacker, CombatUnit defender, BattleTheaterDecision decision, int campaignTurn, int sequence = 0)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + campaignTurn;
            hash = hash * 31 + (int)decision.Theater;
            hash = hash * 31 + decision.PlanetIndex;
            hash = hash * 31 + decision.SpaceRegionId;
            hash = hash * 31 + (attacker != null && attacker.gameObject != null ? attacker.gameObject.GetRuntimeId() : 0);
            hash = hash * 31 + (defender != null && defender.gameObject != null ? defender.gameObject.GetRuntimeId() : 0);
            hash = hash * 31 + (attacker != null ? (BattleTheaterResolver.IsOnSpaceMap(attacker) ? attacker.currentSpaceTileIndex : attacker.currentTileIndex) : -1);
            hash = hash * 31 + (defender != null ? (BattleTheaterResolver.IsOnSpaceMap(defender) ? defender.currentSpaceTileIndex : defender.currentTileIndex) : -1);
            return hash * 31 + sequence;
        }
    }
}
