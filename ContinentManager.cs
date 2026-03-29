using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlanetGenerator))]
public class ContinentManager : MonoBehaviour, ISaveGameParticipant
{
    [Serializable]
    public class ContinentResourceRule
    {
        public string resourceName;
        public float spawnWeight = 1f;
        public bool enabled = true;
    }

    [Serializable]
    public class ContinentCrisisWeight
    {
        public string crisisName;
        public float weight = 1f;
        public bool enabled = true;
    }

    public enum ContinentDiscoveryBonusType
    {
        None,
        RevealTiles,
        GrantGold,
        GrantScience,
        GrantCulture,
        GrantFaith,
        GrantLegacy,
        TriggerNarrative,
    }

    [Serializable]
    public class ContinentDiscoveryBonus
    {
        public string bonusId;
        public ContinentDiscoveryBonusType type;
        public int amount;
        public string payloadName;
        public bool firstDiscoveryOnly = true;
        public bool enabled = true;
    }

    [Serializable]
    public class ContinentDiscoveryResult
    {
        public int continentId;
        public string continentName;
        public int civIndex = -1;
        public bool isFirstDiscovery;
        public List<ContinentDiscoveryBonus> unlockedBonuses = new List<ContinentDiscoveryBonus>();
    }

    [Serializable]
    public class ContinentEntity
    {
        public int continentId;
        public int planetIndex;
        public string name;
        public int centerTileIndex = -1;
        public Vector2Int seedCenter;
        public int widthTiles;
        public int heightTiles;
        public int tileCount;
        public string dominantBiomeName;
        public List<int> tileIndices = new List<int>();
        public List<string> allowedAnimalUnitNames = new List<string>();
        public List<ContinentResourceRule> resourceRules = new List<ContinentResourceRule>();
        public List<ContinentCrisisWeight> crisisWeights = new List<ContinentCrisisWeight>();
        public List<ContinentDiscoveryBonus> discoveryBonuses = new List<ContinentDiscoveryBonus>();
        public List<string> contentTags = new List<string>();
        public List<int> discoveredByCivIndices = new List<int>();
        public bool firstDiscoveryResolved;
        public string archetype;
    }

    [Serializable]
    private class ContinentSaveCollection
    {
        public List<ContinentSaveData> continents = new List<ContinentSaveData>();
    }

    [Serializable]
    private class ContinentSaveData
    {
        public int continentId;
        public string name;
        public List<string> allowedAnimalUnitNames = new List<string>();
        public List<ContinentResourceRule> resourceRules = new List<ContinentResourceRule>();
        public List<ContinentCrisisWeight> crisisWeights = new List<ContinentCrisisWeight>();
        public List<ContinentDiscoveryBonus> discoveryBonuses = new List<ContinentDiscoveryBonus>();
        public List<string> contentTags = new List<string>();
        public List<int> discoveredByCivIndices = new List<int>();
        public bool firstDiscoveryResolved;
        public string archetype;
    }

    [Header("Runtime Labels")]
    [SerializeField] private float labelHeightOffset = 4f;
    [SerializeField] private float labelScale = 0.6f;
    [SerializeField] private float labelFontSize = 30f;
    [SerializeField] private Color labelColor = new Color(0f, 0f, 0f, 1f);
    [SerializeField] private Color labelOutlineColor = new Color(0f, 0f, 0f, 0.9f);

    private PlanetGenerator planetGenerator;
    private TileSystem tileSystem;
    private Transform labelRoot;
    private readonly List<ContinentEntity> continents = new List<ContinentEntity>();
    private readonly Dictionary<int, ContinentEntity> continentById = new Dictionary<int, ContinentEntity>();
    private readonly Dictionary<int, ContinentWorldLabel> labelsById = new Dictionary<int, ContinentWorldLabel>();
    private int[] tileToContinent = Array.Empty<int>();
    private string pendingRestoreJson;

    public string SaveKey => $"continents-p{(planetGenerator != null ? planetGenerator.planetIndex : 0)}";
    public IReadOnlyList<ContinentEntity> Continents => continents;

    private void Awake()
    {
        planetGenerator = GetComponent<PlanetGenerator>();
    }

    private void OnEnable()
    {
        SaveGameRegistry.Register(this);
        GameManager.OnPlanetFullyGenerated += HandlePlanetFullyGenerated;
    }

    private void OnDisable()
    {
        GameManager.OnPlanetFullyGenerated -= HandlePlanetFullyGenerated;
        SaveGameRegistry.Unregister(this);
    }

    public bool CanAnimalSpawnOnTile(CombatUnitData unitData, int tileIndex)
    {
        if (unitData == null || tileIndex < 0 || tileToContinent == null || tileIndex >= tileToContinent.Length)
            return true;

        int continentId = tileToContinent[tileIndex];
        if (continentId < 0 || !continentById.TryGetValue(continentId, out var continent))
            return true;

        if (continent.allowedAnimalUnitNames == null || continent.allowedAnimalUnitNames.Count == 0)
            return true;

        return continent.allowedAnimalUnitNames.Contains(unitData.unitName, StringComparer.OrdinalIgnoreCase);
    }

    public bool CanResourceSpawnOnTile(ResourceData resourceData, int tileIndex)
    {
        if (resourceData == null || tileIndex < 0 || tileToContinent == null || tileIndex >= tileToContinent.Length)
            return true;

        int continentId = tileToContinent[tileIndex];
        if (continentId < 0 || !continentById.TryGetValue(continentId, out var continent))
            return true;

        if (continent.resourceRules == null || continent.resourceRules.Count == 0)
            return true;

        bool anyEnabled = continent.resourceRules.Any(rule => rule != null && rule.enabled);
        if (!anyEnabled)
            return true;

        return continent.resourceRules.Any(rule =>
            rule != null
            && rule.enabled
            && string.Equals(rule.resourceName, resourceData.resourceName, StringComparison.OrdinalIgnoreCase));
    }

    public float GetCrisisWeight(CrisisData crisisData, int continentId)
    {
        if (crisisData == null || !continentById.TryGetValue(continentId, out var continent) || continent.crisisWeights == null)
            return 1f;

        var match = continent.crisisWeights.FirstOrDefault(weight =>
            weight != null
            && weight.enabled
            && string.Equals(weight.crisisName, crisisData.crisisName, StringComparison.OrdinalIgnoreCase));

        return match != null ? Mathf.Max(0f, match.weight) : 1f;
    }

    public IReadOnlyList<ContinentDiscoveryBonus> GetDiscoveryBonuses(int continentId)
    {
        if (!continentById.TryGetValue(continentId, out var continent) || continent.discoveryBonuses == null)
            return Array.Empty<ContinentDiscoveryBonus>();

        return continent.discoveryBonuses;
    }

    public bool TryMarkContinentDiscovered(Civilization civ, int tileIndex, out ContinentDiscoveryResult result)
    {
        result = null;
        if (civ == null || !TryGetContinentForTile(tileIndex, out var continent))
            return false;

        int civIndex = CivilizationManager.Instance != null ? CivilizationManager.Instance.GetCivIndex(civ) : -1;
        if (continent.discoveredByCivIndices.Contains(civIndex))
            return false;

        bool firstDiscovery = continent.discoveredByCivIndices.Count == 0;
        continent.discoveredByCivIndices.Add(civIndex);
        if (firstDiscovery)
            continent.firstDiscoveryResolved = true;

        result = new ContinentDiscoveryResult
        {
            continentId = continent.continentId,
            continentName = continent.name,
            civIndex = civIndex,
            isFirstDiscovery = firstDiscovery,
            unlockedBonuses = continent.discoveryBonuses != null
                ? continent.discoveryBonuses
                    .Where(bonus => bonus != null && bonus.enabled && (!bonus.firstDiscoveryOnly || firstDiscovery))
                    .ToList()
                : new List<ContinentDiscoveryBonus>()
        };

        return true;
    }

    public bool TryGetContinentForTile(int tileIndex, out ContinentEntity continent)
    {
        continent = null;
        if (tileIndex < 0 || tileToContinent == null || tileIndex >= tileToContinent.Length)
            return false;

        int continentId = tileToContinent[tileIndex];
        return continentId >= 0 && continentById.TryGetValue(continentId, out continent);
    }

    public void RebuildFromGenerator()
    {
        if (planetGenerator == null)
            planetGenerator = GetComponent<PlanetGenerator>();
        if (planetGenerator == null || planetGenerator.Grid == null)
            return;

        tileSystem = TileSystem.GetForPlanet(planetGenerator.planetIndex) ?? TileSystem.Instance;
        if (tileSystem == null || !tileSystem.IsReady())
            return;

        var generatedContinents = planetGenerator.GetGeneratedContinents();
        continents.Clear();
        continentById.Clear();
        labelsById.Clear();
        EnsureLabelRoot();
        ClearLabelRoot();

        tileToContinent = new int[planetGenerator.Grid.TileCount];
        for (int i = 0; i < tileToContinent.Length; i++)
            tileToContinent[i] = -1;

        foreach (var generated in generatedContinents)
        {
            var continent = new ContinentEntity
            {
                continentId = generated.Id,
                planetIndex = planetGenerator.planetIndex,
                name = generated.Name,
                seedCenter = generated.Center,
                widthTiles = generated.WidthTiles,
                heightTiles = generated.HeightTiles,
                centerTileIndex = GetTileIndexForCoord(generated.Center),
                archetype = ResolveArchetypeName(generated.WidthTiles, generated.HeightTiles)
            };

            continents.Add(continent);
            continentById[continent.continentId] = continent;
        }

        for (int tileIndex = 0; tileIndex < tileToContinent.Length; tileIndex++)
        {
            var tile = tileSystem.GetTileData(tileIndex);
            if (tile == null)
                continue;

            int continentId = tile.isLand ? planetGenerator.GetContinentIndexForTile(tileIndex) : -1;
            if (continentId < 0 || !continentById.TryGetValue(continentId, out var continent))
            {
                tile.continentId = -1;
                tile.continentName = null;
                continue;
            }

            tileToContinent[tileIndex] = continentId;
            continent.tileIndices.Add(tileIndex);
            tile.continentId = continentId;
            tile.continentName = continent.name;
        }

        foreach (var continent in continents)
        {
            continent.tileCount = continent.tileIndices.Count;
            if (continent.tileCount > 0)
            {
                continent.centerTileIndex = ResolveCenterTile(continent);
                continent.dominantBiomeName = ResolveDominantBiomeName(continent);
                if (string.IsNullOrWhiteSpace(continent.archetype))
                    continent.archetype = ResolveArchetypeName(continent.widthTiles, continent.heightTiles);
            }
        }

        if (!string.IsNullOrEmpty(pendingRestoreJson))
            ApplyRestoredState(pendingRestoreJson);

        RebuildLabels();
    }

    public string CaptureStateJson()
    {
        var saveCollection = new ContinentSaveCollection();
        foreach (var continent in continents)
        {
            saveCollection.continents.Add(new ContinentSaveData
            {
                continentId = continent.continentId,
                name = continent.name,
                allowedAnimalUnitNames = continent.allowedAnimalUnitNames != null
                    ? new List<string>(continent.allowedAnimalUnitNames)
                    : new List<string>(),
                resourceRules = continent.resourceRules != null
                    ? CloneResourceRules(continent.resourceRules)
                    : new List<ContinentResourceRule>(),
                crisisWeights = continent.crisisWeights != null
                    ? CloneCrisisWeights(continent.crisisWeights)
                    : new List<ContinentCrisisWeight>(),
                discoveryBonuses = continent.discoveryBonuses != null
                    ? CloneDiscoveryBonuses(continent.discoveryBonuses)
                    : new List<ContinentDiscoveryBonus>(),
                contentTags = continent.contentTags != null
                    ? new List<string>(continent.contentTags)
                    : new List<string>(),
                discoveredByCivIndices = continent.discoveredByCivIndices != null
                    ? new List<int>(continent.discoveredByCivIndices)
                    : new List<int>(),
                firstDiscoveryResolved = continent.firstDiscoveryResolved,
                archetype = continent.archetype
            });
        }

        return JsonUtility.ToJson(saveCollection);
    }

    public void RestoreStateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return;

        if (continents.Count == 0)
        {
            pendingRestoreJson = json;
            return;
        }

        ApplyRestoredState(json);
        RebuildLabels();
    }

    private void HandlePlanetFullyGenerated(PlanetGenerator generator)
    {
        if (generator == null || generator != planetGenerator)
            return;

        RebuildFromGenerator();
    }

    private void ApplyRestoredState(string json)
    {
        pendingRestoreJson = json;
        var saveCollection = JsonUtility.FromJson<ContinentSaveCollection>(json);
        if (saveCollection?.continents == null)
            return;

        foreach (var saveData in saveCollection.continents)
        {
            if (saveData == null || !continentById.TryGetValue(saveData.continentId, out var continent))
                continue;

            if (!string.IsNullOrWhiteSpace(saveData.name))
                continent.name = saveData.name;

            continent.allowedAnimalUnitNames = saveData.allowedAnimalUnitNames != null
                ? new List<string>(saveData.allowedAnimalUnitNames)
                : new List<string>();
            continent.resourceRules = saveData.resourceRules != null
                ? CloneResourceRules(saveData.resourceRules)
                : new List<ContinentResourceRule>();
            continent.crisisWeights = saveData.crisisWeights != null
                ? CloneCrisisWeights(saveData.crisisWeights)
                : new List<ContinentCrisisWeight>();
            continent.discoveryBonuses = saveData.discoveryBonuses != null
                ? CloneDiscoveryBonuses(saveData.discoveryBonuses)
                : new List<ContinentDiscoveryBonus>();
            continent.contentTags = saveData.contentTags != null
                ? new List<string>(saveData.contentTags)
                : new List<string>();
            continent.discoveredByCivIndices = saveData.discoveredByCivIndices != null
                ? new List<int>(saveData.discoveredByCivIndices)
                : new List<int>();
            continent.firstDiscoveryResolved = saveData.firstDiscoveryResolved;
            continent.archetype = saveData.archetype;

            foreach (int tileIndex in continent.tileIndices)
            {
                var tile = tileSystem != null ? tileSystem.GetTileData(tileIndex) : null;
                if (tile != null)
                    tile.continentName = continent.name;
            }
        }
    }

    private void RebuildLabels()
    {
        EnsureLabelRoot();
        ClearLabelRoot();
        labelsById.Clear();

        foreach (var continent in continents)
        {
            if (continent.tileCount <= 0 || continent.centerTileIndex < 0)
                continue;

            var labelObject = new GameObject($"ContinentLabel_{continent.continentId}");
            labelObject.transform.SetParent(labelRoot, false);
            labelObject.transform.localScale = Vector3.one * labelScale;

            var label = labelObject.AddComponent<ContinentWorldLabel>();
            label.Initialize(tileSystem, continent.centerTileIndex, labelHeightOffset);

            var text = labelObject.AddComponent<TextMeshPro>();
            if (TMP_Settings.defaultFontAsset != null)
                text.font = TMP_Settings.defaultFontAsset;
            text.text = continent.name;
            text.fontSize = labelFontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.color = labelColor;
            text.outlineWidth = 0.18f;
            text.outlineColor = labelOutlineColor;
            text.raycastTarget = false;

            var renderer = labelObject.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.sortingOrder = 90;
            }

            labelsById[continent.continentId] = label;
        }
    }

    private void EnsureLabelRoot()
    {
        if (labelRoot != null)
            return;

        var existing = transform.Find("ContinentLabels");
        if (existing != null)
        {
            labelRoot = existing;
            return;
        }

        var root = new GameObject("ContinentLabels");
        root.transform.SetParent(transform, false);
        labelRoot = root.transform;
    }

    private void ClearLabelRoot()
    {
        if (labelRoot == null)
            return;

        for (int i = labelRoot.childCount - 1; i >= 0; i--)
            Destroy(labelRoot.GetChild(i).gameObject);
    }

    private int GetTileIndexForCoord(Vector2Int coord)
    {
        if (planetGenerator == null || planetGenerator.Grid == null || planetGenerator.Grid.Width <= 0)
            return -1;

        int x = Mathf.Clamp(coord.x, 0, planetGenerator.Grid.Width - 1);
        int y = Mathf.Clamp(coord.y, 0, planetGenerator.Grid.Height - 1);
        return y * planetGenerator.Grid.Width + x;
    }

    private int ResolveCenterTile(ContinentEntity continent)
    {
        if (continent.tileIndices == null || continent.tileIndices.Count == 0 || planetGenerator == null || planetGenerator.Grid == null)
            return continent.centerTileIndex;

        int width = planetGenerator.Grid.Width;
        int bestTile = continent.tileIndices[0];
        float bestScore = float.MaxValue;

        foreach (int tileIndex in continent.tileIndices)
        {
            int tileX = tileIndex % width;
            int tileY = tileIndex / width;
            int deltaX = WrappedDelta(tileX, continent.seedCenter.x, width);
            int deltaY = tileY - continent.seedCenter.y;
            float score = deltaX * deltaX + deltaY * deltaY;
            if (score < bestScore)
            {
                bestScore = score;
                bestTile = tileIndex;
            }
        }

        return bestTile;
    }

    private string ResolveDominantBiomeName(ContinentEntity continent)
    {
        if (continent.tileIndices == null || continent.tileIndices.Count == 0 || tileSystem == null)
            return null;

        var counts = new Dictionary<Biome, int>();
        foreach (int tileIndex in continent.tileIndices)
        {
            var tile = tileSystem.GetTileData(tileIndex);
            if (tile == null)
                continue;

            if (counts.ContainsKey(tile.biome))
                counts[tile.biome]++;
            else
                counts[tile.biome] = 1;
        }

        if (counts.Count == 0)
            return null;

        return counts.OrderByDescending(kvp => kvp.Value).First().Key.ToString();
    }

    private string ResolveArchetypeName(int widthTiles, int heightTiles)
    {
        int area = Mathf.Max(1, widthTiles * heightTiles);
        if (area >= 140000)
            return "Supercontinent";
        if (area >= 70000)
            return "Mainland";
        if (area >= 28000)
            return "Continent";
        return "Island Chain";
    }

    private static List<ContinentResourceRule> CloneResourceRules(List<ContinentResourceRule> source)
    {
        var list = new List<ContinentResourceRule>();
        foreach (var rule in source)
        {
            if (rule == null) continue;
            list.Add(new ContinentResourceRule
            {
                resourceName = rule.resourceName,
                spawnWeight = rule.spawnWeight,
                enabled = rule.enabled
            });
        }
        return list;
    }

    private static List<ContinentCrisisWeight> CloneCrisisWeights(List<ContinentCrisisWeight> source)
    {
        var list = new List<ContinentCrisisWeight>();
        foreach (var weight in source)
        {
            if (weight == null) continue;
            list.Add(new ContinentCrisisWeight
            {
                crisisName = weight.crisisName,
                weight = weight.weight,
                enabled = weight.enabled
            });
        }
        return list;
    }

    private static List<ContinentDiscoveryBonus> CloneDiscoveryBonuses(List<ContinentDiscoveryBonus> source)
    {
        var list = new List<ContinentDiscoveryBonus>();
        foreach (var bonus in source)
        {
            if (bonus == null) continue;
            list.Add(new ContinentDiscoveryBonus
            {
                bonusId = bonus.bonusId,
                type = bonus.type,
                amount = bonus.amount,
                payloadName = bonus.payloadName,
                firstDiscoveryOnly = bonus.firstDiscoveryOnly,
                enabled = bonus.enabled
            });
        }
        return list;
    }

    private int WrappedDelta(int a, int b, int width)
    {
        int delta = a - b;
        if (Mathf.Abs(delta) > width / 2)
            delta = delta > 0 ? delta - width : delta + width;
        return delta;
    }
}