using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SpaceCombatAffectedUnit
{
    public int unitId;
    public int spaceTileIndex;
    public int predictedDamage;
    public bool isPrimaryTarget;
    public bool isFriendlyFire;
}

[Serializable]
public class SpaceCombatPreview
{
    public int attackerUnitId;
    public int primaryTargetUnitId;
    public List<int> affectedTileIndices = new List<int>();
    public List<SpaceCombatAffectedUnit> affectedUnits = new List<SpaceCombatAffectedUnit>();
    public bool targetMayCounterAttack;
    public int predictedCounterDamage;
}

public class SpaceCombatManager : MonoBehaviour
{
    public static SpaceCombatManager Instance { get; private set; }
    public SpaceHexGrid spaceGrid;
    public event Action<CombatUnit, CombatUnit, SpaceCombatPreview> OnSpaceAttackPreviewed;
    public event Action<CombatUnit, CombatUnit, SpaceCombatPreview> OnSpaceAttackResolved;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (spaceGrid == null) spaceGrid = new SpaceHexGrid();
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

    public bool CanDirectAttack(CombatUnit attacker, CombatUnit target, out string reason)
    {
        reason = null;
        if (attacker == null || target == null || attacker.data == null || target.data == null) { reason = "missing attacker or target"; return false; }
        if (attacker.currentHealth <= 0 || target.currentHealth <= 0) { reason = "attacker or target is destroyed"; return false; }
        if (!attacker.data.canDirectlyAttackSpacecraft || !attacker.data.canAttackSpace) { reason = "attacker cannot directly attack spacecraft"; return false; }
        if (!attacker.HasAttackPoints()) { reason = "attacker has no attack points"; return false; }
        if (!AircraftMissionManager.IsHostile(attacker.owner, target.owner)) { reason = "target is not hostile"; return false; }
        if (attacker.isPackedInSpaceFleet || target.isPackedInSpaceFleet) { reason = "packed fleets must unpack before ship combat"; return false; }
        int aTile = GetSpaceTile(attacker); int tTile = GetSpaceTile(target);
        if (spaceGrid == null || spaceGrid.GetTile(aTile) == null || spaceGrid.GetTile(tTile) == null) { reason = "attacker or target has no valid space tile"; return false; }
        int distance = spaceGrid.GetDistance(aTile, tTile);
        if (distance > Mathf.Max(1, attacker.data.directSpaceAttackRange)) { reason = $"target is out of hex range ({distance}>{attacker.data.directSpaceAttackRange})"; return false; }
        if (IsLineBlockedByPlanetOrTerrain(aTile, tTile)) { reason = "a planet or blocking terrain prevents the attack"; return false; }
        return true;
    }

    public SpaceCombatPreview BuildPreview(CombatUnit attacker, CombatUnit primaryTarget)
    {
        var preview = new SpaceCombatPreview { attackerUnitId = attacker != null ? attacker.gameObject.GetRuntimeId() : 0, primaryTargetUnitId = primaryTarget != null ? primaryTarget.gameObject.GetRuntimeId() : 0 };
        if (!CanDirectAttack(attacker, primaryTarget, out _)) return preview;
        int primaryTile = GetSpaceTile(primaryTarget);
        AddAffected(preview, attacker, primaryTarget, primaryTarget, 1f, true);
        if (attacker.data.spaceAttackPattern == SpaceAttackPattern.Blast && attacker.data.spaceBlastRadius > 0)
        {
            foreach (CombatUnit unit in UnitRegistry.GetCombatUnits())
            {
                if (unit == null || unit == primaryTarget || unit.currentHealth <= 0 || unit.isPackedInSpaceFleet) continue;
                int tile = GetSpaceTile(unit);
                if (spaceGrid.GetTile(tile) == null || spaceGrid.GetDistance(primaryTile, tile) > attacker.data.spaceBlastRadius) continue;
                bool hostile = AircraftMissionManager.IsHostile(attacker.owner, unit.owner);
                if (!hostile && !attacker.data.spaceBlastCanDamageFriendlies) continue;
                AddAffected(preview, attacker, primaryTarget, unit, Mathf.Clamp01(attacker.data.spaceBlastDamageMultiplier), false);
            }
        }
        preview.targetMayCounterAttack = CanCounterAttack(primaryTarget, attacker);
        preview.predictedCounterDamage = preview.targetMayCounterAttack ? CalculateAbilityModifiedSpaceDamage(primaryTarget, attacker) : 0;
        OnSpaceAttackPreviewed?.Invoke(attacker, primaryTarget, preview);
        return preview;
    }

    public bool ResolveDirectAttack(CombatUnit attacker, CombatUnit primaryTarget, out string reason)
    {
        if (!CanDirectAttack(attacker, primaryTarget, out reason)) return false;
        var preview = BuildPreview(attacker, primaryTarget);
        attacker.TryConsumeAttackPoint();
        var damaged = new HashSet<int>();
        bool primarySurvived = true;
        foreach (var hit in preview.affectedUnits)
        {
            var go = UnitRegistry.GetObject(hit.unitId);
            var unit = go != null ? go.GetComponent<CombatUnit>() : null;
            if (unit == null || !damaged.Add(hit.unitId) || hit.predictedDamage <= 0) continue;
            bool died = unit.ApplyDamage(hit.predictedDamage, attacker, false);
            AwardSpaceCombatExperience(attacker, unit, hit.predictedDamage, died);
            if (unit == primaryTarget) primarySurvived = !died && unit.currentHealth > 0;
        }
        if (primarySurvived && preview.targetMayCounterAttack && preview.predictedCounterDamage > 0)
        {
            primaryTarget.hasUsedSpaceReactionThisTurn = true;
            bool counterKilled = attacker.ApplyDamage(preview.predictedCounterDamage, primaryTarget, false);
            AwardSpaceCombatExperience(primaryTarget, attacker, preview.predictedCounterDamage, counterKilled);
        }
        OnSpaceAttackResolved?.Invoke(attacker, primaryTarget, preview);
        return true;
    }

    public static int CalculateSpaceCombatDamage(int attack, int defense) => Mathf.Max(0, attack - Mathf.Max(0, defense));

    public static int CalculateAbilityModifiedSpaceDamage(CombatUnit attacker, CombatUnit defender)
    {
        int attack = attacker != null ? attacker.CurrentSpaceAttack : 0;
        int defense = defender != null ? defender.CurrentDefense : 0;
        float damage = CalculateSpaceCombatDamage(attack, defense);
        if (attacker != null)
            damage *= attacker.GetAbilityDamageMultiplier() + attacker.GetAbilityAccuracyModifier();
        damage *= GetAdmiralAttackMultiplier(attacker);
        return Mathf.Max(0, Mathf.RoundToInt(damage));
    }

    private static float GetAdmiralAttackMultiplier(CombatUnit attacker)
    {
        if (attacker == null || attacker.spaceFleetId < 0 || AdmiralManager.Instance == null || SpaceFleetManager.Instance == null) return 1f;
        var fleet = SpaceFleetManager.Instance.GetFleet(attacker.spaceFleetId);
        return fleet == null ? 1f : AdmiralManager.Instance.GetFleetAttackMultiplier(fleet.admiralId);
    }

    private void AwardSpaceCombatExperience(CombatUnit attacker, CombatUnit defender, int damage, bool defenderDestroyed)
    {
        if (attacker == null || damage <= 0) return;
        attacker.GainExperience(damage);
        if (defenderDestroyed) attacker.GainExperience(damage);
    }

    private void AddAffected(SpaceCombatPreview preview, CombatUnit attacker, CombatUnit primaryTarget, CombatUnit victim, float multiplier, bool primary)
    {
        int damage = Mathf.RoundToInt(CalculateAbilityModifiedSpaceDamage(attacker, victim) * multiplier);
        int tile = GetSpaceTile(victim);
        if (!preview.affectedTileIndices.Contains(tile)) preview.affectedTileIndices.Add(tile);
        preview.affectedUnits.Add(new SpaceCombatAffectedUnit { unitId = victim.gameObject.GetRuntimeId(), spaceTileIndex = tile, predictedDamage = damage, isPrimaryTarget = primary, isFriendlyFire = !AircraftMissionManager.IsHostile(attacker.owner, victim.owner) });
    }

    private bool CanCounterAttack(CombatUnit defender, CombatUnit attacker)
    {
        if (defender == null || attacker == null || defender.data == null || defender.hasUsedSpaceReactionThisTurn) return false;
        if (!defender.data.canCounterAttackInSpace || !defender.data.canCounterAttack || defender.CurrentSpaceAttack <= 0) return false;
        return spaceGrid.GetDistance(GetSpaceTile(defender), GetSpaceTile(attacker)) <= Mathf.Max(1, defender.data.spaceCounterAttackRange);
    }

    private bool IsLineBlockedByPlanetOrTerrain(int from, int to)
    {
        if (from == to) return false;
        foreach (int tileIndex in GetStraightLineApproximation(from, to))
        {
            if (tileIndex == from || tileIndex == to) continue;
            var tile = spaceGrid.GetTile(tileIndex);
            if (tile != null && (tile.blocksMovement || tile.terrainType == SpaceTerrainType.Planet)) return true;
        }
        return false;
    }

    private IEnumerable<int> GetStraightLineApproximation(int from, int to)
    {
        var a = spaceGrid.GetTile(from); var b = spaceGrid.GetTile(to); if (a == null || b == null) yield break;
        int steps = Mathf.Max(1, spaceGrid.GetDistance(from, to));
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            int q = Mathf.RoundToInt(Mathf.Lerp(a.q, b.q, t));
            int r = Mathf.RoundToInt(Mathf.Lerp(a.r, b.r, t));
            if (spaceGrid.TryGetIndex(q, r, out int idx)) yield return idx;
        }
    }

    private static int GetSpaceTile(BaseUnit unit) => unit != null && unit.currentSpaceTileIndex >= 0 ? unit.currentSpaceTileIndex : unit != null ? unit.spaceLocation.spaceTileIndex : -1;
}
