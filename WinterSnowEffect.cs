using UnityEngine;

/// <summary>
/// Spawns a snow particle system that follows the camera during Winter.
/// Fades in/out smoothly when the season changes, and disables itself
/// when the camera is in orbit or underwater mode.
/// Attach to any persistent GameObject (e.g. the camera rig).
/// </summary>
public class WinterSnowEffect : MonoBehaviour
{
    public static WinterSnowEffect Instance { get; private set; }

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

    [Header("Fade")]
    [Tooltip("Seconds to fade the emission rate in/out when season changes.")]
    public float fadeDuration = 3f;

    [Header("References (auto-found if null)")]
    public PlanetaryCameraManager cameraManager;

    // Runtime
    private ParticleSystem _ps;
    private ParticleSystem.EmissionModule _emission;
    private ParticleSystem.ShapeModule _shape;
    private bool _shouldSnow = false;
    private float _fadeT = 0f; // 0 = off, 1 = full

    // ================================================================
    //  Lifecycle
    // ================================================================

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
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
        // --- decide target ---
        bool wantSnow = _shouldSnow && !IsOrbitOrUnderwater();
        float target = wantSnow ? 1f : 0f;

        // --- fade ---
        if (!Mathf.Approximately(_fadeT, target))
        {
            float speed = 1f / Mathf.Max(fadeDuration, 0.01f);
            _fadeT = Mathf.MoveTowards(_fadeT, target, speed * Time.deltaTime);
            _emission.rateOverTime = emissionRate * _fadeT;
        }

        // Turn system off entirely when fully faded so it doesn't tick
        if (_fadeT <= 0f && _ps.isPlaying && _ps.particleCount == 0)
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        else if (_fadeT > 0f && !_ps.isPlaying)
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

        _shouldSnow = season == Season.Winter;
    }

    /// <summary>
    /// Called once on enable to sync with the current season (e.g. after loading a save).
    /// </summary>
    private void EvaluateCurrentSeason()
    {
        if (ClimateManager.Instance == null) return;
        int pIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        Season s = ClimateManager.Instance.GetSeasonForPlanet(pIndex);
        _shouldSnow = s == Season.Winter;

        // If already winter, snap immediately
        if (_shouldSnow)
        {
            _fadeT = 1f;
            _emission.rateOverTime = emissionRate;
            _ps.Play();
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
        // Create child GO so we can control its transform independently
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
        vel.x = new ParticleSystem.MinMaxCurve(0f);
        vel.y = new ParticleSystem.MinMaxCurve(-minFallSpeed, -maxFallSpeed);
        vel.z = new ParticleSystem.MinMaxCurve(0f);

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
        EvaluateCurrentSeason();
    }
}
