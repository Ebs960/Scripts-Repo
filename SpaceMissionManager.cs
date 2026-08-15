using System;
using UnityEngine;

public enum SpaceMissionKind
{
    SpaceStrike,
    CityBombardment,
    Recon
}

public enum SpaceMissionResult
{
    Invalid,
    Launched,
    Intercepted,
    Aborted,
    Completed
}

/// <summary>
/// Runtime coordinator for space/orbital missions, automatic interceptor defensive fire, and local anti-space fire.
/// Designers enable capabilities on CombatUnitData; UI/AI can call LaunchMission to resolve the full chain.
/// Mirrors the aircraft mission flow for space combat, interception, and gated passive defenses.
/// </summary>
public class SpaceMissionManager : MonoBehaviour
{
    public static SpaceMissionManager Instance { get; private set; }

    public event Action<CombatUnit, SpaceMissionKind, int, int> OnSpaceMissionLaunched;
    public event Action<CombatUnit, SpaceMissionKind, int, SpaceMissionResult> OnSpaceMissionResolved;
    /// <summary>Fired when an interceptor fires on a spacecraft; bool is true only when the spacecraft was destroyed and the mission stopped.</summary>
    public event Action<CombatUnit, CombatUnit, int, bool> OnSpaceIntercepted;
    /// <summary>Fired when local anti-space defense fires; bool is true only when the spacecraft was destroyed and the mission stopped.</summary>
    public event Action<CombatUnit, CombatUnit, int, bool> OnAntiSpaceEngaged;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    public SpaceMissionResult LaunchMission(CombatUnit spacecraft, SpaceMissionKind missionKind, int targetTileIndex)
    {
        int planetIndex = spacecraft != null ? spacecraft.planetIndex : -1;
        StrategicMissionResult outcome = StrategicMissionResolver.Execute(
            spacecraft,
            missionKind,
            targetTileIndex,
            CanLaunchMission,
            () => OnSpaceMissionLaunched?.Invoke(spacecraft, missionKind, targetTileIndex, planetIndex),
            () => FindBestInterceptor(spacecraft.owner, planetIndex, spacecraft.currentTileIndex, targetTileIndex),
            interceptor => ResolveInterception(interceptor, spacecraft, targetTileIndex),
            () => FindBestAntiSpaceDefender(spacecraft.owner, planetIndex, targetTileIndex),
            defender => ResolveAntiSpace(defender, spacecraft, targetTileIndex),
            () => ResolveMissionEffect(spacecraft, missionKind, targetTileIndex),
            nameof(SpaceMissionManager));

        SpaceMissionResult result = (SpaceMissionResult)(int)outcome;
        if (result != SpaceMissionResult.Invalid)
            OnSpaceMissionResolved?.Invoke(spacecraft, missionKind, targetTileIndex, result);
        return result;
    }

    public bool CanLaunchMission(CombatUnit spacecraft, SpaceMissionKind missionKind, int targetTileIndex, out string reason)
    {
        reason = null;
        if (spacecraft == null || spacecraft.data == null) { reason = "missing spacecraft or unit data"; return false; }
        bool isSpacePlatform = CombatUnitData.IsSpaceCategory(spacecraft.data.unitType) || spacecraft.IsInOrbit || spacecraft.data.canLaunchSpaceMissions;
        if (!isSpacePlatform)
        {
            reason = $"{spacecraft.UnitName} is not a space mission platform";
            return false;
        }
        if (!spacecraft.HasAttackPoints()) { reason = $"{spacecraft.UnitName} has no action points"; return false; }
        if (targetTileIndex < 0) { reason = "invalid target tile"; return false; }
        if (!SupportsMission(spacecraft.data, spacecraft.IsInOrbit, missionKind)) { reason = $"{spacecraft.UnitName} does not support {missionKind}"; return false; }

        var ts = TileSystem.GetForPlanet(spacecraft.planetIndex) ?? TileSystem.Instance;
        if (ts == null) { reason = "no tile system"; return false; }

        int range = GetSpaceMissionRange(spacecraft);
        int distance = Mathf.RoundToInt(ts.GetTileDistance(spacecraft.currentTileIndex, targetTileIndex));
        if (distance > range) { reason = $"target is out of space mission range ({distance}>{range})"; return false; }
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
            if (!AircraftMissionManager.IsHostile(candidate.owner, attackerOwner)) continue;

            int range = Mathf.Max(0, candidate.data.spaceInterceptionRange);
            int targetDistance = Mathf.RoundToInt(ts.GetTileDistance(candidate.currentTileIndex, targetTile));
            int sourceDistance = sourceTile >= 0 ? Mathf.RoundToInt(ts.GetTileDistance(candidate.currentTileIndex, sourceTile)) : int.MaxValue;
            if (targetDistance > range && sourceDistance > range) continue;

            float score = candidate.CurrentSpaceAttack + candidate.CurrentRange - Mathf.Min(targetDistance, sourceDistance);
            if (score > bestScore) { best = candidate; bestScore = score; }
        }

        return best;
    }

    public CombatUnit FindBestAntiSpaceDefender(Civilization attackerOwner, int planetIndex, int targetTile)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return null;

        CombatUnit best = null;
        float bestScore = float.MinValue;
        foreach (CombatUnit candidate in UnitRegistry.GetCombatUnits())
        {
            if (!IsValidAntiSpace(candidate) || candidate.planetIndex != planetIndex) continue;
            if (!AircraftMissionManager.IsHostile(candidate.owner, attackerOwner)) continue;

            int range = Mathf.Max(0, candidate.data.antiSpaceRange);
            int distance = Mathf.RoundToInt(ts.GetTileDistance(candidate.currentTileIndex, targetTile));
            if (distance > range) continue;

            float score = candidate.CurrentSpaceAttack + candidate.CurrentDefense - distance;
            if (score > bestScore) { best = candidate; bestScore = score; }
        }
        return best;
    }

    public bool ResolveInterception(CombatUnit interceptor, CombatUnit spacecraft, int targetTile)
    {
        if (interceptor == null || spacecraft == null || interceptor.data == null) return false;

        bool hit = RollDefensiveFire(interceptor, spacecraft, interceptor.data.spaceInterceptionChance);
        bool spacecraftDestroyed = false;
        bool interceptorDestroyed = false;
        int damage = 0;
        int returnDamage = 0;
        if (hit)
        {
            int interceptorAttack = interceptor.CurrentSpaceAttack;
            int spacecraftAttack = spacecraft.CurrentSpaceAttack;
            int interceptorDefense = interceptor.CurrentDefense;
            int spacecraftDefense = spacecraft.CurrentDefense;

            damage = CalculateSpaceCombatDamage(interceptorAttack, spacecraftDefense);
            returnDamage = spacecraftAttack > 0 ? CalculateSpaceCombatDamage(spacecraftAttack, interceptorDefense) : 0;

            spacecraftDestroyed = damage > 0 && spacecraft.ApplyDamage(damage, interceptor, false);
            interceptorDestroyed = returnDamage > 0 && interceptor.ApplyDamage(returnDamage, spacecraft, false);
        }

        interceptor.TryConsumeAttackPoint();
        OnSpaceIntercepted?.Invoke(interceptor, spacecraft, targetTile, spacecraftDestroyed);
        NotifySpaceEvent(interceptor, spacecraft, spacecraftDestroyed ? "destroyed" : (hit ? "damaged" : "missed"), damage);
        if (returnDamage > 0)
            NotifySpaceEvent(spacecraft, interceptor, interceptorDestroyed ? "destroyed" : "damaged", returnDamage);
        return spacecraftDestroyed;
    }

    public bool ResolveAntiSpace(CombatUnit defender, CombatUnit spacecraft, int targetTile)
    {
        if (defender == null || spacecraft == null || defender.data == null) return false;

        bool hit = RollDefensiveFire(defender, spacecraft, defender.data.antiSpaceInterceptionChance);
        bool spacecraftDestroyed = false;
        int damage = 0;
        if (hit)
        {
            int attack = defender.data.antiSpaceDamage > 0 ? defender.data.antiSpaceDamage : defender.CurrentSpaceAttack;
            damage = CalculateSpaceCombatDamage(attack, spacecraft.CurrentDefense);
            spacecraftDestroyed = damage > 0 && spacecraft.ApplyDamage(damage, defender, false);
        }

        OnAntiSpaceEngaged?.Invoke(defender, spacecraft, targetTile, spacecraftDestroyed);
        NotifySpaceEvent(defender, spacecraft, spacecraftDestroyed ? "shot down" : (hit ? "damaged" : "missed"), damage);
        return spacecraftDestroyed;
    }

    public static int GetSpaceMissionRange(CombatUnit spacecraft)
    {
        if (spacecraft == null || spacecraft.data == null) return 0;
        if (spacecraft.data.spaceMissionRange > 0) return spacecraft.data.spaceMissionRange;
        return Mathf.Max(1, Mathf.FloorToInt(spacecraft.CurrentRange));
    }

    public static bool IsValidAntiSpace(CombatUnit unit)
    {
        return unit != null && unit.data != null && unit.currentHealth > 0
               && unit.data.canAttackSpace && unit.data.canProvideAntiSpace;
    }

    public static bool IsValidInterceptor(CombatUnit unit)
    {
        return unit != null && unit.data != null && unit.currentHealth > 0 && unit.HasAttackPoints()
               && unit.data.canAttackSpace
               && unit.data.canInterceptSpaceMissions;
    }

    private static bool SupportsMission(CombatUnitData data, bool isInOrbit, SpaceMissionKind missionKind)
    {
        bool isSpace = CombatUnitData.IsSpaceCategory(data.unitType) || isInOrbit;
        switch (missionKind)
        {
            case SpaceMissionKind.SpaceStrike: return data.canSpaceStrike || isSpace;
            case SpaceMissionKind.CityBombardment: return data.canBombardCitiesFromSpace || data.canBombardSurface || isInOrbit;
            case SpaceMissionKind.Recon: return data.canReconSpaceMission || isSpace;
            default: return false;
        }
    }

    private static bool RollDefensiveFire(CombatUnit defender, CombatUnit target, float baseChance)
    {
        float evasion = target?.data != null ? target.data.spaceInterceptionEvasion : 0f;
        return StrategicMissionResolver.RollDefensiveFire(defender.CurrentSpaceAttack, target.CurrentDefense, baseChance, evasion);
    }

    private static void ResolveMissionEffect(CombatUnit spacecraft, SpaceMissionKind missionKind, int targetTile)
    {
        switch (missionKind)
        {
            case SpaceMissionKind.SpaceStrike:
                DamageUnitsOnTile(targetTile, spacecraft.planetIndex, spacecraft);
                break;
            case SpaceMissionKind.CityBombardment:
                DamageCityOnTile(targetTile, spacecraft.planetIndex, spacecraft);
                break;
            case SpaceMissionKind.Recon:
                UIManager.Instance?.ShowNotification($"{spacecraft.UnitName} completed a space reconnaissance mission.");
                break;
        }
    }

    private static void DamageUnitsOnTile(int tileIndex, int planetIndex, CombatUnit spacecraft)
    {
        if (spacecraft == null) return;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ == null) return;

        TileLayer[] layers = { TileLayer.Surface, TileLayer.Underwater, TileLayer.Atmosphere, TileLayer.Orbit };
        foreach (TileLayer layer in layers)
        {
            foreach (GameObject go in occ.GetAllOccupantObjects(tileIndex, layer))
            {
                if (go == null) continue;
                var combat = go.GetComponent<CombatUnit>();
                if (combat != null && AircraftMissionManager.IsHostile(combat.owner, spacecraft.owner))
                {
                    int attack = layer == TileLayer.Orbit ? spacecraft.CurrentSpaceAttack : spacecraft.CurrentGroundAttack;
                    int damage = CalculateStrikeDamage(attack, combat.CurrentDefense);
                    if (damage > 0) combat.ApplyDamage(damage, spacecraft, false);
                    continue;
                }
                var worker = go.GetComponent<WorkerUnit>();
                if (worker != null && AircraftMissionManager.IsHostile(worker.owner, spacecraft.owner))
                {
                    int damage = CalculateStrikeDamage(spacecraft.CurrentGroundAttack, worker.CurrentDefense);
                    if (damage > 0) worker.ApplyDamage(damage, spacecraft, false);
                }
            }
        }
    }

    private static void DamageCityOnTile(int tileIndex, int planetIndex, CombatUnit spacecraft)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tile = ts != null ? ts.GetTileData(tileIndex) : null;
        var city = tile != null ? tile.controllingCity : null;
        if (city == null || spacecraft == null || !AircraftMissionManager.IsHostile(city.owner, spacecraft.owner)) return;

        int damage = Mathf.Max(0, spacecraft.CurrentCityAttack > 0 ? spacecraft.CurrentCityAttack : spacecraft.CurrentSpaceAttack);
        if (damage <= 0) return;

        city.defenseRating = Mathf.Max(0, city.defenseRating - damage);
        UIManager.Instance?.ShowNotification($"{city.cityName} was bombarded from space! Defense: {city.defenseRating}/{city.maxDefense}");
    }

    private static int CalculateStrikeDamage(int attack, int defense)
    {
        return StrategicMissionResolver.CalculateDamage(attack, defense);
    }

    private static int CalculateSpaceCombatDamage(int attack, int defense)
    {
        return StrategicMissionResolver.CalculateDamage(attack, defense);
    }

    private static void NotifySpaceEvent(CombatUnit defender, CombatUnit spacecraft, string verb, int damage)
    {
        if (defender == null || spacecraft == null) return;
        if ((defender.owner != null && defender.owner.isPlayerControlled) || (spacecraft.owner != null && spacecraft.owner.isPlayerControlled))
        {
            string damageText = damage > 0 ? $" for {damage} damage" : string.Empty;
            UIManager.Instance?.ShowNotification($"{defender.UnitName} {verb} {spacecraft.UnitName}{damageText}.");
        }
    }
}
