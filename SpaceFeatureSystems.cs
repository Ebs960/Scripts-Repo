using System;
using System.Collections.Generic;
using UnityEngine;

public enum SpaceFeatureType { AsteroidField, DebrisField, Nebula, RadiationZone, CometIceField, GravityAnomaly, Wormhole, DerelictShip, AncientStation, ScientificAnomaly, PirateBase, SpaceCreatureHabitat, ResourceDeposit }

[Serializable] public class SpaceExplorationEventData { public string eventId; public string displayName; }

[CreateAssetMenu(fileName = "New Space Feature", menuName = "Data/Space/Space Feature")]
public class SpaceFeatureData : ScriptableObject
{
    [Header("Identity")] public string featureId; public string displayName; [TextArea] public string description; public SpaceFeatureType featureType; public Sprite icon; public GameObject mapPrefab;
    [Header("Placement")] public int minimumClusterSize = 1; public int maximumClusterSize = 1; public bool mayOccupyOrbitSector; public bool mayOccupyPlanetAnchor; public int minimumDistanceFromPlanet; public int maximumDistanceFromPlanet = -1;
    [Header("Movement")] public bool blocksMovement; public int movementCostModifier; public CombatCategory[] prohibitedUnitCategories;
    [Header("Vision")] public int visionModifier; public bool concealsShips; public int concealmentStrength; public bool hiddenUntilDetected; public int detectionDifficulty;
    [Header("Combat")] public int defenseModifier; public int accuracyModifier; public int damagePerTurn; public bool damagesAllShips; public int hazardResistanceRequired;
    [Header("Resources")] public SpaceResourceData resourceDeposit; public bool permitsExtraction;
    [Header("Exploration")] public bool mayTriggerEvent; public SpaceExplorationEventData[] possibleEvents;
}

[Serializable]
public class SpaceFeatureInstance
{
    public int instanceId; public string featureDataId; public List<int> occupiedTileIndices = new List<int>(); public bool discovered;
    [NonSerialized] public HashSet<int> civilizationsThatDiscoveredIt = new HashSet<int>();
    public List<int> civilizationDiscoverySaveIds = new List<int>(); public int remainingResourceQuantity; public bool eventResolved; public bool destroyed;
    public void BeforeSerialize() { civilizationDiscoverySaveIds = new List<int>(civilizationsThatDiscoveredIt); }
    public void AfterDeserialize() { civilizationsThatDiscoveredIt = new HashSet<int>(civilizationDiscoverySaveIds ?? new List<int>()); }
}

public class SpaceFeatureManager : MonoBehaviour
{
    public static SpaceFeatureManager Instance { get; private set; }
    [SerializeField] private List<SpaceFeatureData> featureDatabase = new List<SpaceFeatureData>();
    private Dictionary<string, SpaceFeatureData> byId = new Dictionary<string, SpaceFeatureData>();
    private void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; RebuildLookup(); }
    private void OnDestroy() { if (Instance == this) Instance = null; }
    public void RebuildLookup() { byId.Clear(); foreach (var d in featureDatabase) if (d != null && !string.IsNullOrEmpty(d.featureId)) byId[d.featureId] = d; }
    public SpaceFeatureInstance GetFeature(int id) => SpaceWorldManager.Instance?.CurrentSystem?.features.Find(f => f.instanceId == id && !f.destroyed);
    public SpaceFeatureData GetFeatureData(string id) { if (string.IsNullOrEmpty(id)) return null; byId.TryGetValue(id, out var d); return d; }
    public int GetMovementCost(int tileIndex, BaseUnit unit) { int c = 0; foreach (var d in GetDataOnTile(tileIndex)) c += d.movementCostModifier; return c; }
    public int GetVisionModifier(int tileIndex) { int c = 0; foreach (var d in GetDataOnTile(tileIndex)) c += d.visionModifier; return c; }
    public int GetDefenseModifier(int tileIndex) { int c = 0; foreach (var d in GetDataOnTile(tileIndex)) c += d.defenseModifier; return c; }
    public bool IsShipConcealed(int tileIndex, BaseUnit unit) { foreach (var d in GetDataOnTile(tileIndex)) if (d.concealsShips) return true; return false; }
    public void ProcessTurnHazards(Civilization civilization) { }
    public void DiscoverFeature(int featureId, Civilization civilization) { var f = GetFeature(featureId); if (f == null) return; f.discovered = true; if (civilization != null) f.civilizationsThatDiscoveredIt.Add(civilization.gameObject.GetRuntimeId()); }
    public void ResolveFeatureInteraction(int featureId, Civilization civilization) { var f = GetFeature(featureId); if (f != null) f.eventResolved = true; }
    private IEnumerable<SpaceFeatureData> GetDataOnTile(int tileIndex)
    {
        var tile = SpaceWorldManager.Instance?.Grid?.GetTile(tileIndex); if (tile == null) yield break;
        foreach (int id in tile.featureInstanceIds) { var f = GetFeature(id); var d = f != null ? GetFeatureData(f.featureDataId) : null; if (d != null) yield return d; }
    }
}
