using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns a snow particle system that follows the camera during Winter.
/// Fades in/out smoothly when the season changes, and disables itself
/// when the camera is in orbit or underwater mode.
/// Snow is filtered per-planet (only snow-capable planets) and per-biome
/// (no snow in deserts/tropicals, full snow in tundra/glaciers, etc.).
/// Attach to any persistent GameObject (e.g. the camera rig).
/// </summary>
public class WinterSnowEffect : MonoBehaviour
{
    public static WinterSnowEffect Instance { get; private set; }

    [Header("Planet Filter")]
    [Tooltip("Only these planet types will produce snowfall. Others are always snow-free.")]
    public PlanetType[] snowPlanetTypes = new PlanetType[]
    {
        PlanetType.Earth,
        PlanetType.Europa,
        PlanetType.Titan,
        PlanetType.Pluto
    };

    [Header("Particle Settings")]
    [Tooltip("Maximum particles alive at once.")]
    public int maxParticles = 4000;
    [Tooltip("Particles emitted per second at full intensity.")]
    public float emissionRate = 800f;
    [Tooltip("Snow particle lifetime (seconds).")]
    public float lifetime = 6f;
    [Tooltip("Particle start size range.")]
    public float minSize = 0.05f;
    public float maxSize = 0.15f;
    [Tooltip("Particle fall speed range (m/s).")]
    public float minFallSpeed = 1.5f;
    public float maxFallSpeed = 3.5f;
    [Tooltip("Horizontal drift noise strength.")]
    public float driftStrength = 0.6f;
    [Tooltip("Spawn box half-extents (XZ spread around camera).")]
    public float spawnAreaHalfWidth = 60f;
    [Tooltip("Spawn height above the camera.")]
    public float spawnHeightAboveCamera = 25f;

    [Header("Biome Intensity")]
    [Tooltip("How fast the biome multiplier blends when crossing biome borders (units/sec).")]
    public float biomeFadeSpeed = 1.5f;

    [Header("Fade")]
    [Tooltip("Seconds to fade the emission rate in/out when season changes.")]
    public float fadeDuration = 3f;

    [Header("References (auto-found if null)")]
    public PlanetaryCameraManager cameraManager;
    [Tooltip("Optional: assign an existing ParticleSystem to use for snow. If null, the script will use any ParticleSystem on this GameObject or children, or create one.")]
    public ParticleSystem snowParticleSystem;

    // Runtime
    private ParticleSystem _ps;
    private ParticleSystem.EmissionModule _emission;
    private ParticleSystem.ShapeModule _shape;
    private bool _shouldSnow = false;
    private float _fadeT = 0f; // 0 = off, 1 = full

    // Planet allow-list (built from Inspector array for fast lookup)
    private HashSet<PlanetType> _snowPlanetSet;

    // Biome-aware intensity
    private float _biomeMultiplier = 1f;       // current smoothed value
    private float _biomeMultiplierTarget = 1f; // raw value from last tile lookup
    private TileSystem _tileSystem;
    private HexGrid _grid;
    private float _nextTileUpdate;
    private int _lastPlanetIndex = int.MinValue;
    // Debug logging throttle
    private float _lastSnowDebugLogTime = -999f;
    private float _lastLoggedEffectiveRate = -1f;

    // ================================================================
    //  Lifecycle
    // ================================================================

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        RebuildPlanetSet();
        BuildParticleSystem();
    }

    void OnEnable()
    {
        ClimateManager.OnPlanetSeasonChanged += OnPlanetSeasonChanged;

        // Evaluate once in case a save was loaded mid-winter
        EvaluateCurrentSeason();
    }

    void OnDisable()
    {
        ClimateManager.OnPlanetSeasonChanged -= OnPlanetSeasonChanged;
    }

    void LateUpdate()
    {
        int currentPlanetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        if (_lastPlanetIndex != currentPlanetIndex)
        {
            _lastPlanetIndex = currentPlanetIndex;
            OnPlanetChanged();
            _nextTileUpdate = 0f;
        }

        // --- biome tile lookup (throttled) ---
        if (_shouldSnow && Time.time >= _nextTileUpdate)
        {
            _nextTileUpdate = Time.time + 0.25f;
            UpdateBiomeMultiplier();
        }

        // Smoothly blend the biome multiplier so snow doesn't pop at borders
        _biomeMultiplier = Mathf.MoveTowards(_biomeMultiplier, _biomeMultiplierTarget,
            biomeFadeSpeed * Time.deltaTime);

        // --- decide target ---
        bool wantSnow = _shouldSnow && !IsOrbitOrUnderwater();
        float target = wantSnow ? 1f : 0f;

        // --- fade ---
        if (!Mathf.Approximately(_fadeT, target))
        {
            float speed = 1f / Mathf.Max(fadeDuration, 0.01f);
            _fadeT = Mathf.MoveTowards(_fadeT, target, speed * Time.deltaTime);
        }

        // Apply emission with both season fade and biome multiplier
        _emission.rateOverTime = new ParticleSystem.MinMaxCurve(emissionRate * _fadeT * _biomeMultiplier);

        // Turn system off entirely when fully faded so it doesn't tick
        float effectiveRate = _fadeT * _biomeMultiplier;
        // Throttled debug log: only print during winter (_shouldSnow true),
        // and then only when effectiveRate changes significantly or every 2s
        if (_shouldSnow)
        {
            if (Time.time - _lastSnowDebugLogTime > 2f || Mathf.Abs(effectiveRate - _lastLoggedEffectiveRate) > 0.01f)
            {
                _lastSnowDebugLogTime = Time.time;
                _lastLoggedEffectiveRate = effectiveRate;
                bool orbitOrUnder = IsOrbitOrUnderwater();
                Debug.Log($"[WinterSnowEffect] shouldSnow={_shouldSnow} fadeT={_fadeT:F3} biomeMul={_biomeMultiplier:F3} biomeTarget={_biomeMultiplierTarget:F3} effectiveRate={effectiveRate:F3} psPlaying={_ps != null && _ps.isPlaying} orbitOrUnder={orbitOrUnder}");
            }
        }

        if (effectiveRate <= 0f && _ps.isPlaying && _ps.particleCount == 0)
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        else if (effectiveRate > 0f && !_ps.isPlaying)
            _ps.Play();

        // --- follow camera ---
        FollowCamera();
    }

    // ================================================================
    //  Season hooks
    // ================================================================

    private void OnPlanetSeasonChanged(int planetIndex, Season season)
    {
        // Only react to the planet the player is currently viewing
        int current = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        if (planetIndex != current) return;

        _shouldSnow = season == Season.Winter && IsPlanetSnowCapable();
    }

    /// <summary>
    /// Called once on enable to sync with the current season (e.g. after loading a save).
    /// </summary>
    private void EvaluateCurrentSeason()
    {
        CacheGridRefs();

        if (ClimateManager.Instance == null) return;
        int pIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        _lastPlanetIndex = pIndex;
        Season s = ClimateManager.Instance.GetSeasonForPlanet(pIndex);
        _shouldSnow = s == Season.Winter && IsPlanetSnowCapable();

        // If already winter on a snow-capable planet, snap immediately
        if (_shouldSnow)
        {
            _fadeT = 1f;
            _biomeMultiplierTarget = 1f;
            _biomeMultiplier = 1f;
            _emission.rateOverTime = new ParticleSystem.MinMaxCurve(emissionRate);
            _ps.Play();

            // Do an immediate biome lookup so intensity is correct from frame 1
            UpdateBiomeMultiplier();
            _biomeMultiplier = _biomeMultiplierTarget; // snap, don't blend
            _emission.rateOverTime = new ParticleSystem.MinMaxCurve(emissionRate * _biomeMultiplier);
        }
    }

    // ================================================================
    //  Helpers
    // ================================================================

    private bool IsOrbitOrUnderwater()
    {
        if (cameraManager == null)
            cameraManager = FindFirstObjectByType<PlanetaryCameraManager>();
        if (cameraManager == null) return false;
        return cameraManager.IsInOrbitMode || cameraManager.IsInUnderwaterMode;
    }

    /// <summary>Returns true if the planet the player is currently looking at supports snowfall.</summary>
    private bool IsPlanetSnowCapable()
    {
        if (_snowPlanetSet == null) RebuildPlanetSet();
        var pg = GameManager.Instance != null ? GameManager.Instance.GetCurrentPlanetGenerator() : null;
        if (pg == null) return true; // fail-open so single-planet Earth prototypes still work
        return _snowPlanetSet.Contains(pg.planetType);
    }

    private void RebuildPlanetSet()
    {
        _snowPlanetSet = new HashSet<PlanetType>();
        if (snowPlanetTypes != null)
        {
            foreach (var pt in snowPlanetTypes)
                _snowPlanetSet.Add(pt);
        }
    }

    // ── Biome-aware intensity ──

    /// <summary>Cache HexGrid and TileSystem refs (called when planet changes).</summary>
    private void CacheGridRefs()
    {
        var pg = GameManager.Instance != null ? GameManager.Instance.GetCurrentPlanetGenerator() : null;
        _grid = pg != null ? pg.Grid : null;
        _tileSystem = TileSystem.Instance;
    }

    /// <summary>Reads the biome under the camera and sets _biomeMultiplierTarget.</summary>
    private void UpdateBiomeMultiplier()
    {
        if (_grid == null || _tileSystem == null || cameraManager == null)
        {
            CacheGridRefs();
            if (_grid == null || _tileSystem == null) return;
        }

        Vector3 focus = cameraManager.FocusPoint;
        int tileIdx = _grid.GetTileAtPosition(focus);
        if (tileIdx < 0) return;

        var td = _tileSystem.GetTileData(tileIdx);
        if (td == null) return;

        _biomeMultiplierTarget = GetBiomeSnowMultiplier(td.biome);
    }

    /// <summary>
    /// Returns 0-1 how strongly snow should fall over a given biome.
    /// 0 = no snow (desert, tropical, volcanic…), 1 = full blizzard (tundra, glacier…).
    /// </summary>
    public static float GetBiomeSnowMultiplier(Biome biome)
    {
        // Prefer authoritative data from ClimateManager (populated from BiomeVisualDatabase)
        if (ClimateManager.Instance != null)
        {
            var resp = ClimateManager.Instance.GetSeasonResponse(biome, Season.Winter);
            if (resp != null)
            {
                return Mathf.Clamp01(resp.snow);
            }
        }

        // Fallback: use the original hardcoded mapping if no runtime data is available
        switch (biome)
        {
            case Biome.Tundra:
            case Biome.Glacier:
            case Biome.Arctic:
            case Biome.IcicleField:
            case Biome.MartianPolarIce:
            case Biome.EuropaIce:
            case Biome.EuropaRidges:
            case Biome.PlutoCryo:
            case Biome.TitanIce:
                return 1f;

            case Biome.Temperate:
            case Biome.Plains:
            case Biome.Swamp:
                return 0.7f;

            case Biome.Coast:
            case Biome.Ocean:
            case Biome.Seas:
            case Biome.Lake:
            case Biome.River:
                return 0.4f;

            case Biome.Desert:
            case Biome.Savannah:
            case Biome.Tropical:
            case Biome.Volcanic:
            case Biome.Steamlands:
            case Biome.Ashlands:
            case Biome.Scorched:
            case Biome.Hellscape:
            case Biome.VenusLava:
            case Biome.VenusianPlains:
            case Biome.MartianRegolith:
            case Biome.MartianDunes:
            case Biome.MercuryPlains:
            case Biome.MercurianIce:
            case Biome.JovianClouds:
            case Biome.SaturnSurface:
            case Biome.UranusSurface:
            case Biome.NeptuneSurface:
            case Biome.TitanLakes:
            case Biome.TitanDunes:
            case Biome.MoonDunes:
            case Biome.AbyssalPlains:
            case Biome.Trench:
                return 0f;

            default:
                return 0.5f;
        }
    }

    private void FollowCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;

        // Position the emitter box above the camera
        Vector3 pos = cam.transform.position;
        pos.y += spawnHeightAboveCamera;
        _ps.transform.position = pos;
    }

    // ================================================================
    //  Particle system construction (code-only, no prefab needed)
    // ================================================================

    private void BuildParticleSystem()
    {
        // If an explicit ParticleSystem was assigned in the Inspector, use it.
        if (snowParticleSystem != null)
        {
            _ps = snowParticleSystem;
            _emission = _ps.emission;
            _shape = _ps.shape;
            _emission.rateOverTime = 0f;
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }

        // Otherwise prefer any existing ParticleSystem on this GameObject or its children
        var existing = GetComponentInChildren<ParticleSystem>(true);
        if (existing != null)
        {
            _ps = existing;
            _emission = _ps.emission;
            _shape = _ps.shape;
            _emission.rateOverTime = 0f;
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return;
        }

        // Fallback: create a dedicated child particle system (old behavior)
        var go = new GameObject("_WinterSnow");
        go.transform.SetParent(transform, false);

        _ps = go.AddComponent<ParticleSystem>();

        // --- Main module ---
        var main = _ps.main;
        main.maxParticles = maxParticles;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.8f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(minFallSpeed, maxFallSpeed);
        main.startSize = new ParticleSystem.MinMaxCurve(minSize, maxSize);
        main.startColor = new Color(0.95f, 0.95f, 1f, 0.85f);
        main.gravityModifier = 0f;           // we control speed via startSpeed pointing downward
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = false;
        main.loop = true;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        // --- Emission ---
        _emission = _ps.emission;
        _emission.rateOverTime = 0f;  // starts silent, faded in via LateUpdate

        // --- Shape  (box above camera) ---
        _shape = _ps.shape;
        _shape.shapeType = ParticleSystemShapeType.Box;
        _shape.scale = new Vector3(spawnAreaHalfWidth * 2f, 0.5f, spawnAreaHalfWidth * 2f);
        _shape.rotation = new Vector3(0f, 0f, 0f);
        // Particles emit downward
        _shape.randomDirectionAmount = 0f;

        // Override velocity to point downward instead of using shape direction
        var vel = _ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        // Ensure all axes use the same MinMaxCurve mode (two-constant mode) to avoid mixed-mode errors
        vel.x = new ParticleSystem.MinMaxCurve(0f, 0f);
        vel.y = new ParticleSystem.MinMaxCurve(-maxFallSpeed, -minFallSpeed);
        vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);

        // Reset startSpeed since velocity module handles movement
        main.startSpeed = 0f;

        // --- Noise (gentle drift) ---
        var noise = _ps.noise;
        noise.enabled = true;
        noise.strength = new ParticleSystem.MinMaxCurve(driftStrength);
        noise.frequency = 0.3f;
        noise.scrollSpeed = 0.1f;
        noise.damping = true;
        noise.octaveCount = 2;
        noise.separateAxes = true;
        noise.strengthX = new ParticleSystem.MinMaxCurve(driftStrength);
        noise.strengthY = new ParticleSystem.MinMaxCurve(driftStrength * 0.2f);  // less vertical noise
        noise.strengthZ = new ParticleSystem.MinMaxCurve(driftStrength);

        // --- Renderer ---
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        // Use the default particle material (white circle). You can assign a
        // custom snow-flake material here if you have one.
        renderer.material = GetDefaultParticleMaterial();
        renderer.sortingOrder = 10;  // draw above terrain

        // --- Color over lifetime (fade out near end) ---
        var col = _ps.colorOverLifetime;
        col.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] { new(Color.white, 0f), new(Color.white, 1f) },
            new GradientAlphaKey[] { new(0f, 0f), new(0.85f, 0.1f), new(0.85f, 0.7f), new(0f, 1f) }
        );
        col.color = grad;

        // --- Size over lifetime (slight shrink at end) ---
        var size = _ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.4f));

        _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private static Material GetDefaultParticleMaterial()
    {
        // Try loading the built-in particle material
        var mat = Resources.Load<Material>("Default-Particle");
        if (mat != null) return mat;

        // Fallback: create a simple unlit particle material
        var shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("HDRP/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Unlit/Color");

        var fallback = new Material(shader != null ? shader : Shader.Find("Hidden/InternalErrorShader"));
        fallback.color = new Color(1f, 1f, 1f, 0.8f);
        return fallback;
    }

    // ================================================================
    //  Public API
    // ================================================================

    /// <summary>
    /// Call when the player switches planets so we can re-evaluate the season.
    /// </summary>
    public void OnPlanetChanged()
    {
        CacheGridRefs();
        EvaluateCurrentSeason();
    }

    /// <summary>
    /// Call if the Inspector snowPlanetTypes array is changed at runtime.
    /// </summary>
    private void OnValidate()
    {
        RebuildPlanetSet();
    }
}
