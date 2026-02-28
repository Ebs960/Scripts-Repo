using UnityEngine;

/// <summary>
/// Plays biome-driven ambient loops and water-proximity sounds, both scaled by camera altitude.
///
/// Features:
///   1. Biome ambient — crossfades between two AudioSources when the biome under the camera changes.
///   2. Water proximity — an additive layer that blends in when the camera is near river/lake/ocean tiles.
///   3. Altitude scaling — all volumes lerp toward silence as the camera zooms out.
///
/// Setup:
///   Attach to any always-active GameObject (e.g. the same one with PlanetaryCameraManager).
///   Assign an AmbientSoundDatabase asset and a PlanetaryCameraManager reference.
///   TileSystem and HexGrid are located automatically at runtime.
/// </summary>
public class AmbientSoundManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("ScriptableObject that maps biomes → clips, water types → clips, and altitude curves.")]
    [SerializeField] private AmbientSoundDatabase database;
    [Tooltip("Camera manager used for altitude and focus point. Auto-found if null.")]
    [SerializeField] private PlanetaryCameraManager cameraManager;

    [Header("Crossfade")]
    [Tooltip("Seconds to crossfade between biome ambient loops when the biome changes.")]
    [SerializeField] private float crossfadeDuration = 1.5f;

    [Header("Update Rate")]
    [Tooltip("Seconds between tile-lookup updates (saves CPU vs every frame).")]
    [SerializeField] private float updateInterval = 0.25f;

    // ── Biome crossfade state ──
    private AudioSource _biomeSourceA;
    private AudioSource _biomeSourceB;
    private bool _aIsActive = true;                    // which source is currently the 'live' one
    private float _crossfadeTimer = 0f;
    private bool _isCrossfading = false;
    private float _biomeBaseVolA;
    private float _biomeBaseVolB;
    private Biome _currentBiome = (Biome)(-1);         // sentinel: no biome yet

    // ── Water layer ──
    private AudioSource _waterSource;
    private TileWaterType _currentWaterType = TileWaterType.None;
    private float _waterTargetVolume = 0f;
    private float _waterBaseVolume = 0f;

    // ── Distance scaling ──
    private float _distanceMultiplier = 1f;
    private Vector3 _lastTileWorldPos;

    // ── Throttle ──
    private float _nextUpdate;

    // ── Cached refs ──
    private TileSystem _tileSystem;
    private HexGrid _grid;
    private PlanetGenerator _planetGenerator;

    // ================================================================

    private void Awake()
    {
        // Create the three AudioSources as hidden children.
        _biomeSourceA = CreateLoopSource("BiomeAmbient_A");
        _biomeSourceB = CreateLoopSource("BiomeAmbient_B");
        _waterSource  = CreateLoopSource("WaterProximity");

        _biomeSourceB.volume = 0f;
    }

    private void Start()
    {
        if (cameraManager == null)
            cameraManager = FindAnyObjectByType<PlanetaryCameraManager>();

        CacheRefs();
    }

    private void Update()
    {
        if (database == null || cameraManager == null) return;

        // ── Mute everything in orbit mode — biome/water sounds don't belong in space ──
        if (cameraManager.IsInOrbitMode)
        {
            _distanceMultiplier = 0f;
            ApplyVolumes();
            return;
        }

        // ── Distance multiplier (every frame for smoothness) ──
        if (Camera.main != null)
        {
            float dist = Vector3.Distance(Camera.main.transform.position, _lastTileWorldPos);
            _distanceMultiplier = database.GetDistanceMultiplier(dist);
        }

        ApplyVolumes();

        // ── Throttled tile lookup ──
        if (Time.time < _nextUpdate) return;
        _nextUpdate = Time.time + updateInterval;

        EvaluateTileUnderCamera();
    }

    // ================================================================
    //  Volume application (called every frame)
    // ================================================================

    private void ApplyVolumes()
    {
        // ── Crossfade tick ──
        if (_isCrossfading)
        {
            _crossfadeTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_crossfadeTimer / crossfadeDuration);
            if (_aIsActive)
            {
                _biomeSourceA.volume = _biomeBaseVolA * t * _distanceMultiplier;
                _biomeSourceB.volume = _biomeBaseVolB * (1f - t) * _distanceMultiplier;
            }
            else
            {
                _biomeSourceB.volume = _biomeBaseVolB * t * _distanceMultiplier;
                _biomeSourceA.volume = _biomeBaseVolA * (1f - t) * _distanceMultiplier;
            }

            if (t >= 1f)
            {
                _isCrossfading = false;
                var dead = _aIsActive ? _biomeSourceB : _biomeSourceA;
                dead.Stop();
                dead.clip = null;
                dead.volume = 0f;
            }
        }
        else
        {
            var active = _aIsActive ? _biomeSourceA : _biomeSourceB;
            if (active.isPlaying)
                active.volume = (_aIsActive ? _biomeBaseVolA : _biomeBaseVolB) * _distanceMultiplier;
        }

        // ── Water volume (smooth lerp) ──
        float wTarget = _waterTargetVolume * _distanceMultiplier;
        _waterSource.volume = Mathf.MoveTowards(_waterSource.volume, wTarget, Time.deltaTime / Mathf.Max(0.1f, crossfadeDuration));

        if (_waterSource.volume <= 0.001f && _waterSource.isPlaying && _waterTargetVolume <= 0f)
        {
            _waterSource.Stop();
            _waterSource.clip = null;
        }
    }

    // ================================================================
    //  Tile evaluation
    // ================================================================

    private void EvaluateTileUnderCamera()
    {
        CacheRefs();
        if (_grid == null || _tileSystem == null) return;

        // Use the physical camera footprint on the ground plane (XZ), not the focus point.
        // FocusPoint is the *look-at target* and can be far from the camera at shallow pitch.
        Vector3 samplePoint = GetCameraFootprintXZ();
        int tileIdx = _grid.GetTileAtPosition(samplePoint);
        if (tileIdx < 0) return;

        var td = _tileSystem.GetTileData(tileIdx);
        if (td == null) return;

        // Cache tile world position for distance calculation (updated at throttled rate,
        // interpolated every frame via the distance multiplier).
        Vector3 tileCenter = _grid.tileCenters[tileIdx];
        _lastTileWorldPos = new Vector3(tileCenter.x, tileCenter.y + td.elevation, tileCenter.z);

        // ── 1. Biome ambient ──
        Biome biome = td.biome;
        if (biome != _currentBiome)
        {
            _currentBiome = biome;
            StartBiomeCrossfade(biome);
        }

        // ── 2. Water proximity ──
        EvaluateWaterProximity(tileIdx, td, samplePoint);
    }

    private Vector3 GetCameraFootprintXZ()
    {
        Vector3 p;
        if (Camera.main != null)
            p = Camera.main.transform.position;
        else if (cameraManager != null)
            p = cameraManager.transform.position;
        else
            p = Vector3.zero;

        p.y = 0f;
        return p;
    }

    // ================================================================
    //  Biome crossfade
    // ================================================================

    private void StartBiomeCrossfade(Biome newBiome)
    {
        var entry = database.GetBiomeEntry(newBiome);
        AudioClip newClip = entry?.clip;
        float newVol = entry?.volume ?? 0f;

        // Flip active source.
        _aIsActive = !_aIsActive;

        var incoming = _aIsActive ? _biomeSourceA : _biomeSourceB;
        if (_aIsActive) _biomeBaseVolA = newVol; else _biomeBaseVolB = newVol;

        incoming.clip = newClip;
        incoming.volume = 0f;
        if (newClip != null) incoming.Play();

        _crossfadeTimer = 0f;
        _isCrossfading = true;
    }

    // ================================================================
    //  Water proximity
    // ================================================================

    private void EvaluateWaterProximity(int centerTile, HexTileData centerData, Vector3 cameraFocus)
    {
        // Check the tile directly under the camera first.
        TileWaterType bestWater = centerData.waterType;
        float bestDist = 0f;

        // If the center tile isn't water, scan immediate neighbours for the nearest water.
        if (bestWater == TileWaterType.None && _grid.neighbors != null && centerTile < _grid.neighbors.Length)
        {
            var nbrs = _grid.neighbors[centerTile];
            if (nbrs != null)
            {
                float closestSq = float.MaxValue;
                for (int i = 0; i < nbrs.Count; i++)
                {
                    int n = nbrs[i];
                    if (n < 0 || n >= _grid.TileCount) continue;
                    var nd = _tileSystem.GetTileData(n);
                    if (nd == null || nd.waterType == TileWaterType.None) continue;

                    Vector3 nc = _grid.tileCenters[n];
                    float dSq = (new Vector2(nc.x - cameraFocus.x, nc.z - cameraFocus.z)).sqrMagnitude;
                    if (dSq < closestSq)
                    {
                        closestSq = dSq;
                        bestWater = nd.waterType;
                        bestDist = Mathf.Sqrt(dSq);
                    }
                }
            }
        }

        // Resolve volume from database.
        if (bestWater == TileWaterType.None)
        {
            _waterTargetVolume = 0f;
            _currentWaterType = TileWaterType.None;
            return;
        }

        var we = database.GetWaterEntry(bestWater);
        if (we == null || we.clip == null)
        {
            _waterTargetVolume = 0f;
            _currentWaterType = bestWater;
            return;
        }

        // Distance attenuation.
        float attenuation = 1f;
        if (bestDist > 0f && we.audibleRadius > 0f)
            attenuation = Mathf.Clamp01(1f - bestDist / we.audibleRadius);

        _waterTargetVolume = we.maxVolume * attenuation;
        _waterBaseVolume = we.maxVolume;

        // Switch clip if water type changed.
        if (bestWater != _currentWaterType || _waterSource.clip != we.clip)
        {
            _currentWaterType = bestWater;
            _waterSource.clip = we.clip;
            if (!_waterSource.isPlaying && _waterTargetVolume > 0f)
                _waterSource.Play();
        }
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private AudioSource CreateLoopSource(string label)
    {
        var go = new GameObject(label);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.loop = true;
        src.playOnAwake = false;
        src.spatialBlend = 0f;   // 2D — ambient is non-positional
        src.volume = 0f;
        return src;
    }

    private void CacheRefs()
    {
        if (_tileSystem == null)
            _tileSystem = TileSystem.Instance;

        if (_grid == null && _tileSystem != null)
        {
            // Grab grid from the planet generator associated with the current planet.
            if (_planetGenerator == null)
            {
                var gm = GameManager.Instance;
                if (gm != null)
                    _planetGenerator = gm.GetCurrentPlanetGenerator();
            }
            if (_planetGenerator != null)
                _grid = _planetGenerator.Grid;
        }
    }

    /// <summary>
    /// Force a re-cache when the player switches planets.
    /// Call from GameManager or wherever planet switching happens.
    /// </summary>
    public void OnPlanetChanged()
    {
        _tileSystem = null;
        _grid = null;
        _planetGenerator = null;
        _currentBiome = (Biome)(-1);
        _currentWaterType = TileWaterType.None;
        CacheRefs();
    }
}
