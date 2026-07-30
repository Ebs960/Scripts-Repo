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

        if (preview.Mode != EngagementMode.TacticalBattle)
        {
            preview.RejectionReason = "engagement mode not tactical";
            return false;
        }

        // A single planetary battle space cannot mix tile indices from different
        // planets. Interplanetary scripted attacks use the documented fallback
        // until the campaign exposes a shared deep-space region identifier.
        if (attacker.planetIndex != defender.planetIndex)
        {
            preview.RejectionReason = "different battle-space regions";
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
        var participants = new HashSet<int>();
        AddRuntimeIds(preview.AttackerUnits, participants);
        AddRuntimeIds(preview.DefenderUnits, participants);
        var seen = new HashSet<int> { preview.AnchorTile };
        queue.Enqueue((preview.AnchorTile, 0));

        while (queue.Count > 0)
        {
            var (tile, depth) = queue.Dequeue();
            if (depth > ruleset.reinforcementRadius)
                continue;

            var occ = TileOccupancyManager.GetForPlanet(preview.PlanetIndex) ?? TileOccupancyManager.Instance;
            foreach (TileLayer layer in System.Enum.GetValues(typeof(TileLayer)))
            {
                var units = occ != null ? occ.GetAllOccupantObjects(tile, layer) : null;
                if (units == null) continue;
                var domain = DomainForLayer(layer);
                var attackerReserve = NewReserve(BattleSide.Attacker, tile, domain);
                var defenderReserve = NewReserve(BattleSide.Defender, tile, domain);

                for (int i = 0; i < units.Count; i++)
                {
                    var cu = units[i] != null ? units[i].GetComponent<CombatUnit>() : null;
                    if (cu == null || cu.currentHealth <= 0)
                        continue;

                    if (cu == preview.Attacker || cu == preview.Defender)
                        continue;

                    int runtimeId = cu.gameObject != null ? cu.gameObject.GetRuntimeId() : 0;
                    if (runtimeId != 0 && !participants.Add(runtimeId))
                        continue;

                    if (cu.owner == attackerCiv)
                    {
                        var profile = BattleProfileInference.Resolve(cu.data);
                        SetReserveDomain(attackerReserve, BattleDomainResolver.Resolve(cu));
                        attackerReserve.Units.Add(new BattleUnitSnapshot(cu, profile, profile != null ? profile.tacticalMovePoints : 3, profile != null ? profile.tacticalActionPoints : 1));
                    }
                    else if (cu.owner == defenderCiv)
                    {
                        var profile = BattleProfileInference.Resolve(cu.data);
                        SetReserveDomain(defenderReserve, BattleDomainResolver.Resolve(cu));
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

    private BattleReinforcementGroup NewReserve(BattleSide side, int tile, BattleDomain domain) => new()
    {
        Side = side,
        OriginCampaignTile = tile,
        AvailableFromRound = ruleset.reinforcementStartRound,
        Domain = domain,
        EntryMethod = domain switch
        {
            BattleDomain.NavalSurface => BattleEntryMethod.NavalEdge,
            BattleDomain.Underwater => BattleEntryMethod.UnderwaterEdge,
            BattleDomain.Air => BattleEntryMethod.AirArrival,
            BattleDomain.Orbit => BattleEntryMethod.OrbitalArrival,
            BattleDomain.Space => BattleEntryMethod.SpaceArrival,
            _ => BattleEntryMethod.LandEdge,
        },
    };

    private static BattleDomain DomainForLayer(TileLayer layer) => layer switch
    {
        TileLayer.Underwater => BattleDomain.Underwater,
        TileLayer.Atmosphere => BattleDomain.Air,
        TileLayer.Orbit => BattleDomain.Orbit,
        _ => BattleDomain.Land,
    };

    private static void SetReserveDomain(BattleReinforcementGroup group, BattleDomain domain)
    {
        if (group.Units.Count > 0) return;
        group.Domain = domain;
        group.EntryMethod = domain switch
        {
            BattleDomain.NavalSurface => BattleEntryMethod.NavalEdge,
            BattleDomain.Underwater => BattleEntryMethod.UnderwaterEdge,
            BattleDomain.Air => BattleEntryMethod.AirArrival,
            BattleDomain.Orbit => BattleEntryMethod.OrbitalArrival,
            BattleDomain.Space => BattleEntryMethod.SpaceArrival,
            _ => BattleEntryMethod.LandEdge,
        };
    }

    private static void AddRuntimeIds(List<BattleUnitSnapshot> snapshots, HashSet<int> ids)
    {
        for (int i = 0; i < snapshots.Count; i++)
            if (snapshots[i].CampaignRuntimeId != 0)
                ids.Add(snapshots[i].CampaignRuntimeId);
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
