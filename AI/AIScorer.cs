using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central utility scoring system for all AI decisions.
/// Every action the AI considers is reduced to a float score — highest score wins.
/// Weights are tuned here in one place so balance changes are easy.
/// </summary>
public static class AIScorer
{
    // ──────────────────────── Weight constants ────────────────────────
    // Kept as public so they can be tuned at runtime via a debug panel.

    public static float W_KILL_BONUS         = 20f;
    public static float W_FOOD_ON_KILL       = 3f;
    public static float W_DAMAGE_DEALT       = 1.5f;
    public static float W_DAMAGE_TAKEN       = -2f;
    public static float W_TARGET_VALUE       = 2f;
    public static float W_LOW_HEALTH_TARGET  = 5f;

    public static float W_DANGER_PENALTY     = -1.5f;
    public static float W_DISTANCE_PENALTY   = -0.3f;
    public static float W_TERRAIN_DEFENSE    = 0.5f;
    public static float W_HILL_BONUS         = 1f;

    public static float W_FORAGE_FOOD        = 4f;
    public static float W_FORAGE_OTHER       = 1f;

    public static float W_SETTLE_YIELD       = 2f;
    public static float W_SETTLE_DISTANCE    = 0.5f;

    public static float W_SHELTER_URGENCY    = 8f;
    public static float W_UPGRADE_VALUE      = 3f;

    public static float W_RETREAT_HEALTH     = 6f;
    public static float W_RETREAT_SAFETY     = 2f;

    public static float W_FORTIFY_BASE       = 1f;

    public static float W_FATIGUE_TARGET_BONUS = 2f;   // bonus for attacking fatigued enemies

    public static float W_RESOURCE_YIELD     = 2f;
    public static float W_RESOURCE_STRATEGIC = 5f;
    public static float W_RESOURCE_UNIQUE    = 8f;

    public static float W_EXPLORE_BASE       = 4f;
    public static float W_EXPLORE_UNEXPLORED = 2f;
    public static float W_EXPLORE_DISTANCE   = -0.5f;

    private const float MosquitoTilePenalty = 18f;

    // ──────────────────────── Procedural Persona System ────────────────────────
    // Modulates weights per-civ based on leader personality.
    // Applied at the start of a civ's turn and restored at the end.
    // Uses response curves rather than crisp if/then transitions.

    private static float[] _savedWeights;

    private static float[] SnapshotWeights() => new float[]
    {
        W_KILL_BONUS, W_FOOD_ON_KILL, W_DAMAGE_DEALT, W_DAMAGE_TAKEN,
        W_TARGET_VALUE, W_LOW_HEALTH_TARGET, W_DANGER_PENALTY, W_DISTANCE_PENALTY,
        W_TERRAIN_DEFENSE, W_HILL_BONUS, W_FORAGE_FOOD, W_FORAGE_OTHER,
        W_SETTLE_YIELD, W_SETTLE_DISTANCE, W_SHELTER_URGENCY, W_UPGRADE_VALUE,
        W_RETREAT_HEALTH, W_RETREAT_SAFETY, W_FORTIFY_BASE,
        W_RESOURCE_YIELD, W_RESOURCE_STRATEGIC, W_RESOURCE_UNIQUE,
        W_EXPLORE_BASE, W_EXPLORE_UNEXPLORED, W_EXPLORE_DISTANCE,
        W_FATIGUE_TARGET_BONUS
    };

    private static void RestoreFromSnapshot(float[] s)
    {
        if (s == null || s.Length < 26) return;
        W_KILL_BONUS = s[0]; W_FOOD_ON_KILL = s[1]; W_DAMAGE_DEALT = s[2]; W_DAMAGE_TAKEN = s[3];
        W_TARGET_VALUE = s[4]; W_LOW_HEALTH_TARGET = s[5]; W_DANGER_PENALTY = s[6]; W_DISTANCE_PENALTY = s[7];
        W_TERRAIN_DEFENSE = s[8]; W_HILL_BONUS = s[9]; W_FORAGE_FOOD = s[10]; W_FORAGE_OTHER = s[11];
        W_SETTLE_YIELD = s[12]; W_SETTLE_DISTANCE = s[13]; W_SHELTER_URGENCY = s[14]; W_UPGRADE_VALUE = s[15];
        W_RETREAT_HEALTH = s[16]; W_RETREAT_SAFETY = s[17]; W_FORTIFY_BASE = s[18];
        W_RESOURCE_YIELD = s[19]; W_RESOURCE_STRATEGIC = s[20]; W_RESOURCE_UNIQUE = s[21];
        W_EXPLORE_BASE = s[22]; W_EXPLORE_UNEXPLORED = s[23]; W_EXPLORE_DISTANCE = s[24];
        W_FATIGUE_TARGET_BONUS = s[25];
    }

    /// <summary>
    /// Apply a procedural persona based on leader personality. Modulates scoring weights
    /// using smooth response curves (not binary if/then). Call at the start of a civ's turn.
    /// </summary>
    public static void ApplyPersona(LeaderData leader)
    {
        _savedWeights = SnapshotWeights();
        if (leader == null) return;

        // Response curve: lerp(1.0, multiplier, focus) where focus is 0–2
        float Curve(float focus, float baseMultiplier) => Mathf.Lerp(1f, baseMultiplier, focus / 2f);

        // Military persona: aggressive leaders value kills more, fear danger less
        W_KILL_BONUS        *= Curve(leader.militaryFocus, 1.4f);
        W_DAMAGE_DEALT      *= Curve(leader.militaryFocus, 1.3f);
        W_TARGET_VALUE      *= Curve(leader.militaryFocus, 1.3f);
        W_DANGER_PENALTY    *= Curve(leader.militaryFocus, 0.7f);   // less afraid
        W_RETREAT_HEALTH    *= Curve(leader.militaryFocus, 0.8f);   // retreat less eagerly

        // Risk from aggressiveness (0–10 scale, 5 is neutral)
        float riskFactor = (leader.aggressiveness - 5f) / 10f; // -0.5 to +0.5
        W_DANGER_PENALTY    *= (1f - riskFactor * 0.4f);
        W_RETREAT_HEALTH    *= (1f - riskFactor * 0.3f);
        W_FORTIFY_BASE      *= (1f + riskFactor * 0.3f); // cautious leaders fortify more

        // Economic persona: values resources and food gathering
        W_FORAGE_FOOD       *= Curve(leader.economicFocus, 1.3f);
        W_RESOURCE_YIELD    *= Curve(leader.economicFocus, 1.4f);
        W_RESOURCE_STRATEGIC *= Curve(leader.economicFocus, 1.3f);

        // Scientific persona: values tech-enabling resources and exploration
        W_EXPLORE_BASE      *= Curve(leader.scientificFocus, 1.3f);
        W_EXPLORE_UNEXPLORED *= Curve(leader.scientificFocus, 1.2f);

        // Cultural persona: values settle and build more
        W_SETTLE_YIELD      *= Curve(leader.culturalFocus, 1.3f);
        W_UPGRADE_VALUE     *= Curve(leader.culturalFocus, 1.2f);

        // Expansion persona
        W_SETTLE_YIELD      *= Curve(leader.expansion / 5f, 1.3f); // 0–10 → 0–2 range
        W_EXPLORE_BASE      *= Curve(leader.expansion / 5f, 1.2f);

        // Diplomacy modulates risk (diplomatic leaders are more cautious)
        float dipFactor = leader.diplomacy / 10f; // 0–1
        W_DANGER_PENALTY    *= (1f + dipFactor * 0.2f); // more careful
        W_RETREAT_HEALTH    *= (1f + dipFactor * 0.15f);
    }

    /// <summary>
    /// Restore default weights after a civ's turn completes.
    /// </summary>
    public static void ResetPersona()
    {
        RestoreFromSnapshot(_savedWeights);
        _savedWeights = null;
    }

    private static bool CivilizationShouldAvoidMosquitoes(Civilization civ)
    {
        if (civ == null || civ.civData == null)
            return false;

        if (civ.civData.isTribe || civ.civData.isCityState)
            return false;

        return !civ.HasMosquitoImmunityTechnology();
    }

    private static float GetMosquitoPenalty(BaseUnit unit, HexTileData tileData)
    {
        if (unit == null || tileData == null || !tileData.hasMosquitoes)
            return 0f;

        if (!CivilizationShouldAvoidMosquitoes(unit.owner))
            return 0f;

        if (unit is CombatUnit combatUnit)
        {
            if (combatUnit.data == null)
                return 0f;

            var ut = combatUnit.data.unitType;
            if (ut == CombatCategory.Animal || CombatUnitData.IsAirCategory(ut) || CombatUnitData.IsNavalCategory(ut))
                return 0f;

            if (combatUnit.data.immuneToMosquitoes)
                return 0f;
        }

        if (unit is WorkerUnit workerUnit && workerUnit.data != null && workerUnit.data.immuneToMosquitoes)
            return 0f;

        return -MosquitoTilePenalty;
    }

    private static float GetMosquitoPenalty(Civilization civ, HexTileData tileData, float weight = 1f)
    {
        if (tileData == null || !tileData.hasMosquitoes || !CivilizationShouldAvoidMosquitoes(civ))
            return 0f;

        return -MosquitoTilePenalty * weight;
    }

    // ──────────────────────── Attack scoring ────────────────────────

    /// <summary>
    /// Score an attack from attacker against defender. Higher = more desirable.
    /// Considers kill probability, food gain, focus fire, terrain, and risk.
    /// </summary>
    public static float ScoreAttack(BaseUnit attacker, BaseUnit defender, DangerMap dangerMap)
    {
        if (attacker == null || defender == null) return float.MinValue;
        float score = 0f;

        int attackerDmg = attacker.CurrentAttack;
        int defenderHP = defender.currentHealth;
        int defenderMaxHP = defender.MaxHealth;

        // Kill bonus: huge incentive to finish off wounded targets
        bool willKill = attackerDmg >= defenderHP;
        if (willKill) score += W_KILL_BONUS;

        // Food from killing animals
        if (defender is CombatUnit ct && ct.data != null && ct.data.unitType == CombatCategory.Animal)
        {
            score += ct.data.foodOnKill * W_FOOD_ON_KILL;
        }

        // Damage dealt
        int expectedDmg = Mathf.Min(attackerDmg, defenderHP);
        score += expectedDmg * W_DAMAGE_DEALT;

        // Low-health target bonus — concentrate fire on wounded units
        float hpRatio = (float)defenderHP / Mathf.Max(1, defenderMaxHP);
        score += (1f - hpRatio) * W_LOW_HEALTH_TARGET;

        // Target military value (removing a strong unit is worth more)
        score += defender.BaseAttack * W_TARGET_VALUE;

        // Fatigue vulnerability: prefer attacking weakened units
        if (defender.currentFatigue > 50f)
            score += (defender.currentFatigue / 100f) * W_FATIGUE_TARGET_BONUS;

        // Risk: expected counter-damage if defender survives
        if (!willKill)
        {
            int counterDmg = Mathf.Max(0, defender.BaseAttack - attacker.BaseDefense);
            score += counterDmg * W_DAMAGE_TAKEN;
        }

        // Terrain advantage: attacker on a hill or defending tile with defense bonus
        try
        {
            var ts = TileSystem.GetForPlanet(attacker.planetIndex) ?? TileSystem.Instance;
            if (ts != null)
            {
                var attackerTile = ts.GetTileData(attacker.currentTileIndex);
                if (attackerTile != null && attackerTile.isHill) score += 2f;
                var defenderTile = ts.GetTileData(defender.currentTileIndex);
                if (defenderTile != null)
                {
                    score -= (defenderTile.improvementDefenseAdd * 0.5f + defenderTile.improvementDefensePct * 5f);
                }
            }
        }
        catch { }

        // Post-attack danger: will the attacker be safe after this?
        float postDanger = dangerMap.GetDanger(attacker.currentTileIndex);
        if (willKill) postDanger -= defender.BaseAttack; // killed target no longer threatens
        score += Mathf.Min(0, -postDanger * 0.3f);

        return score;
    }

    // ──────────────────────── Tile / movement scoring ────────────────────────

    /// <summary>
    /// Score a tile for movement (general-purpose: approach, exploration, retreat).
    /// </summary>
    public static float ScoreTileForMovement(BaseUnit unit, int tileIndex, int objectiveTile, DangerMap dangerMap)
    {
        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return float.MinValue;
        float score = 0f;

        // Danger
        score += dangerMap.GetDanger(tileIndex) * W_DANGER_PENALTY;

        // Distance to objective (fewer tiles = better)
        if (objectiveTile >= 0)
        {
            int dist = ts.GetTileDistance(tileIndex, objectiveTile);
            score += dist * W_DISTANCE_PENALTY;
        }

        // Terrain defense
        var td = ts.GetTileData(tileIndex);
        if (td != null)
        {
            score += (td.improvementDefenseAdd + td.improvementDefensePct * 10f) * W_TERRAIN_DEFENSE;
            if (td.isHill) score += W_HILL_BONUS;
            score += GetMosquitoPenalty(unit, td);
        }

        return score;
    }

    // ──────────────────────── Retreat scoring ────────────────────────

    /// <summary>
    /// Score a tile as a retreat destination. Prefers safe, far-from-danger tiles.
    /// </summary>
    public static float ScoreRetreat(BaseUnit unit, int tileIndex, DangerMap dangerMap)
    {
        float score = 0f;

        // Safety: less danger = better retreat
        float danger = dangerMap.GetDanger(tileIndex);
        float currentDanger = dangerMap.GetDanger(unit.currentTileIndex);
        float dangerReduction = currentDanger - danger;
        score += dangerReduction * W_RETREAT_SAFETY;

        // Health urgency: lower health = higher retreat value
        float hpRatio = (float)unit.currentHealth / Mathf.Max(1, unit.MaxHealth);
        score += (1f - hpRatio) * W_RETREAT_HEALTH;

        return score;
    }

    // ──────────────────────── Forage scoring ────────────────────────

    public static float ScoreForage(WorkerUnit worker, ResourceInstance resource, DangerMap dangerMap)
    {
        if (resource == null || resource.data == null) return float.MinValue;
        float score = 0f;
        score += resource.data.forageFood * W_FORAGE_FOOD;
        score += (resource.data.forageGold + resource.data.forageScience + resource.data.forageCulture) * W_FORAGE_OTHER;
        score += dangerMap.GetDanger(worker.currentTileIndex) * W_DANGER_PENALTY;
        return score;
    }

    // ──────────────────────── Settle City scoring ────────────────────────

    /// <summary>
    /// Deep city placement scoring: evaluates the full workable area (2-ring BFS),
    /// fresh water, defensibility, resource diversity, biome quality, and spacing.
    /// </summary>
    public static float ScoreSettleCity(WorkerUnit worker, int tileIndex, DangerMap dangerMap)
    {
        var ts = TileSystem.GetForPlanet(worker.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return float.MinValue;
        var td = ts.GetTileData(tileIndex);
        if (td == null || !td.isLand) return float.MinValue;

        float score = 5f;

        // ── Workable area yields (2-ring BFS like City.GetTerritoryTiles) ──
        float totalFood = 0f, totalProd = 0f, totalGold = 0f, totalScience = 0f;
        int landTiles = 0, hillTiles = 0, waterTiles = 0;
        int uniqueResourceTypes = 0;
        var seenResources = new System.Collections.Generic.HashSet<string>();
        bool hasRiver = false, hasCoast = false;

        var visited = new System.Collections.Generic.HashSet<int>();
        var queue = new System.Collections.Generic.Queue<(int tile, int ring)>();
        queue.Enqueue((tileIndex, 0));
        visited.Add(tileIndex);

        while (queue.Count > 0)
        {
            var (tile, ring) = queue.Dequeue();
            var ntd = ts.GetTileData(tile);
            if (ntd == null) continue;

            float ringWeight = ring == 0 ? 1f : (ring == 1 ? 0.6f : 0.3f);
            score += GetMosquitoPenalty(worker.owner, ntd, ringWeight);
            if (ntd.isLand)
            {
                var ny = ntd.GetTotalYield();
                totalFood    += ny.Food * ringWeight;
                totalProd    += ny.Production * ringWeight;
                totalGold    += ny.Gold * ringWeight;
                totalScience += ny.Science * ringWeight;
                landTiles++;
                if (ntd.isHill) hillTiles++;
                if (ntd.resource != null)
                {
                    string rname = ntd.resource.resourceName;
                    if (!string.IsNullOrEmpty(rname) && !seenResources.Contains(rname))
                    {
                        seenResources.Add(rname);
                        uniqueResourceTypes++;
                    }
                }
            }
            else
            {
                waterTiles++;
                hasCoast = true;
            }
            if (ntd.waterType == TileWaterType.River) hasRiver = true;

            if (ring < 2)
            {
                int[] neighbors = ts.GetNeighbors(tile);
                if (neighbors != null)
                {
                    foreach (int n in neighbors)
                    {
                        if (n >= 0 && !visited.Contains(n)) { visited.Add(n); queue.Enqueue((n, ring + 1)); }
                    }
                }
            }
        }

        // Yield scoring (food is king for growth, production for building)
        score += totalFood * W_SETTLE_YIELD * 1.2f;
        score += totalProd * W_SETTLE_YIELD * 0.8f;
        score += totalGold * W_SETTLE_YIELD * 0.4f;
        score += totalScience * W_SETTLE_YIELD * 0.3f;

        // Resource diversity: unique resources are very valuable
        score += uniqueResourceTypes * W_RESOURCE_UNIQUE * 0.5f;

        // Fresh water: rivers boost city growth
        if (hasRiver) score += 6f;
        // Coast: naval access
        if (hasCoast) score += 3f;

        // Defensibility: hills in the area help
        score += hillTiles * 1.5f;
        // Penalty for too much water (less workable land)
        if (landTiles < 5) score -= (5 - landTiles) * 3f;

        // ── Safety ──
        score += dangerMap.GetDanger(tileIndex) * W_DANGER_PENALTY;

        // ── City spacing ──
        if (worker.owner != null && worker.owner.cities != null)
        {
            int closestDist = int.MaxValue;
            foreach (var city in worker.owner.cities)
            {
                if (city == null) continue;
                int dist = ts.GetTileDistance(tileIndex, city.centerTileIndex);
                if (dist < closestDist) closestDist = dist;
                if (dist < 4) return float.MinValue; // hard minimum
            }
            // Sweet spot: 5–8 tiles from nearest city
            if (closestDist >= 5 && closestDist <= 8) score += W_SETTLE_DISTANCE * 4f;
            else if (closestDist >= 4 && closestDist < 5) score += W_SETTLE_DISTANCE * 1f;
            else if (closestDist > 8 && closestDist <= 12) score += W_SETTLE_DISTANCE * 1f;
            else if (closestDist > 12) score -= W_SETTLE_DISTANCE * 3f;
        }
        else
        {
            score += 10f; // first city bonus
        }

        // ── Avoid overlap with other civs' territory ──
        if (td.owner != null && td.owner != worker.owner) score -= 8f;

        return score;
    }

    // ──────────────────────── Resource Prioritization ────────────────────────

    /// <summary>
    /// Score a resource tile for prioritized collection/improvement. Considers what the civ
    /// already has (diminishing returns) and what it needs (food when hungry, production for building).
    /// </summary>
    public static float ScoreResourceTile(Civilization civ, HexTileData tileData, int tileIndex, DangerMap dangerMap)
    {
        if (tileData == null || tileData.resource == null) return 0f;
        var rd = tileData.resource;
        float score = 0f;

        // Yield value
        score += rd.foodPerTurn * W_RESOURCE_YIELD * 1.2f;
        score += rd.productionPerTurn * W_RESOURCE_YIELD;
        score += rd.goldPerTurn * W_RESOURCE_YIELD * 0.7f;
        score += rd.sciencePerTurn * W_RESOURCE_YIELD * 0.8f;

        // Forage value (one-off food especially valuable early)
        bool earlyGame = civ.cities == null || civ.cities.Count == 0;
        if (earlyGame)
            score += rd.forageFood * W_FORAGE_FOOD * 1.5f;

        // Uniqueness: resources the civ doesn't have yet are worth more
        int existingCount = civ.GetResourceCount(rd);
        if (existingCount == 0)
            score += W_RESOURCE_UNIQUE;
        else
            score += W_RESOURCE_STRATEGIC / (1f + existingCount); // diminishing returns

        // Civ needs: food when hungry, production when building
        if (civ.food < 10) score += rd.foodPerTurn * 3f;
        if (civ.food < 10) score += rd.forageFood * 2f;

        // Safety
        score += dangerMap.GetDanger(tileIndex) * W_DANGER_PENALTY * 0.5f;

        return score;
    }

    // ──────────────────────── Build Improvement scoring ────────────────────────

    public static float ScoreBuildImprovement(WorkerUnit worker, ImprovementData imp, int tileIndex, DangerMap dangerMap)
    {
        float score = 0f;
        if (imp == null) return float.MinValue;

        // Shelter urgency: huge bonus when winter is approaching
        if (imp.isShelter && ClimateManager.Instance != null)
        {
            int turnsUntilWinter = ClimateManager.Instance.GetTurnsUntilWinter(worker.planetIndex);
            if (turnsUntilWinter <= ClimateManager.Instance.turnsPerSeason + 1)
                score += W_SHELTER_URGENCY * (1f + 1f / Mathf.Max(1, turnsUntilWinter));
        }

        // Yield improvements
        score += imp.foodPerTurn * W_FORAGE_FOOD * 0.5f;
        score += imp.productionPerTurn * W_FORAGE_OTHER * 0.3f;
        score += imp.goldPerTurn * W_FORAGE_OTHER * 0.2f;

        score += dangerMap.GetDanger(tileIndex) * W_DANGER_PENALTY * 0.3f;
        return score;
    }

    // ──────────────────────── Improvement Upgrade scoring ────────────────────────

    public static float ScoreUpgrade(ImprovementUpgradeData upgrade, int tileIndex, DangerMap dangerMap)
    {
        if (upgrade == null) return float.MinValue;
        float score = 0f;
        score += upgrade.additionalFood * W_FORAGE_FOOD * 0.5f;
        score += (upgrade.additionalProduction + upgrade.additionalGold + upgrade.additionalPolicyPoints) * W_UPGRADE_VALUE * 0.3f;
        score += (upgrade.defenseAdd + upgrade.defensePct * 10f) * W_TERRAIN_DEFENSE;
        score += upgrade.additionalShelterCapacity * W_SHELTER_URGENCY * 0.3f;
        return score;
    }

    // ──────────────────────── Fortify scoring ────────────────────────

    public static float ScoreFortify(BaseUnit unit, DangerMap dangerMap)
    {
        float score = W_FORTIFY_BASE;
        // More valuable when on defensive terrain
        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        if (ts != null)
        {
            var td = ts.GetTileData(unit.currentTileIndex);
            if (td != null)
            {
                score += (td.improvementDefenseAdd + td.improvementDefensePct * 10f) * W_TERRAIN_DEFENSE;
                if (td.isHill) score += W_HILL_BONUS;
            }
        }
        // More valuable at high health (holding a position)
        float hpRatio = (float)unit.currentHealth / Mathf.Max(1, unit.MaxHealth);
        score += hpRatio * 2f;
        return score;
    }

    // ──────────────────────── War Timing ────────────────────────

    /// <summary>
    /// Two-phase war evaluation:
    ///   Phase A — WAR DESIRE: do we WANT to fight them? (diplomacy, coveted resources, desperation, grievances)
    ///   Phase B — WAR TIMING: is NOW a good time? (openings, seasons, military readiness, enemy distracted)
    /// War only happens when desire is strong enough AND timing is favorable.
    /// A civ with no grievances won't attack even with a perfect opening.
    /// A civ with deep grudges will wait for the right moment.
    /// </summary>
    public static float ScoreWarDecision(Civilization attacker, Civilization defender)
    {
        if (attacker == null || defender == null) return float.MinValue;

        // ════════════════════════════════════════════════════════
        //  PHASE A — WAR DESIRE (do we want to fight them?)
        //  This is the PRIMARY driver. Without desire, timing is irrelevant.
        // ════════════════════════════════════════════════════════

        float desire = 0f;

        // ── Diplomatic relationship: the #1 factor ──
        float reputation = 0f;
        int trustLevel = 5;
        try
        {
            if (DiplomacyManager.Instance != null)
            {
                var memory = DiplomacyManager.Instance.GetDiplomaticMemory(attacker);
                reputation = memory.GetReputation(defender);   // -100 to +100
                trustLevel = memory.GetTrustLevel(defender);   // 0 to 10
            }
        }
        catch { }

        // Reputation is the core diplomatic signal
        // Negative reputation = they've wronged us. Positive = they've been friendly.
        desire += -reputation * 0.4f; // rep=-80 → desire +32; rep=+60 → desire -24

        // Trust level gates aggression: high trust makes war nearly impossible
        if (trustLevel >= 8) desire -= 30f;       // trusted friend
        else if (trustLevel >= 6) desire -= 12f;  // decent relations
        else if (trustLevel <= 2) desire += 10f;  // hostile
        else if (trustLevel <= 1) desire += 18f;  // bitter enemy

        // Past grievances: they declared war on us before, broke peace, attacked our ally
        try
        {
            if (DiplomacyManager.Instance != null)
            {
                var memory = DiplomacyManager.Instance.GetDiplomaticMemory(attacker);
                if (memory.HasRecentEvent(defender, DiplomaticEventType.DeclaredWar, 30)) desire += 15f;
                if (memory.HasRecentEvent(defender, DiplomaticEventType.BrokePeace, 20)) desire += 12f;
                if (memory.HasRecentEvent(defender, DiplomaticEventType.AttackedAlly, 20)) desire += 10f;
                if (memory.HasRecentEvent(defender, DiplomaticEventType.Denounced, 15)) desire += 5f;
            }
        }
        catch { }

        // ── They have things we want ──
        // Resources they have that we don't
        int covetedResources = 0;
        try
        {
            if (defender.resourceStockpile != null)
            {
                foreach (var kv in defender.resourceStockpile)
                {
                    if (kv.Value > 0 && attacker.GetResourceCount(kv.Key) == 0)
                        covetedResources++;
                }
            }
        }
        catch { }
        desire += covetedResources * 3f;

        // Territory we want: they own tiles adjacent to our territory (border friction)
        bool sharesBorder = false;
        int contestedBorderTiles = 0;
        try
        {
            if (attacker.ownedTilesByPlanet != null && defender.ownedTilesByPlanet != null)
            {
                foreach (var kv in attacker.ownedTilesByPlanet)
                {
                    if (!defender.ownedTilesByPlanet.TryGetValue(kv.Key, out var defTiles) || defTiles == null) continue;
                    var ts = TileSystem.GetForPlanet(kv.Key);
                    if (ts == null) continue;
                    foreach (int tile in kv.Value)
                    {
                        int[] neighbors = ts.GetNeighbors(tile);
                        if (neighbors == null) continue;
                        foreach (int n in neighbors)
                        {
                            if (defTiles.Contains(n)) { sharesBorder = true; contestedBorderTiles++; }
                        }
                    }
                }
            }
        }
        catch { }
        desire += Mathf.Min(10f, contestedBorderTiles * 0.5f); // border friction

        // ── Desperation: famine, losing cities, existential threat ──
        int myCities = attacker.cities != null ? attacker.cities.Count : 0;
        if (attacker.food <= 0 && myCities > 0) desire += 12f;  // starving — must take food sources
        if (attacker.food <= 0 && myCities == 0) desire += 8f;  // nomadic famine
        // If they have many cities and we have few, conquest is attractive
        int theirCities = defender.cities != null ? defender.cities.Count : 0;
        if (theirCities > myCities && myCities > 0)
            desire += (theirCities - myCities) * 2f;

        // ── Leader personality modifies desire ──
        if (attacker.leader != null)
        {
            if (attacker.leader.isWarmonger) desire += 10f;
            if (attacker.leader.primaryAgenda == LeaderAgenda.Militaristic) desire += 6f;
            else if (attacker.leader.secondaryAgenda == LeaderAgenda.Militaristic) desire += 3f;
            if (attacker.leader.primaryAgenda == LeaderAgenda.Diplomatic) desire -= 15f;
            else if (attacker.leader.secondaryAgenda == LeaderAgenda.Diplomatic) desire -= 7f;
            if (attacker.leader.primaryAgenda == LeaderAgenda.Scientific) desire -= 5f;
            else if (attacker.leader.secondaryAgenda == LeaderAgenda.Scientific) desire -= 2.5f;
            if (attacker.leader.primaryAgenda == LeaderAgenda.Religious) desire -= 3f;
            else if (attacker.leader.secondaryAgenda == LeaderAgenda.Religious) desire -= 1.5f;
        }

        // ── Diplomatic fallout: will allies turn on us? ──
        int currentWars = 0;
        if (attacker.relations != null)
            foreach (var r in attacker.relations.Values)
                if (r == DiplomaticState.War) currentWars++;
        desire -= currentWars * 10f; // already fighting — don't open another front

        if (defender.relations != null)
        {
            int defenderAllies = 0;
            foreach (var r in defender.relations)
                if (r.Value == DiplomaticState.Alliance && r.Key != attacker) defenderAllies++;
            desire -= defenderAllies * 5f;
        }

        // ── GATE: if desire is not positive, don't even evaluate timing ──
        if (desire <= 0f) return desire;

        // ════════════════════════════════════════════════════════
        //  PHASE B — WAR TIMING (is now a good moment to strike?)
        //  Only evaluated when desire > 0. Modifies the final score.
        // ════════════════════════════════════════════════════════

        float timing = 0f;

        // ── Military readiness ──
        float myStrength = CivilizationManager.Instance != null
            ? CivilizationManager.Instance.ComputeMilitaryStrength(attacker) : 0f;
        float theirStrength = CivilizationManager.Instance != null
            ? CivilizationManager.Instance.ComputeMilitaryStrength(defender) : 1f;
        float ratio = myStrength / Mathf.Max(1f, theirStrength);
        timing += (ratio - 1f) * 8f; // moderate weight: advantage helps but doesn't decide

        // ── Opening: undefended enemy city within striking distance ──
        try
        {
            if (defender.cities != null && attacker.combatUnits != null)
            {
                foreach (var city in defender.cities)
                {
                    if (city == null) continue;
                    foreach (var unit in attacker.combatUnits)
                    {
                        if (unit == null || unit.currentTileIndex < 0 || unit.planetIndex != city.planetIndex) continue;
                        var ts = TileSystem.GetForPlanet(unit.planetIndex);
                        if (ts == null) continue;
                        int dist = ts.GetTileDistance(unit.currentTileIndex, city.centerTileIndex);
                        if (dist > 6) continue;
                        bool defended = false;
                        if (defender.combatUnits != null)
                        {
                            foreach (var d in defender.combatUnits)
                            {
                                if (d == null || d.planetIndex != city.planetIndex) continue;
                                if (ts.GetTileDistance(d.currentTileIndex, city.centerTileIndex) <= 3) { defended = true; break; }
                            }
                        }
                        if (!defended) { timing += 10f; goto doneOpening; }
                    }
                }
                doneOpening:;
            }
        }
        catch { }

        // ── Enemy distracted: already at war with someone else ──
        if (defender.relations != null)
        {
            foreach (var r in defender.relations)
            {
                if (r.Value == DiplomaticState.War && r.Key != attacker)
                { timing += 8f; break; }
            }
        }

        // ── Seasonal timing ──
        try
        {
            if (ClimateManager.Instance != null)
            {
                int pIndex = attacker.combatUnits != null && attacker.combatUnits.Count > 0
                    ? attacker.combatUnits[0].planetIndex : 0;
                Season s = ClimateManager.Instance.GetSeasonForPlanet(pIndex);
                int turnsToWinter = ClimateManager.Instance.GetTurnsUntilWinter(pIndex);
                if (s == Season.Spring) timing += 5f;
                else if (s == Season.Summer) timing += 2f;
                else if (s == Season.Autumn) timing -= 4f;
                if (turnsToWinter <= 2) timing -= 15f;
            }
        }
        catch { }

        // ── Economy: can we sustain a campaign? ──
        if (attacker.food < 10 && myCities > 0) timing -= 6f;
        float myProd = 0f;
        if (attacker.cities != null)
            foreach (var city in attacker.cities) if (city != null) myProd += city.GetProductionPerTurn();
        timing += Mathf.Min(6f, myProd * 0.3f);

        // ── Tech edge ──
        int myTechs = attacker.researchedTechs != null ? attacker.researchedTechs.Count : 0;
        int theirTechs = defender.researchedTechs != null ? defender.researchedTechs.Count : 0;
        timing += (myTechs - theirTechs) * 1f;

        // ── Geography ──
        if (sharesBorder) timing += 4f;
        else timing -= 6f; // no border = can't project force

        // ════════════════════════════════════════════════════════
        //  FINAL SCORE: desire drives the decision, timing adjusts it
        // ════════════════════════════════════════════════════════

        return desire + timing;
    }

    // ──────────────────────── Exploration scoring ────────────────────────

    /// <summary>
    /// Score a tile as an exploration target. Prefers tiles that border many unexplored tiles
    /// and are reasonably safe/close.
    /// </summary>
    public static float ScoreExplore(BaseUnit unit, int tileIndex, int unexploredNeighborCount, DangerMap dangerMap)
    {
        float score = W_EXPLORE_BASE;
        score += unexploredNeighborCount * W_EXPLORE_UNEXPLORED;
        score += dangerMap.GetDanger(tileIndex) * W_DANGER_PENALTY;

        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        if (ts != null)
        {
            int dist = ts.GetTileDistance(unit.currentTileIndex, tileIndex);
            score += dist * W_EXPLORE_DISTANCE;
            score += GetMosquitoPenalty(unit, ts.GetTileData(tileIndex));
        }

        return score;
    }

    // ──────────────────────── Command scoring wrapper ────────────────────────

    /// <summary>
    /// Score any AICommand. Dispatches to the appropriate specialized scorer.
    /// </summary>
    public static float ScoreCommand(AICommand cmd, DangerMap dangerMap)
    {
        if (cmd == null) return float.MinValue;

        switch (cmd)
        {
            case AIAttackCommand atk:
                return ScoreAttack(atk.unit, atk.target, dangerMap);

            case AIApproachCommand app:
                return ScoreTileForMovement(app.unit, app.approachTileIndex,
                    app.target != null ? app.target.currentTileIndex : -1, dangerMap) + 3f;

            case AIRetreatCommand ret:
                return ScoreRetreat(ret.unit, ret.retreatTileIndex, dangerMap);

            case AIForageCommand fg:
                return fg.score; // pre-scored during generation

            case AISettleCityCommand sc:
                return sc.score;

            case AIBuildImprovementCommand bi:
                return bi.score;

            case AIFortifyCommand ft:
                return ft.score;

            case AIMoveCommand mv:
                return ScoreTileForMovement(mv.unit, mv.targetTileIndex, -1, dangerMap);

            case AIExploreCommand ex:
                return ex.score; // pre-scored during generation

            default:
                return cmd.score;
        }
    }
}
