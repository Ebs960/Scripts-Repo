// Assets/Scripts Repo/MissileManager.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Singleton that owns the missile system runtime.
/// Handles: launch from city / unit / silo, parabolic flight animation,
/// blast detonation, city destruction, radiation pollution, and save/load.
/// Register on a persistent GameObject alongside GameManager.
/// </summary>
public class MissileManager : MonoBehaviour, ISaveGameParticipant
{
    public static MissileManager Instance { get; private set; }

    // ─── Events ──────────────────────────────────────────────────────────────
    /// <summary>Fired when a missile leaves its launcher (sourceTile, data, planetIndex).</summary>
    public event Action<int, MissileData, int> OnMissileLaunched;
    /// <summary>Fired when a missile impacts its target tile (targetTile, data, planetIndex).</summary>
    public event Action<int, MissileData, int> OnMissileDetonated;
    /// <summary>Fired when a missile is intercepted before impact (sourceTile, targetTile, data, defender, planetIndex).</summary>
    public event Action<int, int, MissileData, CombatUnit, int> OnMissileIntercepted;

    // ─── Silo Missile Inventory ───────────────────────────────────────────────
    // Key = compound of (planetIndex, tileIndex); stored separately from City/Unit because
    // improvements have no runtime MonoBehaviour to hold state.
    private readonly Dictionary<long, List<MissileData>> _siloInventories =
        new Dictionary<long, List<MissileData>>();

    // ─── ISaveGameParticipant ─────────────────────────────────────────────────
    public string SaveKey => "MissileManager_v1";

    // ─── Lifecycle ───────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SaveGameRegistry.Register(this);
    }

    private void OnDestroy()
    {
        SaveGameRegistry.Unregister(this);
        if (Instance == this) Instance = null;
    }

    // ─── Silo Storage API ────────────────────────────────────────────────────
    private static long MakeSiloKey(int planetIndex, int tileIndex) =>
        ((long)(uint)planetIndex << 32) | (uint)tileIndex;

    public List<MissileData> GetSiloMissiles(int planetIndex, int tileIndex)
    {
        long key = MakeSiloKey(planetIndex, tileIndex);
        if (!_siloInventories.TryGetValue(key, out var list))
        {
            list = new List<MissileData>();
            _siloInventories[key] = list;
        }
        return list;
    }

    public bool SiloHasMissiles(int planetIndex, int tileIndex) =>
        GetSiloMissiles(planetIndex, tileIndex).Count > 0;

    public void AddMissileToSilo(int planetIndex, int tileIndex, MissileData missile)
    {
        if (missile == null) return;
        GetSiloMissiles(planetIndex, tileIndex).Add(missile);
    }

    public bool RemoveMissileFromSilo(int planetIndex, int tileIndex, MissileData missile)
    {
        return GetSiloMissiles(planetIndex, tileIndex).Remove(missile);
    }

    public int GetSiloMissileCapacity(int planetIndex, int tileIndex)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return 0;
        var tile = ts.GetTileData(tileIndex);
        if (tile?.improvement == null) return 0;
        return tile.improvement.isMissileSilo ? tile.improvement.siloMissileCapacity : 0;
    }

    // ─── Launch API ──────────────────────────────────────────────────────────
    /// <summary>Launch a missile from a city's storedMissiles inventory.</summary>
    public void LaunchFromCity(City city, MissileData missile, int targetTileIndex)
    {
        if (city == null || missile == null) return;
        if (!city.storedMissiles.Remove(missile))
        {
            Debug.LogWarning($"[MissileManager] City '{city.cityName}' has no missile '{missile.missileName}'.");
            return;
        }
        var ts = TileSystem.GetForPlanet(city.planetIndex) ?? TileSystem.Instance;
        Vector3 launchPos = ts != null ? ts.GetTileCenterFlat(city.centerTileIndex) : city.transform.position;
        OnMissileLaunched?.Invoke(city.centerTileIndex, missile, city.planetIndex);
        if (TryInterceptMissile(city.owner, city.centerTileIndex, targetTileIndex, city.planetIndex, missile)) return;
        StartCoroutine(FlightCoroutine(missile, launchPos, targetTileIndex, city.planetIndex));
    }

    /// <summary>Launch a missile from a combat unit's storedMissiles inventory.</summary>
    public void LaunchFromUnit(CombatUnit unit, MissileData missile, int targetTileIndex)
    {
        if (unit == null || missile == null) return;
        if (!unit.storedMissiles.Remove(missile))
        {
            Debug.LogWarning($"[MissileManager] Unit '{unit.data?.unitName}' has no missile '{missile.missileName}'.");
            return;
        }
        var ts = TileSystem.GetForPlanet(unit.planetIndex) ?? TileSystem.Instance;
        Vector3 launchPos = ts != null ? ts.GetTileCenterFlat(unit.currentTileIndex) : unit.transform.position;
        OnMissileLaunched?.Invoke(unit.currentTileIndex, missile, unit.planetIndex);
        if (TryInterceptMissile(unit.owner, unit.currentTileIndex, targetTileIndex, unit.planetIndex, missile)) return;
        StartCoroutine(FlightCoroutine(missile, launchPos, targetTileIndex, unit.planetIndex));
    }

    /// <summary>Launch a missile from a silo improvement on the given tile.</summary>
    public void LaunchFromSilo(int siloTileIndex, int planetIndex, MissileData missile, int targetTileIndex)
    {
        if (missile == null) return;
        if (!RemoveMissileFromSilo(planetIndex, siloTileIndex, missile))
        {
            Debug.LogWarning($"[MissileManager] Silo tile {siloTileIndex} (planet {planetIndex}) has no missile '{missile.missileName}'.");
            return;
        }
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        Vector3 launchPos = ts != null ? ts.GetTileCenterFlat(siloTileIndex) : Vector3.zero;
        Civilization sourceOwner = ResolveTileOwner(planetIndex, siloTileIndex);
        OnMissileLaunched?.Invoke(siloTileIndex, missile, planetIndex);
        if (TryInterceptMissile(sourceOwner, siloTileIndex, targetTileIndex, planetIndex, missile)) return;
        StartCoroutine(FlightCoroutine(missile, launchPos, targetTileIndex, planetIndex));
    }


    // ─── Interception Helpers ───────────────────────────────────────────────
    private bool TryInterceptMissile(Civilization sourceOwner, int sourceTile, int targetTile, int planetIndex, MissileData missile)
    {
        if (missile == null || !missile.canBeIntercepted) return false;

        CombatUnit defender = FindBestMissileInterceptor(sourceOwner, targetTile, planetIndex, missile);
        if (defender == null || defender.data == null) return false;

        float chance = CalculateMissileInterceptionChance(defender, missile);
        bool intercepted = UnityEngine.Random.value <= chance;
        if (!intercepted) return false;

        defender.TryConsumeAttackPoint();
        OnMissileIntercepted?.Invoke(sourceTile, targetTile, missile, defender, planetIndex);
        if ((defender.owner != null && defender.owner.isPlayerControlled) || (sourceOwner != null && sourceOwner.isPlayerControlled))
            UIManager.Instance?.ShowNotification($"{defender.UnitName} intercepted incoming missile {missile.missileName}.");
        return true;
    }

    private static CombatUnit FindBestMissileInterceptor(Civilization sourceOwner, int targetTile, int planetIndex, MissileData missile)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return null;

        CombatUnit best = null;
        float bestScore = float.MinValue;
        foreach (CombatUnit candidate in UnitRegistry.GetCombatUnits())
        {
            if (!AircraftMissionManager.IsValidAntiAir(candidate) || candidate.planetIndex != planetIndex) continue;
            if (!AircraftMissionManager.IsHostile(candidate.owner, sourceOwner)) continue;
            if (candidate.data.antiAirRange < missile.minimumInterceptorRange) continue;

            int distance = Mathf.RoundToInt(ts.GetTileDistance(candidate.currentTileIndex, targetTile));
            if (distance > candidate.data.antiAirRange) continue;

            float score = candidate.CurrentAirAttack + candidate.CurrentDefense - distance;
            if (score > bestScore) { best = candidate; bestScore = score; }
        }
        return best;
    }

    private static float CalculateMissileInterceptionChance(CombatUnit defender, MissileData missile)
    {
        float baseChance = defender?.data != null ? defender.data.antiAirInterceptionChance : 0f;
        float statBonus = defender != null ? Mathf.Clamp01(defender.CurrentAirAttack / 100f) * 0.35f : 0f;
        return Mathf.Clamp01(baseChance + statBonus - missile.interceptionEvasion);
    }

    private static Civilization ResolveTileOwner(int planetIndex, int tileIndex)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        var tile = ts != null ? ts.GetTileData(tileIndex) : null;
        return tile != null ? tile.owner : null;
    }

    // ─── Flight Coroutine ────────────────────────────────────────────────────
    private IEnumerator FlightCoroutine(MissileData data, Vector3 launchPos, int targetTileIndex, int planetIndex)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        Vector3 targetPos = ts != null ? ts.GetTileCenterFlat(targetTileIndex) : Vector3.zero;

        float duration = Mathf.Max(0.1f, data.flightDuration);

        // Try to use MissileProjectileController on the flight prefab for designer-controlled arcs.
        // Fall back to manual lerp if controller isn't present.
        if (data.flightPrefab != null)
        {
            var go = Instantiate(data.flightPrefab, launchPos, Quaternion.identity);
            var controller = go.GetComponent<MissileProjectileController>();

            if (controller != null)
            {
                bool done = false;
                controller.StartFlight(launchPos, targetPos, data.arcHeight, duration, () => done = true);
                yield return new WaitUntil(() => done);
                Destroy(go);
            }
            else
            {
                // Manual parabolic arc fallback
                yield return ManualArcCoroutine(go, launchPos, targetPos, data.arcHeight, duration);
                Destroy(go);
            }
        }
        else
        {
            // No prefab — just wait for flight duration
            yield return new WaitForSeconds(duration);
        }

        Detonate(data, targetPos, targetTileIndex, planetIndex);
    }

    private static IEnumerator ManualArcCoroutine(GameObject projectile, Vector3 from, Vector3 to,
        float arcHeight, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            Vector3 pos = Vector3.Lerp(from, to, t);
            pos.y += arcHeight * 4f * t * (1f - t);

            if (projectile != null)
            {
                float tNext = Mathf.Clamp01((elapsed + 0.016f) / duration);
                Vector3 next = Vector3.Lerp(from, to, tNext);
                next.y += arcHeight * 4f * tNext * (1f - tNext);
                Vector3 dir = next - pos;
                if (dir.sqrMagnitude > 0.0001f)
                    projectile.transform.rotation = Quaternion.LookRotation(dir.normalized);
                projectile.transform.position = pos;
            }

            yield return null;
        }
    }

    // ─── Detonation ──────────────────────────────────────────────────────────
    private void Detonate(MissileData data, Vector3 impactWorldPos, int targetTileIndex, int planetIndex)
    {
        if (data.impactPrefab != null)
            Instantiate(data.impactPrefab, impactWorldPos, Quaternion.identity);

        if (data.isNuclear && data.nuclearFlashPrefab != null)
            Instantiate(data.nuclearFlashPrefab, impactWorldPos, Quaternion.identity);

        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null)
        {
            OnMissileDetonated?.Invoke(targetTileIndex, data, planetIndex);
            return;
        }

        var blastTiles = GetTilesInRadius(ts, targetTileIndex, data.blastRadius);
        foreach (int tileIdx in blastTiles)
        {
            var tile = ts.GetTileData(tileIdx);
            if (tile == null) continue;

            // Unit damage
            if (data.blastUnitDamage > 0)
                DamageUnitsOnTile(tileIdx, planetIndex, data.blastUnitDamage);

            // City damage / destruction
            if (data.cityDamage > 0)
                DamageCityOnTile(tile, data);

            // Apply pollution (nuclear or any missile with pollutionLevel > 0)
            if (data.pollutionLevel > 0 && data.pollutionDuration > 0)
            {
                // Additive stacking: take the max intensity, extend duration
                tile.pollutionLevel = Mathf.Max(tile.pollutionLevel, data.pollutionLevel);
                tile.pollutionTurnsRemaining = Mathf.Max(tile.pollutionTurnsRemaining, data.pollutionDuration);
            }
        }

        OnMissileDetonated?.Invoke(targetTileIndex, data, planetIndex);
    }

    // ─── Blast Helpers ───────────────────────────────────────────────────────
    /// <summary>Returns all tile indices within <paramref name="radius"/> tiles of <paramref name="center"/> (BFS).</summary>
    public static List<int> GetTilesInRadius(TileSystem ts, int center, int radius)
    {
        var result = new HashSet<int> { center };
        if (radius <= 0) return new List<int>(result);

        var frontier = new List<int> { center };
        for (int r = 0; r < radius; r++)
        {
            var next = new List<int>();
            foreach (int tile in frontier)
            {
                var neighbors = ts.GetNeighbors(tile);
                if (neighbors == null) continue;
                foreach (int n in neighbors)
                    if (result.Add(n)) next.Add(n);
            }
            frontier = next;
            if (frontier.Count == 0) break;
        }
        return new List<int>(result);
    }

    private static void DamageUnitsOnTile(int tileIndex, int planetIndex, int damage)
    {
        if (damage <= 0) return;
        var occ = TileOccupancyManager.GetForPlanet(planetIndex) ?? TileOccupancyManager.Instance;
        if (occ == null) return;

        var objects = occ.GetAllOccupantObjects(tileIndex, TileLayer.Surface);
        foreach (var go in objects)
        {
            if (go == null) continue;

            var combat = go.GetComponent<CombatUnit>();
            if (combat != null) { combat.ApplyDamage(damage); continue; }

            var worker = go.GetComponent<WorkerUnit>();
            if (worker != null) worker.ApplyDamage(damage);
        }
    }

    private static void DamageCityOnTile(HexTileData tile, MissileData data)
    {
        var city = tile.controllingCity;
        if (city == null) return;

        city.defenseRating = Mathf.Max(0, city.defenseRating - data.cityDamage);

        if (data.canWipeCity && city.defenseRating <= 0)
        {
            string name = city.cityName;
            city.owner?.RemoveCity(city);
            UIManager.Instance?.ShowNotification($"{name} has been obliterated by a missile strike!");
            Destroy(city.gameObject);
        }
        else
        {
            UIManager.Instance?.ShowNotification(
                $"{city.cityName} was struck! Defense: {city.defenseRating}/{city.maxDefense}");
        }
    }

    // ─── Pollution Tick (called from ClimateManager each turn) ───────────────
    /// <summary>Ticks pollution decay and applies per-turn pollution damage to units on contaminated tiles.</summary>
    public void ProcessPollutionTick(int planetIndex)
    {
        var ts = TileSystem.GetForPlanet(planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;

        var planet = GameManager.Instance?.GetPlanetGenerator(planetIndex);
        int count = (planet != null && planet.Grid != null) ? planet.Grid.TileCount : 0;
        for (int i = 0; i < count; i++)
        {
            var tile = ts.GetTileData(i);
            if (tile == null || tile.pollutionTurnsRemaining <= 0) continue;

            // Per-turn unit damage from radiation
            if (tile.pollutionLevel > 0)
                DamageUnitsOnTile(i, planetIndex, tile.pollutionLevel);

            tile.pollutionTurnsRemaining--;
            if (tile.pollutionTurnsRemaining <= 0)
                tile.pollutionLevel = 0;
        }
    }

    // ─── Range Utilities (public for use by MissileLaunchMode) ──────────────
    /// <summary>Returns true if <paramref name="targetTile"/> is within <paramref name="range"/> tiles of <paramref name="sourceTile"/>.</summary>
    public static bool IsInMissileRange(TileSystem ts, int sourceTile, int targetTile, int range)
    {
        if (ts == null) return false;
        return Mathf.RoundToInt(ts.GetTileDistance(sourceTile, targetTile)) <= range;
    }

    /// <summary>Returns all tile indices within missile range of a source tile.</summary>
    public static List<int> GetTilesInMissileRange(TileSystem ts, int sourceTile, int range) =>
        GetTilesInRadius(ts, sourceTile, range);

    // ─── Save / Load ─────────────────────────────────────────────────────────
    [Serializable]
    private class SiloEntry
    {
        public long key;
        public List<string> missileNames = new List<string>();
    }

    [Serializable]
    private class SaveData
    {
        public List<SiloEntry> silos = new List<SiloEntry>();
    }

    public string CaptureStateJson()
    {
        var save = new SaveData();
        foreach (var kvp in _siloInventories)
        {
            if (kvp.Value == null || kvp.Value.Count == 0) continue;
            save.silos.Add(new SiloEntry
            {
                key = kvp.Key,
                missileNames = kvp.Value
                    .Where(m => m != null)
                    .Select(m => m.missileName)
                    .ToList()
            });
        }
        return JsonUtility.ToJson(save);
    }

    public void RestoreStateJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return;
        var save = JsonUtility.FromJson<SaveData>(json);
        if (save == null) return;

        // Build name → asset lookup from all loaded MissileData assets
        var lookup = Resources.FindObjectsOfTypeAll<MissileData>()
            .Where(m => m != null)
            .GroupBy(m => m.missileName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        _siloInventories.Clear();
        foreach (var entry in save.silos)
        {
            var list = new List<MissileData>();
            foreach (var name in entry.missileNames)
            {
                if (lookup.TryGetValue(name, out var md)) list.Add(md);
            }
            _siloInventories[entry.key] = list;
        }
    }
}
