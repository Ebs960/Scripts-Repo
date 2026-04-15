using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central manager for the disease system. Handles per-round infection rolls,
/// disease spread between nearby cities/herds, and per-turn tick processing.
/// Subscribes to TurnManager.OnNeutralTurn for world-level processing each round.
/// </summary>
public class DiseaseManager : MonoBehaviour
{
    public static DiseaseManager Instance { get; private set; }

    [Header("Disease Database")]
    [Tooltip("All disease types that can appear in the game.")]
    public DiseaseData[] allDiseases;

    [Header("Global Settings")]
    [Tooltip("Global multiplier applied to all infection chances (difficulty scaling).")]
    public float infectionChanceMultiplier = 1f;

    [Tooltip("Global multiplier applied to all spread chances.")]
    public float spreadChanceMultiplier = 1f;

    [Tooltip("Minimum round before diseases can appear.")]
    public int minimumRoundForDiseases = 10;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(this); return; }
    }

    void OnEnable()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnNeutralTurn += ProcessDiseaseRound;
    }

    void OnDisable()
    {
        if (TurnManager.Instance != null)
            TurnManager.Instance.OnNeutralTurn -= ProcessDiseaseRound;
    }

    /// <summary>
    /// Main entry point called once per round after all civilizations have taken their turns.
    /// </summary>
    private void ProcessDiseaseRound(int round)
    {
        if (round < minimumRoundForDiseases) return;
        if (allDiseases == null || allDiseases.Length == 0) return;

        var civs = CivilizationManager.Instance != null ? CivilizationManager.Instance.GetAllCivs() : null;
        if (civs == null) return;

        // 1) Tick existing diseases on all cities and herds
        TickExistingDiseases(civs);

        // 2) Try to spread existing diseases to nearby targets
        SpreadDiseases(civs);

        // 3) Roll for spontaneous new infections
        RollSpontaneousInfections(civs, round);
    }

    // ─── Tick Existing ─────────────────────────────────────────────────────────

    private void TickExistingDiseases(IReadOnlyList<Civilization> civs)
    {
        for (int c = 0; c < civs.Count; c++)
        {
            var civ = civs[c];
            if (civ == null) continue;

            // Cities: disease turn effects are applied inside City.ProcessCityTurn via
            // ApplyDiseaseTurnEffects(). We only need to process herds here.
            if (civ.herds != null)
            {
                foreach (var herd in civ.herds)
                {
                    if (herd != null)
                        herd.ProcessDiseaseTurn();
                }
            }
        }
    }

    // ─── Spread ────────────────────────────────────────────────────────────────

    private void SpreadDiseases(IReadOnlyList<Civilization> civs)
    {
        // Collect all currently infected cities and herds, then try to spread
        var infectedCities = new List<(City city, DiseaseInstance di)>();
        var infectedHerds = new List<(Herd herd, DiseaseInstance di)>();

        for (int c = 0; c < civs.Count; c++)
        {
            var civ = civs[c];
            if (civ == null) continue;

            if (civ.cities != null)
            {
                foreach (var city in civ.cities)
                {
                    if (city == null || city.activeDiseases == null) continue;
                    foreach (var di in city.activeDiseases)
                    {
                        if (di != null && di.data != null)
                            infectedCities.Add((city, di));
                    }
                }
            }

            if (civ.herds != null)
            {
                foreach (var herd in civ.herds)
                {
                    if (herd == null || herd.activeDiseases == null) continue;
                    foreach (var di in herd.activeDiseases)
                    {
                        if (di != null && di.data != null)
                            infectedHerds.Add((herd, di));
                    }
                }
            }
        }

        // Spread from infected cities
        foreach (var (srcCity, di) in infectedCities)
        {
            var sourceTotals = srcCity.owner != null ? srcCity.owner.GetDiseaseModifierTotals(di.data, srcCity) : default;
            float chance = di.data.spreadChance * spreadChanceMultiplier * sourceTotals.SpreadChanceMultiplier;
            if (chance <= 0f) continue;

            // Spread to nearby cities (all civs)
            SpreadFromCityToNearbyCities(srcCity, di.data, chance, civs);

            // Spread to nearby herds
            SpreadFromCityToNearbyHerds(srcCity, di.data, chance, civs);

            // Spread along trade routes
            if (di.data.spreadsAlongTradeRoutes)
                SpreadAlongTradeRoutes(srcCity, di.data, chance);
        }

        // Spread from infected herds
        foreach (var (srcHerd, di) in infectedHerds)
        {
            var sourceTotals = srcHerd.owner != null ? srcHerd.owner.GetDiseaseModifierTotals(di.data, herdContext: srcHerd) : default;
            float chance = di.data.spreadChance * spreadChanceMultiplier * sourceTotals.SpreadChanceMultiplier;
            if (chance <= 0f) continue;

            SpreadFromHerdToNearbyCities(srcHerd, di.data, chance, civs);
            SpreadFromHerdToNearbyHerds(srcHerd, di.data, chance, civs);
        }
    }

    private void SpreadFromCityToNearbyCities(City src, DiseaseData disease, float chance, IReadOnlyList<Civilization> civs)
    {
        var ts = TileSystem.GetForPlanet(src.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;

        for (int c = 0; c < civs.Count; c++)
        {
            var civ = civs[c];
            if (civ == null || civ.cities == null) continue;
            foreach (var target in civ.cities)
            {
                if (target == null || target == src) continue;
                if (target.planetIndex != src.planetIndex) continue;
                if (target.HasDisease(disease) || target.HasDiseaseImmunity(disease)) continue;

                int dist = ts.GetTileDistance(src.centerTileIndex, target.centerTileIndex);
                if (dist > disease.spreadRadius) continue;

                var targetTotals = target.owner != null ? target.owner.GetDiseaseModifierTotals(disease, target) : default;
                float finalChance = chance * targetTotals.InfectionChanceMultiplier * target.GetDiseaseResistance(disease);
                if (Random.value < finalChance)
                    target.InfectWithDisease(disease);
            }
        }
    }

    private void SpreadFromCityToNearbyHerds(City src, DiseaseData disease, float chance, IReadOnlyList<Civilization> civs)
    {
        var ts = TileSystem.GetForPlanet(src.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;

        for (int c = 0; c < civs.Count; c++)
        {
            var civ = civs[c];
            if (civ == null || civ.herds == null) continue;
            foreach (var target in civ.herds)
            {
                if (target == null) continue;
                if (target.planetIndex != src.planetIndex) continue;
                if (target.HasDisease(disease) || target.HasDiseaseImmunity(disease)) continue;

                int dist = ts.GetTileDistance(src.centerTileIndex, target.currentTileIndex);
                if (dist > disease.spreadRadius) continue;

                var targetTotals = target.owner != null ? target.owner.GetDiseaseModifierTotals(disease, herdContext: target) : default;
                if (Random.value < chance * targetTotals.InfectionChanceMultiplier)
                    target.InfectWithDisease(disease);
            }
        }
    }

    private void SpreadFromHerdToNearbyCities(Herd src, DiseaseData disease, float chance, IReadOnlyList<Civilization> civs)
    {
        var ts = TileSystem.GetForPlanet(src.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;

        for (int c = 0; c < civs.Count; c++)
        {
            var civ = civs[c];
            if (civ == null || civ.cities == null) continue;
            foreach (var target in civ.cities)
            {
                if (target == null) continue;
                if (target.planetIndex != src.planetIndex) continue;
                if (target.HasDisease(disease) || target.HasDiseaseImmunity(disease)) continue;

                int dist = ts.GetTileDistance(src.currentTileIndex, target.centerTileIndex);
                if (dist > disease.spreadRadius) continue;

                var targetTotals = target.owner != null ? target.owner.GetDiseaseModifierTotals(disease, target) : default;
                float finalChance = chance * targetTotals.InfectionChanceMultiplier * target.GetDiseaseResistance(disease);
                if (Random.value < finalChance)
                    target.InfectWithDisease(disease);
            }
        }
    }

    private void SpreadFromHerdToNearbyHerds(Herd src, DiseaseData disease, float chance, IReadOnlyList<Civilization> civs)
    {
        var ts = TileSystem.GetForPlanet(src.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;

        for (int c = 0; c < civs.Count; c++)
        {
            var civ = civs[c];
            if (civ == null || civ.herds == null) continue;
            foreach (var target in civ.herds)
            {
                if (target == null || target == src) continue;
                if (target.planetIndex != src.planetIndex) continue;
                if (target.HasDisease(disease) || target.HasDiseaseImmunity(disease)) continue;

                int dist = ts.GetTileDistance(src.currentTileIndex, target.currentTileIndex);
                if (dist > disease.spreadRadius) continue;

                var targetTotals = target.owner != null ? target.owner.GetDiseaseModifierTotals(disease, herdContext: target) : default;
                if (Random.value < chance * targetTotals.InfectionChanceMultiplier)
                    target.InfectWithDisease(disease);
            }
        }
    }

    private void SpreadAlongTradeRoutes(City srcCity, DiseaseData disease, float chance)
    {
        var routes = srcCity.GetActiveTradeRoutes();
        if (routes == null) return;

        foreach (var route in routes)
        {
            if (route == null) continue;

            // Determine the other end of the route
            City partner = null;
            if (route.sourceCity == srcCity) partner = route.destinationCity;
            else if (route.destinationCity == srcCity) partner = route.sourceCity;

            if (partner == null) continue;
            if (partner.HasDisease(disease) || partner.HasDiseaseImmunity(disease)) continue;

            var partnerTotals = partner.owner != null ? partner.owner.GetDiseaseModifierTotals(disease, partner) : default;
            float finalChance = chance * partnerTotals.InfectionChanceMultiplier * partner.GetDiseaseResistance(disease);
            if (Random.value < finalChance)
                partner.InfectWithDisease(disease);
        }
    }

    // ─── Spontaneous Infection ─────────────────────────────────────────────────

    private void RollSpontaneousInfections(IReadOnlyList<Civilization> civs, int round)
    {
        foreach (var disease in allDiseases)
        {
            if (disease == null) continue;

            // Check season filter
            if (disease.useSeasonFilter && disease.affectedSeasons != null && disease.affectedSeasons.Length > 0)
            {
                bool seasonMatch = false;
                // Check against planet 0 season (primary planet); diseases fire globally
                if (ClimateManager.Instance != null)
                {
                    Season currentSeason = ClimateManager.Instance.GetSeasonForPlanet(0);
                    foreach (var s in disease.affectedSeasons)
                    {
                        if (s == currentSeason) { seasonMatch = true; break; }
                    }
                }
                if (!seasonMatch) continue;
            }

            for (int c = 0; c < civs.Count; c++)
            {
                var civ = civs[c];
                if (civ == null) continue;

                // Roll for cities
                if (civ.cities != null)
                {
                    foreach (var city in civ.cities)
                    {
                        if (city == null) continue;
                        if (city.HasDisease(disease) || city.HasDiseaseImmunity(disease)) continue;

                        // Biome filter
                        if (disease.useBiomeFilter && disease.affectedBiomes != null && disease.affectedBiomes.Length > 0)
                        {
                            var ts = TileSystem.GetForPlanet(city.planetIndex) ?? TileSystem.Instance;
                            if (ts != null)
                            {
                                var td = ts.GetTileData(city.centerTileIndex);
                                if (td != null && !System.Array.Exists(disease.affectedBiomes, b => b == td.biome))
                                    continue;
                            }
                        }

                        float baseChance = disease.baseInfectionChance + disease.infectionChancePerPopulation * city.level;
                        var targetTotals = city.owner != null ? city.owner.GetDiseaseModifierTotals(disease, city) : default;
                        float finalChance = baseChance * infectionChanceMultiplier * targetTotals.InfectionChanceMultiplier * city.GetDiseaseResistance(disease);

                        if (Random.value < finalChance)
                            city.InfectWithDisease(disease);
                    }
                }

                // Roll for herds
                if (civ.herds != null)
                {
                    foreach (var herd in civ.herds)
                    {
                        if (herd == null) continue;
                        if (herd.HasDisease(disease) || herd.HasDiseaseImmunity(disease)) continue;

                        // Biome filter
                        if (disease.useBiomeFilter && disease.affectedBiomes != null && disease.affectedBiomes.Length > 0)
                        {
                            var ts = TileSystem.GetForPlanet(herd.planetIndex) ?? TileSystem.Instance;
                            if (ts != null)
                            {
                                var td = ts.GetTileData(herd.currentTileIndex);
                                if (td != null && !System.Array.Exists(disease.affectedBiomes, b => b == td.biome))
                                    continue;
                            }
                        }

                        int animalCount = herd.GetTotalAnimalCount();
                        float baseChance = disease.baseInfectionChance + disease.infectionChancePer100Animals * (animalCount / 100f);
                        var targetTotals = herd.owner != null ? herd.owner.GetDiseaseModifierTotals(disease, herdContext: herd) : default;
                        float finalChance = baseChance * infectionChanceMultiplier * targetTotals.InfectionChanceMultiplier;

                        if (Random.value < finalChance)
                            herd.InfectWithDisease(disease);
                    }
                }
            }
        }
    }

    // ─── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Manually trigger a disease outbreak on a specific city (e.g. from an event).
    /// </summary>
    public bool TriggerDiseaseOnCity(City city, DiseaseData disease)
    {
        if (city == null || disease == null) return false;
        return city.InfectWithDisease(disease);
    }

    /// <summary>
    /// Manually trigger a disease outbreak on a specific herd (e.g. from an event).
    /// </summary>
    public bool TriggerDiseaseOnHerd(Herd herd, DiseaseData disease)
    {
        if (herd == null || disease == null) return false;
        return herd.InfectWithDisease(disease);
    }

    /// <summary>
    /// Cure all diseases on a city.
    /// </summary>
    public void CureAllDiseases(City city, bool grantImmunity = true)
    {
        if (city == null || city.activeDiseases == null) return;
        for (int i = city.activeDiseases.Count - 1; i >= 0; i--)
        {
            var di = city.activeDiseases[i];
            if (di != null && di.data != null)
                city.CureDisease(di.data, grantImmunity);
        }
    }

    /// <summary>
    /// Cure all diseases on a herd.
    /// </summary>
    public void CureAllDiseases(Herd herd, bool grantImmunity = true)
    {
        if (herd == null || herd.activeDiseases == null) return;
        for (int i = herd.activeDiseases.Count - 1; i >= 0; i--)
        {
            var di = herd.activeDiseases[i];
            if (di != null && di.data != null)
                herd.CureDisease(di.data, grantImmunity);
        }
    }
}
