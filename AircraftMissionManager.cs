// Assets/Scripts Repo/AircraftMissionManager.cs
using System;
using UnityEngine;

public enum AircraftMissionKind
{
    AirStrike,
    CityBombardment,
    Recon
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
/// Runtime coordinator for aircraft missions, automatic interceptor defensive fire, and local anti-air fire.
/// Designers enable capabilities on CombatUnitData; UI/AI can call LaunchMission to resolve the full chain.
/// </summary>
public class AircraftMissionManager : MonoBehaviour
{
    public static AircraftMissionManager Instance { get; private set; }

    public event Action<CombatUnit, AircraftMissionKind, int, int> OnAircraftMissionLaunched;
    public event Action<CombatUnit, AircraftMissionKind, int, AircraftMissionResult> OnAircraftMissionResolved;
    /// <summary>Fired when an interceptor fires on an aircraft; bool is true only when the aircraft was destroyed and the mission stopped.</summary>
    public event Action<CombatUnit, CombatUnit, int, bool> OnAircraftIntercepted;
    /// <summary>Fired when local anti-air fires on an aircraft; bool is true only when the aircraft was destroyed and the mission stopped.</summary>
    public event Action<CombatUnit, CombatUnit, int, bool> OnAntiAirEngaged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
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

        if (aircraft.currentHealth <= 0)
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

            float score = candidate.CurrentAirAttack + candidate.CurrentRange - Mathf.Min(targetDistance, sourceDistance);
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

        bool hit = RollDefensiveFire(interceptor, aircraft, interceptor.data.interceptionChance);
        bool aircraftDestroyed = false;
        bool interceptorDestroyed = false;
        int damage = 0;
        int returnDamage = 0;
        if (hit)
        {
            int interceptorAttack = interceptor.CurrentAirAttack;
            int aircraftAttack = aircraft.CurrentAirAttack;
            int interceptorDefense = interceptor.CurrentDefense;
            int aircraftDefense = aircraft.CurrentDefense;

            damage = CalculateAirCombatDamage(interceptorAttack, aircraftDefense);
            returnDamage = aircraftAttack > 0 ? CalculateAirCombatDamage(aircraftAttack, interceptorDefense) : 0;

            aircraftDestroyed = damage > 0 && aircraft.ApplyDamage(damage, interceptor, false);
            interceptorDestroyed = returnDamage > 0 && interceptor.ApplyDamage(returnDamage, aircraft, false);
        }

        interceptor.TryConsumeAttackPoint();
        OnAircraftIntercepted?.Invoke(interceptor, aircraft, targetTile, aircraftDestroyed);
        NotifyAirEvent(interceptor, aircraft, aircraftDestroyed ? "destroyed" : (hit ? "damaged" : "missed"), damage);
        if (returnDamage > 0)
            NotifyAirEvent(aircraft, interceptor, interceptorDestroyed ? "destroyed" : "damaged", returnDamage);
        return aircraftDestroyed;
    }

    public bool ResolveAntiAir(CombatUnit defender, CombatUnit aircraft, int targetTile)
    {
        if (defender == null || aircraft == null || defender.data == null) return false;

        bool hit = RollDefensiveFire(defender, aircraft, defender.data.antiAirInterceptionChance);
        bool aircraftDestroyed = false;
        int damage = 0;
        if (hit)
        {
            int attack = defender.data.antiAirDamage > 0 ? defender.data.antiAirDamage : defender.CurrentAirAttack;
            damage = CalculateAirCombatDamage(attack, aircraft.CurrentDefense);
            aircraftDestroyed = damage > 0 && aircraft.ApplyDamage(damage, defender, false);
        }

        OnAntiAirEngaged?.Invoke(defender, aircraft, targetTile, aircraftDestroyed);
        NotifyAirEvent(defender, aircraft, aircraftDestroyed ? "shot down" : (hit ? "damaged" : "missed"), damage);
        return aircraftDestroyed;
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
               && unit.data.canAttackAir && unit.data.canProvideAntiAir;
    }

    public static bool IsValidInterceptor(CombatUnit unit)
    {
        return unit != null && unit.data != null && unit.currentHealth > 0 && unit.HasAttackPoints()
               && unit.data.canAttackAir
               && unit.data.canInterceptAirMissions;
    }

    private static bool SupportsMission(CombatUnitData data, AircraftMissionKind missionKind)
    {
        bool isAir = CombatUnitData.IsAirCategory(data.unitType);
        switch (missionKind)
        {
            case AircraftMissionKind.AirStrike: return data.canAirStrike || isAir;
            case AircraftMissionKind.CityBombardment: return data.canBombardCitiesFromAir || data.unitType == CombatCategory.Bomber;
            case AircraftMissionKind.Recon: return data.canReconAirMission || isAir;
            default: return false;
        }
    }

    private static bool RollDefensiveFire(CombatUnit defender, CombatUnit aircraft, float baseChance)
    {
        float attack = Mathf.Max(1f, defender.CurrentAirAttack);
        float defense = Mathf.Max(1f, aircraft.CurrentDefense);
        float statFactor = attack / (attack + defense);
        float hitChance = Mathf.Clamp01(baseChance + (statFactor - 0.5f));
        if (UnityEngine.Random.value > hitChance) return false;

        float evasionChance = aircraft?.data != null ? Mathf.Clamp01(aircraft.data.interceptionEvasion) : 0f;
        return evasionChance <= 0f || UnityEngine.Random.value > evasionChance;
    }

    private static void ResolveMissionEffect(CombatUnit aircraft, AircraftMissionKind missionKind, int targetTile)
    {
        switch (missionKind)
        {
            case AircraftMissionKind.AirStrike:
                DamageUnitsOnTile(targetTile, aircraft.planetIndex, aircraft);
                break;
            case AircraftMissionKind.CityBombardment:
                DamageCityOnTile(targetTile, aircraft.planetIndex, aircraft);
                break;
            case AircraftMissionKind.Recon:
                UIManager.Instance?.ShowNotification($"{aircraft.UnitName} completed an aerial reconnaissance mission.");
                break;
        }
    }

    private static void DamageUnitsOnTile(int tileIndex, int planetIndex, CombatUnit aircraft)
    {
        if (aircraft == null) return;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ == null) return;

        TileLayer[] layers = { TileLayer.Surface, TileLayer.Underwater, TileLayer.Atmosphere };
        foreach (TileLayer layer in layers)
        {
            foreach (GameObject go in occ.GetAllOccupantObjects(tileIndex, layer))
            {
                if (go == null) continue;
                var combat = go.GetComponent<CombatUnit>();
                if (combat != null && IsHostile(combat.owner, aircraft.owner))
                {
                    int damage = CalculateStrikeDamage(aircraft.CurrentGroundAttack, combat.CurrentDefense);
                    if (damage > 0) combat.ApplyDamage(damage, aircraft, false);
                    continue;
                }
                var worker = go.GetComponent<WorkerUnit>();
                if (worker != null && IsHostile(worker.owner, aircraft.owner))
                {
                    int damage = CalculateStrikeDamage(aircraft.CurrentGroundAttack, worker.CurrentDefense);
                    if (damage > 0) worker.ApplyDamage(damage, aircraft, false);
                }
            }
        }
    }

    private static void DamageCityOnTile(int tileIndex, int planetIndex, CombatUnit aircraft)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tile = ts != null ? ts.GetTileData(tileIndex) : null;
        var city = tile != null ? tile.controllingCity : null;
        if (city == null || aircraft == null || !IsHostile(city.owner, aircraft.owner)) return;

        int damage = Mathf.Max(0, aircraft.CurrentCityAttack);
        if (damage <= 0) return;

        city.defenseRating = Mathf.Max(0, city.defenseRating - damage);
        UIManager.Instance?.ShowNotification($"{city.cityName} was bombed from the air! Defense: {city.defenseRating}/{city.maxDefense}");
    }


    private static int CalculateStrikeDamage(int attack, int defense)
    {
        return Mathf.Max(0, attack - Mathf.Max(0, defense));
    }

    private static int CalculateAirCombatDamage(int attack, int defense)
    {
        return Mathf.Max(0, attack - Mathf.Max(0, defense));
    }

    private static void NotifyAirEvent(CombatUnit defender, CombatUnit aircraft, string verb, int damage)
    {
        if (defender == null || aircraft == null) return;
        if ((defender.owner != null && defender.owner.isPlayerControlled) || (aircraft.owner != null && aircraft.owner.isPlayerControlled))
        {
            string damageText = damage > 0 ? $" for {damage} damage" : string.Empty;
            UIManager.Instance?.ShowNotification($"{defender.UnitName} {verb} {aircraft.UnitName}{damageText}.");
        }
    }
}
