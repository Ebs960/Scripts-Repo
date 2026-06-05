// Assets/Scripts Repo/AircraftMissionManager.cs
using System;
using System.Collections.Generic;
using UnityEngine;

public enum AircraftMissionKind
{
    AirStrike,
    CityBombardment,
    Recon,
    Patrol,
    Interception
}

public enum AircraftMissionResult
{
    Invalid,
    Launched,
    Intercepted,
    Aborted,
    Completed
}

/// <summary>
/// Runtime coordinator for aircraft missions, combat air patrol interception, and local anti-air fire.
/// Designers enable capabilities on CombatUnitData; UI/AI can call LaunchMission to resolve the full chain.
/// </summary>
public class AircraftMissionManager : MonoBehaviour
{
    public static AircraftMissionManager Instance { get; private set; }

    public event Action<CombatUnit, AircraftMissionKind, int, int> OnAircraftMissionLaunched;
    public event Action<CombatUnit, AircraftMissionKind, int, AircraftMissionResult> OnAircraftMissionResolved;
    public event Action<CombatUnit, CombatUnit, int, bool> OnAircraftIntercepted;
    public event Action<CombatUnit, CombatUnit, int, bool> OnAntiAirEngaged;

    private readonly HashSet<CombatUnit> activePatrols = new HashSet<CombatUnit>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public void RegisterPatrol(CombatUnit aircraft)
    {
        if (IsValidInterceptor(aircraft)) activePatrols.Add(aircraft);
    }

    public void ClearPatrol(CombatUnit aircraft)
    {
        if (aircraft != null) activePatrols.Remove(aircraft);
    }

    public void ClearAllPatrolsForOwner(Civilization owner)
    {
        activePatrols.RemoveWhere(unit => unit == null || unit.owner == owner);
    }

    public AircraftMissionResult LaunchMission(CombatUnit aircraft, AircraftMissionKind missionKind, int targetTileIndex)
    {
        if (!CanLaunchMission(aircraft, missionKind, targetTileIndex, out string reason))
        {
            Debug.LogWarning($"[AircraftMissionManager] Mission rejected: {reason}");
            return AircraftMissionResult.Invalid;
        }

        int planetIndex = aircraft.planetIndex;
        OnAircraftMissionLaunched?.Invoke(aircraft, missionKind, targetTileIndex, planetIndex);

        if (missionKind == AircraftMissionKind.Patrol || missionKind == AircraftMissionKind.Interception)
        {
            RegisterPatrol(aircraft);
            aircraft.TryConsumeAttackPoint();
            OnAircraftMissionResolved?.Invoke(aircraft, missionKind, targetTileIndex, AircraftMissionResult.Completed);
            return AircraftMissionResult.Completed;
        }

        CombatUnit interceptor = FindBestInterceptor(aircraft.owner, planetIndex, aircraft.currentTileIndex, targetTileIndex);
        if (ResolveInterception(interceptor, aircraft, targetTileIndex))
        {
            OnAircraftMissionResolved?.Invoke(aircraft, missionKind, targetTileIndex, AircraftMissionResult.Intercepted);
            return AircraftMissionResult.Intercepted;
        }

        CombatUnit antiAir = FindBestAntiAirDefender(aircraft.owner, planetIndex, targetTileIndex);
        if (ResolveAntiAir(antiAir, aircraft, targetTileIndex))
        {
            OnAircraftMissionResolved?.Invoke(aircraft, missionKind, targetTileIndex, AircraftMissionResult.Aborted);
            return AircraftMissionResult.Aborted;
        }

        aircraft.TryConsumeAttackPoint();
        ResolveMissionEffect(aircraft, missionKind, targetTileIndex);
        OnAircraftMissionResolved?.Invoke(aircraft, missionKind, targetTileIndex, AircraftMissionResult.Completed);
        return AircraftMissionResult.Completed;
    }

    public bool CanLaunchMission(CombatUnit aircraft, AircraftMissionKind missionKind, int targetTileIndex, out string reason)
    {
        reason = null;
        if (aircraft == null || aircraft.data == null) { reason = "missing aircraft or unit data"; return false; }
        if (!CombatUnitData.IsAirCategory(aircraft.data.unitType) && !aircraft.data.canLaunchAirMissions)
        {
            reason = $"{aircraft.UnitName} is not an aircraft mission platform";
            return false;
        }
        if (!aircraft.HasAttackPoints()) { reason = $"{aircraft.UnitName} has no action points"; return false; }
        if (targetTileIndex < 0) { reason = "invalid target tile"; return false; }
        if (!SupportsMission(aircraft.data, missionKind)) { reason = $"{aircraft.UnitName} does not support {missionKind}"; return false; }

        var ts = TileSystem.GetForPlanet(aircraft.planetIndex) ?? TileSystem.Instance;
        if (ts == null) { reason = "no tile system"; return false; }

        int range = GetAircraftMissionRange(aircraft);
        int distance = Mathf.RoundToInt(ts.GetTileDistance(aircraft.currentTileIndex, targetTileIndex));
        if (distance > range) { reason = $"target is out of aircraft mission range ({distance}>{range})"; return false; }
        return true;
    }

    public CombatUnit FindBestInterceptor(Civilization attackerOwner, int planetIndex, int sourceTile, int targetTile)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return null;

        CombatUnit best = null;
        float bestScore = float.MinValue;

        foreach (CombatUnit candidate in UnitRegistry.GetCombatUnits())
        {
            if (!IsValidInterceptor(candidate) || candidate.planetIndex != planetIndex) continue;
            if (!IsHostile(candidate.owner, attackerOwner)) continue;

            int range = Mathf.Max(0, candidate.data.interceptionRange);
            int targetDistance = Mathf.RoundToInt(ts.GetTileDistance(candidate.currentTileIndex, targetTile));
            int sourceDistance = sourceTile >= 0 ? Mathf.RoundToInt(ts.GetTileDistance(candidate.currentTileIndex, sourceTile)) : int.MaxValue;
            if (targetDistance > range && sourceDistance > range) continue;

            float patrolBonus = activePatrols.Contains(candidate) ? 1000f : 0f;
            float score = patrolBonus + candidate.CurrentAirAttack + candidate.CurrentRange - Mathf.Min(targetDistance, sourceDistance);
            if (score > bestScore) { best = candidate; bestScore = score; }
        }

        return best;
    }

    public CombatUnit FindBestAntiAirDefender(Civilization attackerOwner, int planetIndex, int targetTile)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return null;

        CombatUnit best = null;
        float bestScore = float.MinValue;
        foreach (CombatUnit candidate in UnitRegistry.GetCombatUnits())
        {
            if (!IsValidAntiAir(candidate) || candidate.planetIndex != planetIndex) continue;
            if (!IsHostile(candidate.owner, attackerOwner)) continue;

            int range = Mathf.Max(0, candidate.data.antiAirRange);
            int distance = Mathf.RoundToInt(ts.GetTileDistance(candidate.currentTileIndex, targetTile));
            if (distance > range) continue;

            float score = candidate.CurrentAirAttack + candidate.CurrentDefense - distance;
            if (score > bestScore) { best = candidate; bestScore = score; }
        }
        return best;
    }

    public bool ResolveInterception(CombatUnit interceptor, CombatUnit aircraft, int targetTile)
    {
        if (interceptor == null || aircraft == null || interceptor.data == null) return false;

        float chance = CalculateEngagementChance(interceptor, aircraft, interceptor.data.interceptionChance);
        bool stopped = UnityEngine.Random.value <= chance;
        int damage = Mathf.Max(1, Mathf.RoundToInt(interceptor.CurrentAirAttack * (stopped ? 1f : 0.35f)));
        aircraft.ApplyDamage(damage, interceptor, false);
        interceptor.TryConsumeAttackPoint();
        if (!stopped && activePatrols.Contains(interceptor)) activePatrols.Remove(interceptor);
        OnAircraftIntercepted?.Invoke(interceptor, aircraft, targetTile, stopped);
        NotifyAirEvent(interceptor, aircraft, stopped ? "intercepted" : "engaged", damage);
        return stopped;
    }

    public bool ResolveAntiAir(CombatUnit defender, CombatUnit aircraft, int targetTile)
    {
        if (defender == null || aircraft == null || defender.data == null) return false;

        float chance = CalculateEngagementChance(defender, aircraft, defender.data.antiAirInterceptionChance);
        bool stopped = UnityEngine.Random.value <= chance;
        int configuredDamage = defender.data.antiAirDamage > 0 ? defender.data.antiAirDamage : defender.CurrentAirAttack;
        int damage = Mathf.Max(1, Mathf.RoundToInt(configuredDamage * (stopped ? 1f : 0.5f)));
        aircraft.ApplyDamage(damage, defender, false);
        OnAntiAirEngaged?.Invoke(defender, aircraft, targetTile, stopped);
        NotifyAirEvent(defender, aircraft, stopped ? "shot down" : "damaged", damage);
        return stopped;
    }

    public static bool IsHostile(Civilization defender, Civilization attacker)
    {
        if (defender == null || attacker == null || defender == attacker) return false;
        if (DiplomacyManager.Instance != null)
            return DiplomacyManager.Instance.GetRelationship(defender, attacker) == DiplomaticState.War;
        return defender.relations != null
               && defender.relations.TryGetValue(attacker, out DiplomaticState state)
               && state == DiplomaticState.War;
    }

    public static int GetAircraftMissionRange(CombatUnit aircraft)
    {
        if (aircraft == null || aircraft.data == null) return 0;
        if (aircraft.data.airMissionRange > 0) return aircraft.data.airMissionRange;
        return Mathf.Max(1, Mathf.FloorToInt(aircraft.CurrentRange));
    }

    public static bool IsValidAntiAir(CombatUnit unit)
    {
        return unit != null && unit.data != null && unit.currentHealth > 0
               && unit.data.canAttackAir && (unit.data.canProvideAntiAir || unit.data.antiAirRange > 0 || unit.CurrentAirAttack > 0);
    }

    public static bool IsValidInterceptor(CombatUnit unit)
    {
        return unit != null && unit.data != null && unit.currentHealth > 0 && unit.HasAttackPoints()
               && unit.data.canAttackAir
               && (unit.data.canInterceptAirMissions || unit.data.canAirPatrol || CombatUnitData.IsAirCategory(unit.data.unitType));
    }

    private static bool SupportsMission(CombatUnitData data, AircraftMissionKind missionKind)
    {
        bool isAir = CombatUnitData.IsAirCategory(data.unitType);
        switch (missionKind)
        {
            case AircraftMissionKind.AirStrike: return data.canAirStrike || isAir;
            case AircraftMissionKind.CityBombardment: return data.canBombardCitiesFromAir || data.unitType == CombatCategory.Bomber;
            case AircraftMissionKind.Recon: return data.canReconAirMission || isAir;
            case AircraftMissionKind.Patrol:
            case AircraftMissionKind.Interception: return data.canAirPatrol || data.canInterceptAirMissions || data.unitType == CombatCategory.Fighter;
            default: return false;
        }
    }

    private static float CalculateEngagementChance(CombatUnit defender, CombatUnit aircraft, float baseChance)
    {
        float attack = Mathf.Max(1f, defender.CurrentAirAttack);
        float defense = Mathf.Max(1f, aircraft.CurrentDefense);
        float statFactor = attack / (attack + defense);
        return Mathf.Clamp01(baseChance + (statFactor - 0.5f));
    }

    private static void ResolveMissionEffect(CombatUnit aircraft, AircraftMissionKind missionKind, int targetTile)
    {
        int damage = aircraft.data.airMissionDamage > 0 ? aircraft.data.airMissionDamage : aircraft.CurrentAirAttack;
        switch (missionKind)
        {
            case AircraftMissionKind.AirStrike:
                DamageUnitsOnTile(targetTile, aircraft.planetIndex, damage, aircraft.owner);
                break;
            case AircraftMissionKind.CityBombardment:
                DamageCityOnTile(targetTile, aircraft.planetIndex, aircraft, damage);
                break;
            case AircraftMissionKind.Recon:
                UIManager.Instance?.ShowNotification($"{aircraft.UnitName} completed an aerial reconnaissance mission.");
                break;
        }
    }

    private static void DamageUnitsOnTile(int tileIndex, int planetIndex, int damage, Civilization attackerOwner)
    {
        if (damage <= 0) return;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ == null) return;

        TileLayer[] layers = { TileLayer.Surface, TileLayer.Underwater, TileLayer.Atmosphere };
        foreach (TileLayer layer in layers)
        {
            foreach (GameObject go in occ.GetAllOccupantObjects(tileIndex, layer))
            {
                if (go == null) continue;
                var combat = go.GetComponent<CombatUnit>();
                if (combat != null && IsHostile(combat.owner, attackerOwner)) { combat.ApplyDamage(damage); continue; }
                var worker = go.GetComponent<WorkerUnit>();
                if (worker != null && IsHostile(worker.owner, attackerOwner)) worker.ApplyDamage(damage);
            }
        }
    }

    private static void DamageCityOnTile(int tileIndex, int planetIndex, CombatUnit aircraft, int fallbackDamage)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tile = ts != null ? ts.GetTileData(tileIndex) : null;
        var city = tile != null ? tile.controllingCity : null;
        if (city == null || !IsHostile(city.owner, aircraft.owner)) return;

        int configured = aircraft.data.cityAirMissionDamage > 0
            ? aircraft.data.cityAirMissionDamage
            : Mathf.Max(1, Mathf.RoundToInt(fallbackDamage * 0.5f));
        city.defenseRating = Mathf.Max(0, city.defenseRating - configured);
        UIManager.Instance?.ShowNotification($"{city.cityName} was bombed from the air! Defense: {city.defenseRating}/{city.maxDefense}");
    }

    private static void NotifyAirEvent(CombatUnit defender, CombatUnit aircraft, string verb, int damage)
    {
        if (defender == null || aircraft == null) return;
        if ((defender.owner != null && defender.owner.isPlayerControlled) || (aircraft.owner != null && aircraft.owner.isPlayerControlled))
            UIManager.Instance?.ShowNotification($"{defender.UnitName} {verb} {aircraft.UnitName} for {damage} damage.");
    }
}
