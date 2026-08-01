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

        var decision = BattleTheaterResolver.ResolveBattleTheater(attacker, defender);
        if (!decision.IsValid)
        {
            preview.RejectionReason = decision.RejectionReason;
            return false;
        }
        preview.Theater = decision.Theater;
        preview.SpaceRegionId = decision.SpaceRegionId;
        preview.AllowsManualBattle = decision.AllowsManualBattle;
        preview.AllowsRetreat = decision.AllowsRetreat;
        preview.AllowsCancel = decision.AllowsCancel;

        // A single planetary battle space cannot mix tile indices from different
        // planets. Interplanetary scripted attacks use the documented fallback
        // until the campaign exposes a shared deep-space region identifier.
        if (decision.Theater != BattleTheater.DeepSpace && attacker.planetIndex != defender.planetIndex)
        {
            preview.RejectionReason = "different battle-space regions";
            return false;
        }

        preview.PlanetIndex = decision.PlanetIndex;
        preview.AnchorTile = decision.Theater == BattleTheater.DeepSpace ? defender.currentSpaceTileIndex : defender.currentTileIndex;
        preview.RandomSeed = BattleSeedBuilder.Build(attacker, defender, decision, GameManager.Instance != null ? GameManager.Instance.currentTurn : 0);

        var attackerUnits = CollectStackCombatUnits(attacker);
        var defenderUnits = CollectStackCombatUnits(defender);

        AssignFormationIdentity(attacker, attackerUnits);
        AssignFormationIdentity(defender, defenderUnits);

        AddSnapshots(attackerUnits, preview.AttackerUnits, preview.Theater);
        AddSnapshots(defenderUnits, preview.DefenderUnits, preview.Theater);

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

    private void AddSnapshots(List<CombatUnit> sourceUnits, List<BattleUnitSnapshot> snapshots, BattleTheater theater)
    {
        for (int i = 0; i < sourceUnits.Count; i++)
        {
            var cu = sourceUnits[i];
            if (cu == null || cu.currentHealth <= 0)
                continue;
            BattleDomain domain = BattleDomainResolver.Resolve(cu);
            bool assignedNavalAircraft = domain == BattleDomain.Air && cu.IsTransported
                && cu.TransportingUnit != null
                && BattleDomainResolver.Resolve(cu.TransportingUnit) == BattleDomain.NavalSurface;
            if (!BattleTheaterResolver.AllowsDomain(theater, domain, assignedNavalAircraft))
                continue;

            var profile = BattleProfileInference.Resolve(cu.data);
            int move = profile != null ? Mathf.Max(1, profile.tacticalMovePoints) : 3;
            int actions = profile != null ? Mathf.Max(1, profile.tacticalActionPoints) : 1;
            snapshots.Add(new BattleUnitSnapshot(cu, profile, move, actions));
        }
    }

    private static List<CombatUnit> CollectStackCombatUnits(CombatUnit root)
    {
        var list = new List<CombatUnit>();
        var seen = new HashSet<CombatUnit>();
        AddUnitAndCargo(root, list, seen);
        var stacked = root.GetStackedUnits();
        for (int i = 0; i < stacked.Count; i++)
        {
            if (stacked[i] is CombatUnit cu)
                AddUnitAndCargo(cu, list, seen);
        }

        return list;
    }

    private static void AddUnitAndCargo(CombatUnit unit, List<CombatUnit> result, HashSet<CombatUnit> seen)
    {
        if (unit == null || !seen.Add(unit))
            return;
        result.Add(unit);
        var cargo = unit.GetTransportedUnits();
        for (int i = 0; i < cargo.Count; i++)
            AddUnitAndCargo(cargo[i], result, seen);
    }

    private void BuildReinforcements(EngagementPreview preview, Civilization attackerCiv, Civilization defenderCiv)
    {
        if (preview.Theater == BattleTheater.DeepSpace)
        {
            BuildSpaceReinforcements(preview, attackerCiv, defenderCiv);
            return;
        }
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

                for (int i = 0; i < units.Count; i++)
                {
                    var cu = units[i] != null ? units[i].GetComponent<CombatUnit>() : null;
                    if (cu == null || cu.currentHealth <= 0)
                        continue;

                    if (cu == preview.Attacker || cu == preview.Defender)
                        continue;

                    BattleDomain candidateDomain = BattleDomainResolver.Resolve(cu);
                    // Underwater air support requires an explicit carrier/naval assignment;
                    // transported aircraft satisfy that relationship, arbitrary nearby air does not.
                    bool assignedNavalAircraft = candidateDomain == BattleDomain.Air && cu.IsTransported
                        && cu.TransportingUnit != null
                        && BattleDomainResolver.Resolve(cu.TransportingUnit) == BattleDomain.NavalSurface;
                    if (!BattleTheaterResolver.AllowsDomain(preview.Theater, candidateDomain, assignedNavalAircraft))
                        continue;

                    int runtimeId = cu.gameObject != null ? cu.gameObject.GetRuntimeId() : 0;
                    if (runtimeId != 0 && !participants.Add(runtimeId))
                        continue;

                    if (cu.owner == attackerCiv)
                    {
                        var profile = BattleProfileInference.Resolve(cu.data);
                        var snapshot = new BattleUnitSnapshot(cu, profile, profile != null ? profile.tacticalMovePoints : 3, profile != null ? profile.tacticalActionPoints : 1);
                        GetOrCreateReserve(preview, BattleSide.Attacker, tile, BattleDomainResolver.Resolve(cu), preview.Theater, snapshot.FormationId)
                            .Units.Add(snapshot);
                    }
                    else if (cu.owner == defenderCiv)
                    {
                        var profile = BattleProfileInference.Resolve(cu.data);
                        var snapshot = new BattleUnitSnapshot(cu, profile, profile != null ? profile.tacticalMovePoints : 3, profile != null ? profile.tacticalActionPoints : 1);
                        GetOrCreateReserve(preview, BattleSide.Defender, tile, BattleDomainResolver.Resolve(cu), preview.Theater, snapshot.FormationId)
                            .Units.Add(snapshot);
                    }
                }
            }

            var neigh = ts.GetNeighbors(tile);
            for (int i = 0; i < neigh.Length; i++)
            {
                if (seen.Add(neigh[i]))
                    queue.Enqueue((neigh[i], depth + 1));
            }
        }
    }

    private BattleReinforcementGroup GetOrCreateReserve(
        EngagementPreview preview,
        BattleSide side,
        int tile,
        BattleDomain domain,
        BattleTheater theater,
        string formationId)
    {
        for (int i = 0; i < preview.Reinforcements.Count; i++)
        {
            var group = preview.Reinforcements[i];
            if (group.Side == side
                && group.OriginCampaignTile == tile
                && group.Domain == domain
                && group.Theater == theater
                && group.FormationId == formationId)
                return group;
        }

        var reserve = NewReserve(side, tile, domain, theater, formationId);
        preview.Reinforcements.Add(reserve);
        return reserve;
    }

    private void BuildSpaceReinforcements(EngagementPreview preview, Civilization attackerCiv, Civilization defenderCiv)
    {
        var grid = SpaceWorldManager.Instance != null ? SpaceWorldManager.Instance.Grid
            : (SpaceCombatManager.Instance != null ? SpaceCombatManager.Instance.spaceGrid : null);
        if (grid == null)
            return;

        var participants = new HashSet<int>();
        AddRuntimeIds(preview.AttackerUnits, participants);
        AddRuntimeIds(preview.DefenderUnits, participants);

        foreach (var unit in UnitRegistry.GetCombatUnits())
        {
            if (unit == null || unit.currentHealth <= 0 || !BattleTheaterResolver.IsOnSpaceMap(unit))
                continue;

            int runtimeId = unit.gameObject != null ? unit.gameObject.GetRuntimeId() : 0;
            if (runtimeId == 0 || participants.Contains(runtimeId))
                continue;
            if (grid.GetDistance(unit.currentSpaceTileIndex, preview.AnchorTile) > ruleset.reinforcementRadius)
                continue;
            if (unit.owner != attackerCiv && unit.owner != defenderCiv)
                continue;
            if (!BattleTheaterResolver.AllowsDomain(BattleTheater.DeepSpace, BattleDomainResolver.Resolve(unit)))
                continue;

            participants.Add(runtimeId);
            unit.EnsureMilitaryFormationIdentity();
            var profile = BattleProfileInference.Resolve(unit.data);
            var snapshot = new BattleUnitSnapshot(unit, profile, profile != null ? profile.tacticalMovePoints : 3, profile != null ? profile.tacticalActionPoints : 1);
            BattleSide side = unit.owner == attackerCiv ? BattleSide.Attacker : BattleSide.Defender;
            var group = GetOrCreateReserve(preview, side, unit.currentSpaceTileIndex, BattleDomain.Space, BattleTheater.DeepSpace, snapshot.FormationId);
            group.OriginSpaceRegion = unit.currentSpaceTileIndex;
            group.Units.Add(snapshot);
        }
    }

    private BattleReinforcementGroup NewReserve(BattleSide side, int tile, BattleDomain domain, BattleTheater theater, string formationId) => new()
    {
        ReinforcementGroupId = StableReserveGroupId(side, tile, domain, formationId),
        FormationId = formationId,
        Side = side,
        Theater = theater,
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

    private static int StableReserveGroupId(BattleSide side, int tile, BattleDomain domain, string formationId)
    {
        unchecked
        {
            int hash = ((tile + 1) * 397) ^ ((int)side * 31) ^ (int)domain;
            if (!string.IsNullOrEmpty(formationId))
            {
                for (int i = 0; i < formationId.Length; i++)
                    hash = (hash * 31) ^ formationId[i];
            }
            return hash;
        }
    }

    private static void AssignFormationIdentity(CombatUnit root, List<CombatUnit> members)
    {
        if (root == null || members == null)
            return;

        string formationId = root.EnsureMilitaryFormationIdentity();
        MilitaryFormationType formationType = ResolveFormationType(root);
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i] != null)
                members[i].AssignMilitaryFormation(formationId, formationType, root.MilitaryFormationName);
        }
    }

    private static MilitaryFormationType ResolveFormationType(CombatUnit unit) => BattleDomainResolver.Resolve(unit) switch
    {
        BattleDomain.NavalSurface => MilitaryFormationType.SurfaceFleet,
        BattleDomain.Underwater => MilitaryFormationType.UnderwaterGroup,
        BattleDomain.Air => MilitaryFormationType.AirWing,
        BattleDomain.Orbit => MilitaryFormationType.OrbitalForce,
        BattleDomain.Space => MilitaryFormationType.SpaceFleet,
        _ => MilitaryFormationType.Army,
    };

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
