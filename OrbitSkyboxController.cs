using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Drives a camera-height-based crossfade between the normal sky and a space skybox.
/// Attach to a GameObject with an HDRP Volume component set to Local mode.
/// The Volume should contain an HDRI Sky override with a starfield cubemap assigned.
///
/// As the camera zooms past orbitTransitionStart, the Volume weight lerps from 0→1,
/// gradually replacing the scene's default sky with the space sky.
/// Also fades out fog, adjusts exposure, and optionally dims directional light.
/// </summary>
[RequireComponent(typeof(Volume))]
public class OrbitSkyboxController : MonoBehaviour
{
    [Header("Camera Reference")]
    [Tooltip("Reference to the PlanetaryCameraManager. Auto-found if null.")]
    public PlanetaryCameraManager cameraManager;

    [Header("Transition Thresholds")]
    [Tooltip("Camera height at which the space transition begins (0% space).")]
    public float orbitTransitionStart = 140f;
    [Tooltip("Camera height at which transition is fully in space (100% space).")]
    public float orbitTransitionEnd = 200f;

    [Header("Visual Effects")]
    [Tooltip("Dim the main directional light as we enter orbit. 1 = full brightness on surface.")]
    public float surfaceLightIntensity = 1f;
    [Tooltip("Light intensity when fully in orbit view.")]
    public float orbitLightIntensity = 0.3f;
    [Tooltip("Optional directional light (sun) to dim during transition.")]
    public Light sunLight;

    [Header("Fog Fadeout")]
    [Tooltip("Enable fog fading as camera enters orbit.")]
    public bool fadeFog = true;

    [Header("Exposure")]
    [Tooltip("Surface exposure compensation (default scene value).")]
    public float surfaceExposure = 0f;
    [Tooltip("Exposure compensation when fully in orbit (darker = more space-like).")]
    public float orbitExposure = -1.5f;

    [Header("Stars Particle System")]
    [Tooltip("Optional particle system for distant stars. Enabled when entering orbit.")]
    public ParticleSystem starsParticleSystem;

    // Cached references
    private Volume _volume;
    private float _savedSunIntensity;
    private Fog _fog;
    private Exposure _exposure;
    private float _savedFogMaxDistance;
    private float _savedFogAttenDistance;
    private bool _hasFog;
    private bool _hasExposure;
    private bool _initialized;

    /// <summary>
    /// Current orbit transition blend (0 = surface, 1 = full orbit). Useful for other systems.
    /// </summary>
    public float OrbitBlend { get; private set; }

    void Start()
    {
        Initialize();
    }

    void Initialize()
    {
        if (_initialized) return;

        _volume = GetComponent<Volume>();
        _volume.weight = 0f;
        _volume.priority = 10; // Override scene default sky

        if (cameraManager == null)
            cameraManager = FindAnyObjectByType<PlanetaryCameraManager>();

        // Cache sun intensity
        if (sunLight != null)
            _savedSunIntensity = surfaceLightIntensity;

        // Try to find Fog override in the scene's default volume for fading
        // We look for a global volume with fog to fade it out
        _hasFog = false;
        _hasExposure = false;

        // Find the global/default volume with fog
        var volumes = FindObjectsByType<Volume>();
        foreach (var vol in volumes)
        {
            if (vol == _volume) continue; // skip our own
            if (vol.profile == null) continue;

            if (!_hasFog && vol.profile.TryGet(out Fog fog))
            {
                _fog = fog;
                _hasFog = true;
                _savedFogMaxDistance = fog.meanFreePath.value;
                _savedFogAttenDistance = fog.baseHeight.value;
            }

            if (!_hasExposure && vol.profile.TryGet(out Exposure exp))
            {
                _exposure = exp;
                _hasExposure = true;
            }
        }

        // Stars off by default
        if (starsParticleSystem != null)
        {
            var emission = starsParticleSystem.emission;
            emission.enabled = false;
        }

        _initialized = true;
    }

    void LateUpdate()
    {
        if (cameraManager == null) return;

        // Only apply the orbit transition when the camera is actually in orbit mode.
        // Without this guard, simply zooming out on the surface world dims the sun,
        // kills fog, and darkens exposure — breaking surface visuals.
        float t;
        if (cameraManager.IsInOrbitMode)
        {
            float camHeight = cameraManager.CameraHeight;
            t = Mathf.InverseLerp(orbitTransitionStart, orbitTransitionEnd, camHeight);
        }
        else
        {
            t = 0f;
        }
        OrbitBlend = t;

        // Drive the space sky volume weight
        _volume.weight = t;

        // Dim sun light
        if (sunLight != null)
        {
            sunLight.intensity = Mathf.Lerp(surfaceLightIntensity, orbitLightIntensity, t);
        }

        // Fade fog out
        if (fadeFog && _hasFog && _fog != null)
        {
            // Increase mean free path to effectively remove fog
            _fog.meanFreePath.value = Mathf.Lerp(_savedFogMaxDistance, _savedFogMaxDistance * 100f, t);
        }

        // Adjust exposure
        if (_hasExposure && _exposure != null)
        {
            _exposure.compensation.value = Mathf.Lerp(surfaceExposure, orbitExposure, t);
        }

        // Stars particle system
        if (starsParticleSystem != null)
        {
            var emission = starsParticleSystem.emission;
            emission.enabled = t > 0.1f;
        }
    }
}
