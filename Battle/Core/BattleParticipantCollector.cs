using System.Collections.Generic;
using UnityEngine;

public sealed class BattleParticipantCollector
{
    private readonly BattleRuleset ruleset;

    public BattleParticipantCollector(BattleRuleset ruleset)
    {
        this.ruleset = ruleset;
    }

    public bool TryBuildPreview(CombatUnit attacker, CombatUnit defender, out EngagementPreview preview)
    {
        preview = new EngagementPreview
        {
            Attacker = attacker,
            Defender = defender,
            Mode = EngagementModeResolver.ResolveEngagementMode(attacker, defender),
            IsValid = false,
        };

        if (attacker == null || defender == null)
        {
            preview.RejectionReason = "missing attacker or defender";
            return false;
        }

        if (preview.Mode != EngagementMode.TacticalLandBattle)
        {
            preview.RejectionReason = "engagement mode not tactical";
            return false;
        }

        if (attacker.planetIndex != defender.planetIndex)
        {
            preview.RejectionReason = "cross planet";
            return false;
        }

        if (attacker.currentLayer != TileLayer.Surface || defender.currentLayer != TileLayer.Surface)
        {
            preview.RejectionReason = "non surface layer";
            return false;
        }

        preview.PlanetIndex = attacker.planetIndex;
        preview.AnchorTile = defender.currentTileIndex;
        preview.RandomSeed = attacker.gameObject.GetRuntimeId() ^ defender.gameObject.GetRuntimeId() ^ Time.frameCount;

        var attackerUnits = CollectStackCombatUnits(attacker);
        var defenderUnits = CollectStackCombatUnits(defender);

        AddSnapshots(attackerUnits, preview.AttackerUnits);
        AddSnapshots(defenderUnits, preview.DefenderUnits);

        if (preview.AttackerUnits.Count == 0 || preview.DefenderUnits.Count == 0)
        {
            preview.RejectionReason = "empty participant side";
            return false;
        }

        // Reinforcement support: find adjacent allied stacks and queue as reserve groups.
        BuildReinforcements(preview, attacker.owner, defender.owner);

        preview.IsValid = true;
        return true;
    }

    private void AddSnapshots(List<CombatUnit> sourceUnits, List<BattleUnitSnapshot> snapshots)
    {
        for (int i = 0; i < sourceUnits.Count; i++)
        {
            var cu = sourceUnits[i];
            if (cu == null || cu.currentHealth <= 0)
                continue;

            var profile = BattleProfileInference.Resolve(cu.data);
            int move = profile != null ? Mathf.Max(1, profile.tacticalMovePoints) : 3;
            int actions = profile != null ? Mathf.Max(1, profile.tacticalActionPoints) : 1;
            snapshots.Add(new BattleUnitSnapshot(cu, profile, move, actions));
        }
    }

    private static List<CombatUnit> CollectStackCombatUnits(CombatUnit root)
    {
        var list = new List<CombatUnit> { root };
        var stacked = root.GetStackedUnits();
        for (int i = 0; i < stacked.Count; i++)
        {
            if (stacked[i] is CombatUnit cu)
                list.Add(cu);
        }

        return list;
    }

    private void BuildReinforcements(EngagementPreview preview, Civilization attackerCiv, Civilization defenderCiv)
    {
        var ts = TileSystem.GetForPlanet(preview.PlanetIndex) ?? TileSystem.Instance;
        if (ts == null)
            return;

        var queue = new Queue<(int tile, int depth)>();
        var seen = new HashSet<int> { preview.AnchorTile };
        queue.Enqueue((preview.AnchorTile, 0));

        while (queue.Count > 0)
        {
            var (tile, depth) = queue.Dequeue();
            if (depth > ruleset.reinforcementRadius)
                continue;

            var occ = TileOccupancyManager.GetForPlanet(preview.PlanetIndex) ?? TileOccupancyManager.Instance;
            var units = occ != null ? occ.GetAllOccupantObjects(tile, TileLayer.Surface) : null;
            if (units != null)
            {
                var attackerReserve = new BattleReinforcementGroup { Side = BattleSide.Attacker, OriginCampaignTile = tile, AvailableFromRound = ruleset.reinforcementStartRound };
                var defenderReserve = new BattleReinforcementGroup { Side = BattleSide.Defender, OriginCampaignTile = tile, AvailableFromRound = ruleset.reinforcementStartRound };

                for (int i = 0; i < units.Count; i++)
                {
                    var cu = units[i] != null ? units[i].GetComponent<CombatUnit>() : null;
                    if (cu == null || cu.currentHealth <= 0)
                        continue;

                    if (cu == preview.Attacker || cu == preview.Defender)
                        continue;

                    if (cu.owner == attackerCiv)
                    {
                        var profile = BattleProfileInference.Resolve(cu.data);
                        attackerReserve.Units.Add(new BattleUnitSnapshot(cu, profile, profile != null ? profile.tacticalMovePoints : 3, profile != null ? profile.tacticalActionPoints : 1));
                    }
                    else if (cu.owner == defenderCiv)
                    {
                        var profile = BattleProfileInference.Resolve(cu.data);
                        defenderReserve.Units.Add(new BattleUnitSnapshot(cu, profile, profile != null ? profile.tacticalMovePoints : 3, profile != null ? profile.tacticalActionPoints : 1));
                    }
                }

                if (attackerReserve.Units.Count > 0)
                    preview.Reinforcements.Add(attackerReserve);
                if (defenderReserve.Units.Count > 0)
                    preview.Reinforcements.Add(defenderReserve);
            }

            var neigh = ts.GetNeighbors(tile);
            for (int i = 0; i < neigh.Length; i++)
            {
                if (seen.Add(neigh[i]))
                    queue.Enqueue((neigh[i], depth + 1));
            }
        }
    }
}

public static class BattleProfileInference
{
    public static TacticalUnitProfile Resolve(CombatUnitData data)
    {
        if (data == null)
            return null;

        return data.tacticalUnitProfile;
    }
}
