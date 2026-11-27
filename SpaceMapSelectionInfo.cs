using System.Collections.Generic;
using System.Text;
using UnityEngine;

public enum SpaceMapSelectionKind { None, Planet, Fleet, Ship, Sector }

/// <summary>
/// Read-only lookups and display text for space map selections. It owns no state and
/// never copies gameplay data: every value is read from the live fleet, unit, planet,
/// and space-world objects each time it is asked.
/// </summary>
public static class SpaceMapSelectionInfo
{
    public const string LabelColor = "#C8A45A";

    // ── Lookups ─────────────────────────────────────────────────────────────

    public static void CollectFleets(List<SpaceFleet> results)
    {
        results.Clear();
        var managed = SpaceFleetManager.Instance != null ? SpaceFleetManager.Instance.fleets : null;
        if (managed != null)
            foreach (var fleet in managed)
                if (fleet != null && !results.Contains(fleet)) results.Add(fleet);
        var world = SpaceWorldManager.Instance != null ? SpaceWorldManager.Instance.CurrentSystem?.fleets : null;
        if (world != null)
            foreach (var fleet in world)
                if (fleet != null && !results.Contains(fleet)) results.Add(fleet);
    }

    public static SpaceFleet FindFleet(int fleetId)
    {
        if (fleetId < 0) return null;
        if (SpaceFleetManager.Instance != null)
        {
            var managed = SpaceFleetManager.Instance.GetFleet(fleetId);
            if (managed != null) return managed;
        }
        var world = SpaceWorldManager.Instance;
        if (world == null) return null;
        if (world.Entities.TryGetFleet(fleetId, out var indexed)) return indexed;
        return world.CurrentSystem?.fleets?.Find(f => f != null && f.fleetId == fleetId);
    }

    /// <summary>Resolves a unit by runtime id through the unit registry, which also holds inactive packed units.</summary>
    public static BaseUnit ResolveUnit(int runtimeId)
    {
        GameObject go = UnitRegistry.GetObject(runtimeId);
        if (go != null && go.TryGetComponent<BaseUnit>(out var unit)) return unit;
        if (SpaceWorldManager.Instance != null && SpaceWorldManager.Instance.Entities.TryGetShip(runtimeId, out var ship) && ship != null)
            return ship;
        return null;
    }

    public static bool IsAlive(BaseUnit unit) => unit != null && unit.currentHealth > 0;

    /// <summary>The fleet a unit belongs to, only when the fleet also lists the unit as a member.</summary>
    public static SpaceFleet GetFleetOf(BaseUnit unit)
    {
        if (unit == null) return null;
        var fleet = FindFleet(unit.spaceFleetId);
        return fleet != null && fleet.memberUnitIds != null && fleet.memberUnitIds.Contains(unit.gameObject.GetRuntimeId()) ? fleet : null;
    }

    public static Civilization PlayerCivilization => CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;

    public static Civilization ResolveCivilization(int civilizationId)
    {
        if (CivilizationManager.Instance == null) return null;
        foreach (var civ in CivilizationManager.Instance.GetAllCivs())
            if (civ != null && civ.gameObject.GetRuntimeId() == civilizationId) return civ;
        return null;
    }

    /// <summary>Owner from the fleet's recorded owner id, falling back to the owner of its own member units.</summary>
    public static Civilization GetFleetOwner(SpaceFleet fleet)
    {
        if (fleet == null) return null;
        var civ = ResolveCivilization(fleet.ownerCivilizationId);
        if (civ != null || fleet.memberUnitIds == null) return civ;
        foreach (int id in fleet.memberUnitIds)
        {
            var unit = ResolveUnit(id);
            if (unit != null && unit.owner != null) return unit.owner;
        }
        return null;
    }

    public static bool IsPlayerOwned(Civilization owner)
    {
        var player = PlayerCivilization;
        return player != null && owner == player;
    }

    public static string CivilizationName(Civilization civ)
    {
        if (civ == null) return "Unknown";
        return civ.civData != null && !string.IsNullOrEmpty(civ.civData.civName) ? civ.civData.civName : civ.name;
    }

    public static GameManager.PlanetData FindPlanetByBodyId(int bodyId)
    {
        var planets = GameManager.Instance != null ? GameManager.Instance.GetPlanetData() : null;
        if (planets == null || bodyId < 0) return null;
        foreach (var planet in planets.Values)
            if (planet != null && planet.celestialBodyId == bodyId) return planet;
        return planets.TryGetValue(bodyId, out var byIndex) ? byIndex : null;
    }

    public static GameManager.PlanetData FindPlanetByIndex(int planetIndex)
    {
        var planets = GameManager.Instance != null ? GameManager.Instance.GetPlanetData() : null;
        return planets != null && planets.TryGetValue(planetIndex, out var planet) ? planet : null;
    }

    /// <summary>Planet details are known for the homeworld, the planet being played, and explored planets.</summary>
    public static bool IsPlanetDiscovered(GameManager.PlanetData planet)
    {
        if (planet == null) return false;
        int current = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : -1;
        return planet.isExplored || planet.isHomeWorld || planet.planetIndex == current;
    }

    /// <summary>Features flagged hidden-until-detected stay hidden until the player civilization discovers them.</summary>
    public static bool IsFeatureKnown(SpaceFeatureInstance feature, SpaceFeatureData data)
    {
        if (feature == null || feature.destroyed) return false;
        if (data == null || !data.hiddenUntilDetected) return true;
        var player = PlayerCivilization;
        if (player == null) return feature.discovered;
        return feature.civilizationsThatDiscoveredIt != null && feature.civilizationsThatDiscoveredIt.Contains(player.gameObject.GetRuntimeId());
    }

    // ── Text ────────────────────────────────────────────────────────────────

    public static string Label(string label, string value) => $"<color={LabelColor}>{label}</color>  {value}";

    public static string Nicify(object value)
    {
        string raw = value != null ? value.ToString() : string.Empty;
        var sb = new StringBuilder(raw.Length + 4);
        for (int i = 0; i < raw.Length; i++)
        {
            char c = raw[i];
            if (c == '_') { sb.Append(' '); continue; }
            if (i > 0 && char.IsUpper(c) && char.IsLower(raw[i - 1])) sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    public static string SectorName(int tileIndex)
    {
        var tile = SpaceWorldManager.Instance != null ? SpaceWorldManager.Instance.Grid?.GetTile(tileIndex) : null;
        if (tile == null) return tileIndex >= 0 ? $"Sector {tileIndex}" : "Unknown sector";
        var planet = FindPlanetByBodyId(tile.planetId);
        if (planet != null) return planet.planetName;
        if (tile.isPlanetOrbitSector)
        {
            var orbited = FindPlanetByBodyId(tile.associatedPlanetId);
            if (orbited != null) return $"Sector {tileIndex}, orbit of {orbited.planetName}";
        }
        return $"Sector {tileIndex}";
    }

    public static string UnitName(BaseUnit unit)
    {
        if (unit == null) return "Unknown spacecraft";
        string name = unit.UnitName;
        return string.IsNullOrEmpty(name) || name == "Unknown" ? unit.name : name;
    }

    public static string UnitClass(BaseUnit unit)
    {
        var combat = unit as CombatUnit;
        return combat != null && combat.data != null ? Nicify(combat.data.unitType) : "Spacecraft";
    }

    public static Sprite UnitIcon(BaseUnit unit)
    {
        var combat = unit as CombatUnit;
        return combat != null && combat.data != null ? combat.data.icon : null;
    }

    public static int RemainingRoute(List<int> path, int cursor)
    {
        return path != null ? Mathf.Max(0, path.Count - 1 - cursor) : 0;
    }

    public static string UnitStatus(BaseUnit unit)
    {
        if (unit == null) return "Missing or destroyed";
        if (unit.currentHealth <= 0) return "Destroyed";
        if (unit.isPackedInSpaceFleet) return "Packed";
        if (RemainingRoute(unit.queuedSpacePath, unit.queuedSpacePathCursor) > 0) return "In transit";
        if (unit.spaceLocation.locationType == SpaceLocationType.PlanetOrbit) return "In orbit";
        if (unit.spaceLocation.locationType == SpaceLocationType.PlanetSurface && unit.currentSpaceTileIndex < 0) return "Landed";
        return unit.currentSpaceMovementPoints > 0 ? "Ready" : "No movement left";
    }

    public static string UnitLocation(BaseUnit unit)
    {
        if (unit == null) return "Unknown";
        if (unit.currentSpaceTileIndex >= 0) return SectorName(unit.currentSpaceTileIndex);
        var planet = FindPlanetByIndex(unit.spaceLocation.planetIndex >= 0 ? unit.spaceLocation.planetIndex : unit.planetIndex);
        string planetName = planet != null ? planet.planetName : "planet";
        switch (unit.spaceLocation.locationType)
        {
            case SpaceLocationType.PlanetOrbit: return $"Orbit of {planetName}";
            case SpaceLocationType.PlanetSurface: return $"Surface of {planetName}";
            case SpaceLocationType.Destroyed: return "Destroyed";
            default: return "Unknown";
        }
    }

    public static string UnitRoute(BaseUnit unit)
    {
        if (unit == null) return "None";
        int remaining = RemainingRoute(unit.queuedSpacePath, unit.queuedSpacePathCursor);
        if (remaining <= 0) return "None";
        int destination = unit.queuedSpacePath[unit.queuedSpacePath.Count - 1];
        return $"{remaining} to sector {destination}";
    }

    public static string UnitAbilities(BaseUnit unit)
    {
        if (unit == null) return "None";
        var names = new List<string>();
        if (unit.unlockedAbilities != null)
            foreach (var ability in unit.unlockedAbilities) AddAbilityName(names, ability);
        if (unit.EquipmentGrantedAbilities != null)
            foreach (var ability in unit.EquipmentGrantedAbilities) AddAbilityName(names, ability);
        return names.Count > 0 ? string.Join(", ", names) : "None";
    }

    private static void AddAbilityName(List<string> names, Ability ability)
    {
        if (ability != null && !string.IsNullOrEmpty(ability.abilityName) && !names.Contains(ability.abilityName))
            names.Add(ability.abilityName);
    }

    /// <summary>Missions the unit's data supports under SpaceMissionManager's own rules.</summary>
    public static string UnitMissions(BaseUnit unit)
    {
        var combat = unit as CombatUnit;
        if (combat == null || combat.data == null) return "None";
        bool platform = CombatUnitData.IsSpaceCategory(combat.data.unitType) || combat.IsInOrbit || combat.data.canLaunchSpaceMissions;
        if (!platform) return "None";
        var names = new List<string>();
        foreach (SpaceMissionKind kind in System.Enum.GetValues(typeof(SpaceMissionKind)))
            if (SpaceMissionManager.SupportsMission(combat.data, combat.IsInOrbit, kind)) names.Add(Nicify(kind));
        if (names.Count == 0) return "None";
        string text = $"{string.Join(", ", names)}\nRange {SpaceMissionManager.GetSpaceMissionRange(combat)}";
        return combat.HasAttackPoints() ? text : text + ", no action points";
    }

    public static string FleetStatus(SpaceFleet fleet)
    {
        if (fleet == null) return "Unknown";
        string state = fleet.isPacked ? "Packed" : fleet.isDeployed ? "Deployed" : "Unpacked";
        int remaining = RemainingRoute(fleet.queuedPath, fleet.queuedPathCursor);
        return remaining > 0 ? $"{state}, {remaining} sectors to go" : state;
    }

    public static string FleetAdmiral(SpaceFleet fleet)
    {
        if (fleet == null || fleet.admiralId < 0) return "No admiral assigned";
        var admiral = AdmiralManager.Instance != null ? AdmiralManager.Instance.GetAdmiral(fleet.admiralId) : null;
        if (admiral == null) return "Admiral record unavailable";
        string text = $"{admiral.admiralName}\nLevel {admiral.level}";
        return admiral.status == AdmiralStatus.Active ? text : $"{text}, {Nicify(admiral.status)}";
    }

    public static string ImprovementName(SpaceImprovementInstance instance, SpaceImprovementData data)
    {
        if (data != null && !string.IsNullOrEmpty(data.displayName)) return data.displayName;
        return instance != null && !string.IsNullOrEmpty(instance.improvementDataId) ? instance.improvementDataId : "Unknown structure";
    }

    public static string ImprovementStatus(SpaceImprovementInstance instance)
    {
        if (instance == null) return "Unknown";
        if (instance.currentHealth <= 0) return "Destroyed";
        if (instance.underConstruction) return "Building";
        if (instance.disabled) return "Disabled";
        return instance.operational ? "Operational" : "Offline";
    }
}
