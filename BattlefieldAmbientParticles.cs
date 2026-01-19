using UnityEngine;

/// <summary>
/// Creates ambient floating particles for the battlefield atmosphere.
/// Spawns biome-appropriate particles like dust motes, ash, snow, pollen, etc.
/// Attach to BattleMapGenerator or a dedicated Ambient parent object.
/// </summary>
public class BattlefieldAmbientParticles : MonoBehaviour
{
    [Header("Particle Settings")]
    [Tooltip("Number of particles in the system")]
    [Range(50, 10000)]
    public int particleCount = 1000;
    
    [Tooltip("Area size for particle spawning")]
    public float spawnAreaSize = 100f;
    
    [Tooltip("Height range for particles (min, max)")]
    public Vector2 heightRange = new Vector2(0.5f, 15f);
    
    [Tooltip("Base particle size")]
    [Range(0.01f, 0.5f)]
    public float particleSize = 0.05f;
    
    [Tooltip("Particle lifetime")]
    [Range(5f, 30f)]
    public float particleLifetime = 15f;
    
    [Header("Movement")]
    [Tooltip("How much particles drift with wind")]
    [Range(0f, 2f)]
    public float windInfluence = 0.5f;
    
    [Tooltip("Random drift speed")]
    [Range(0f, 1f)]
    public float driftSpeed = 0.2f;
    
    [Tooltip("Wind direction (synced with grass/clouds if available)")]
    public Vector3 windDirection = new Vector3(1f, 0f, 0.3f);
    
    [Header("Appearance")]
    [Tooltip("Primary particle color")]
    public Color particleColor = new Color(1f, 1f, 0.9f, 0.6f);
    
    [Tooltip("Whether particles glow/emit light")]
    public bool emissive = false;
    
    [Tooltip("Emission intensity if emissive")]
    [Range(0f, 5f)]
    public float emissionIntensity = 1f;
    
    [Header("Biome Adaptation")]
    [Tooltip("Automatically adjust particles based on biome")]
    public bool adaptToBiome = true;
    
    // Particle system reference (renamed to avoid hiding Component.particleSystem)
    private ParticleSystem ambientParticleSystem;
    private ParticleSystemRenderer ambientParticleRenderer;
    
    // Secondary particle system for special effects (embers, fireflies)
    private ParticleSystem secondaryParticleSystem;
    
    /// <summary>
    /// Create ambient particles for the battlefield
    /// </summary>
    public void CreateParticles(float mapSize, Biome biome = Biome.Plains)
    {
        // Clear any existing particles
        ClearParticles();
        
        // Adapt to biome
        if (adaptToBiome)
        {
            AdaptToBiome(biome);
        }
        
        // Update spawn area to match map
        spawnAreaSize = Mathf.Max(spawnAreaSize, mapSize);
        
        // Skip if particle count is 0 (some biomes have no particles)
        if (particleCount <= 0)
        {
            // No particles for this biome type
            return;
        }
        
        // Create main particle system
        CreateMainParticleSystem();
        
        // Create secondary effects if needed (embers, fireflies, etc.)
        CreateSecondaryParticles(biome);
        
        // Ambient particles created for biome
    }
    
    /// <summary>
    /// Create the main ambient particle system
    /// </summary>
    private void CreateMainParticleSystem()
    {
        // Create GameObject
        GameObject particleGO = new GameObject("AmbientParticles");
        particleGO.transform.SetParent(transform);
        particleGO.transform.localPosition = Vector3.zero;
        
        // Add ParticleSystem
        ambientParticleSystem = particleGO.AddComponent<ParticleSystem>();
        
        // Stop to configure
        ambientParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        
        // Main module
        var main = ambientParticleSystem.main;
        main.maxParticles = particleCount;
        main.startLifetime = particleLifetime;
        main.startSpeed = driftSpeed;
        main.startSize = particleSize;
        main.startColor = particleColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;
        main.loop = true;
        main.prewarm = true;
        
        // Emission module
        var emission = ambientParticleSystem.emission;
        emission.enabled = true;
        emission.rateOverTime = particleCount / particleLifetime;
        
        // Shape module - box covering the battlefield
        var shape = ambientParticleSystem.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(spawnAreaSize, heightRange.y - heightRange.x, spawnAreaSize);
        shape.position = new Vector3(0, (heightRange.x + heightRange.y) / 2f, 0);
        
        // Velocity over lifetime (wind drift)
        var velocityOverLifetime = ambientParticleSystem.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.space = ParticleSystemSimulationSpace.World;
        velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(windDirection.x * windInfluence * -1f, windDirection.x * windInfluence);
        velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f); // Gentle vertical bobbing
        velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(windDirection.z * windInfluence * -1f, windDirection.z * windInfluence);
        
        // Noise module for organic movement
        var noise = ambientParticleSystem.noise;
        noise.enabled = true;
        noise.strength = 0.3f;
        noise.frequency = 0.5f;
        noise.scrollSpeed = 0.1f;
        noise.damping = true;
        
        // Color over lifetime (fade in/out)
        var colorOverLifetime = ambientParticleSystem.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.9f), new GradientAlphaKey(0f, 1f) }
        );
        colorOverLifetime.color = gradient;
        
        // Size over lifetime (slight pulsing)
        var sizeOverLifetime = ambientParticleSystem.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 0.5f);
        sizeCurve.AddKey(0.5f, 1f);
        sizeCurve.AddKey(1f, 0.5f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
        
        // Renderer settings
        ambientParticleRenderer = particleGO.GetComponent<ParticleSystemRenderer>();
        ambientParticleRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        ambientParticleRenderer.sortMode = ParticleSystemSortMode.Distance;
        
        // Create and assign material
        Material particleMat = CreateParticleMaterial();
        if (particleMat == null)
        {
            Debug.LogError("[BattlefieldAmbientParticles] Failed to create particle material. Particles will not render.");
            return; // Cannot create particle system without material
        }
        ambientParticleRenderer.material = particleMat;
        
        // Start the system
        ambientParticleSystem.Play();
    }
    
    /// <summary>
    /// Create secondary particle effects for specific biomes
    /// </summary>
    private void CreateSecondaryParticles(Biome biome)
    {
        // Determine if we need secondary particles
        bool needsEmbers = biome == Biome.Volcanic || biome == Biome.Hellscape || 
                  biome == Biome.Ashlands ||
                          biome == Biome.IoVolcanic;
        
        bool needsFireflies = biome == Biome.Forest || biome == Biome.Swamp || 
                             biome == Biome.Marsh || biome == Biome.Jungle;
        
        bool needsSnowflakes = biome == Biome.Arctic || biome == Biome.Glacier ||
                      biome == Biome.IcicleField || biome == Biome.CryoForest ||
                      biome == Biome.Tundra;
        
        if (needsEmbers)
        {
            CreateEmberParticles();
        }
        else if (needsFireflies)
        {
            CreateFireflyParticles();
        }
        else if (needsSnowflakes)
        {
            CreateSnowParticles();
        }
    }
    
    /// <summary>
    /// Create glowing ember particles for volcanic biomes
    /// </summary>
    private void CreateEmberParticles()
    {
        GameObject emberGO = new GameObject("EmberParticles");
        emberGO.transform.SetParent(transform);
        emberGO.transform.localPosition = Vector3.zero;
        
        secondaryParticleSystem = emberGO.AddComponent<ParticleSystem>();
        secondaryParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        
        var main = secondaryParticleSystem.main;
        main.maxParticles = 200;
        main.startLifetime = 8f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.5f, 0.1f, 1f),  // Orange
            new Color(1f, 0.2f, 0f, 1f)      // Red-orange
        );
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;
        main.loop = true;
        main.prewarm = true;
        
        var emission = secondaryParticleSystem.emission;
        emission.rateOverTime = 25f;
        
        var shape = secondaryParticleSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(spawnAreaSize * 0.8f, 2f, spawnAreaSize * 0.8f);
        shape.position = new Vector3(0, 1f, 0);
        
        // Embers rise upward
        var velocityOverLifetime = secondaryParticleSystem.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(0.5f, 2f);
        velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
        velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(-0.5f, 0.5f);
        
        // Noise for flickering movement
        var noise = secondaryParticleSystem.noise;
        noise.enabled = true;
        noise.strength = 0.5f;
        noise.frequency = 1f;
        
        // Color fades from bright to dark
        var colorOverLifetime = secondaryParticleSystem.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient emberGradient = new Gradient();
        emberGradient.SetKeys(
            new GradientColorKey[] { 
                new GradientColorKey(new Color(1f, 0.8f, 0.3f), 0f),
                new GradientColorKey(new Color(1f, 0.3f, 0.1f), 0.5f),
                new GradientColorKey(new Color(0.3f, 0.1f, 0.05f), 1f)
            },
            new GradientAlphaKey[] { 
                new GradientAlphaKey(1f, 0f), 
                new GradientAlphaKey(0.8f, 0.7f), 
                new GradientAlphaKey(0f, 1f) 
            }
        );
        colorOverLifetime.color = emberGradient;
        
        // Renderer with additive blending for glow
        var renderer = emberGO.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        
        Material emberMat = CreateEmissiveParticleMaterial(new Color(1f, 0.5f, 0.1f), 2f);
        if (emberMat == null)
        {
            Debug.LogError("[BattlefieldAmbientParticles] Failed to create ember material. Embers will not render.");
            return; // Cannot create particle system without material
        }
        renderer.material = emberMat;
        
        secondaryParticleSystem.Play();
    }
    
    /// <summary>
    /// Create glowing firefly particles for forest biomes
    /// </summary>
    private void CreateFireflyParticles()
    {
        GameObject fireflyGO = new GameObject("FireflyParticles");
        fireflyGO.transform.SetParent(transform);
        fireflyGO.transform.localPosition = Vector3.zero;
        
        secondaryParticleSystem = fireflyGO.AddComponent<ParticleSystem>();
        secondaryParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        
        var main = secondaryParticleSystem.main;
        main.maxParticles = 100;
        main.startLifetime = 10f;
        main.startSpeed = 0.1f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
        main.startColor = new Color(0.8f, 1f, 0.4f, 1f); // Yellow-green glow
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;
        main.loop = true;
        
        var emission = secondaryParticleSystem.emission;
        emission.rateOverTime = 10f;
        
        var shape = secondaryParticleSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(spawnAreaSize * 0.6f, 5f, spawnAreaSize * 0.6f);
        shape.position = new Vector3(0, 3f, 0);
        
        // Slow random movement
        var velocityOverLifetime = secondaryParticleSystem.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
        velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
        
        // Noise for erratic firefly movement
        var noise = secondaryParticleSystem.noise;
        noise.enabled = true;
        noise.strength = 0.8f;
        noise.frequency = 0.8f;
        noise.scrollSpeed = 0.5f;
        
        // Blinking effect via color
        var colorOverLifetime = secondaryParticleSystem.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient blinkGradient = new Gradient();
        blinkGradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(new Color(0.8f, 1f, 0.4f), 0f), new GradientColorKey(new Color(0.8f, 1f, 0.4f), 1f) },
            new GradientAlphaKey[] { 
                new GradientAlphaKey(0f, 0f), 
                new GradientAlphaKey(1f, 0.1f),
                new GradientAlphaKey(0f, 0.2f),
                new GradientAlphaKey(1f, 0.4f),
                new GradientAlphaKey(0f, 0.5f),
                new GradientAlphaKey(1f, 0.7f),
                new GradientAlphaKey(0f, 0.8f),
                new GradientAlphaKey(1f, 0.9f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLifetime.color = blinkGradient;
        
        var renderer = fireflyGO.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        
        Material fireflyMat = CreateEmissiveParticleMaterial(new Color(0.8f, 1f, 0.4f), 3f);
        if (fireflyMat == null)
        {
            Debug.LogError("[BattlefieldAmbientParticles] Failed to create firefly material. Fireflies will not render.");
            return; // Cannot create particle system without material
        }
        renderer.material = fireflyMat;
        
        secondaryParticleSystem.Play();
    }
    
    /// <summary>
    /// Create falling snow particles
    /// </summary>
    private void CreateSnowParticles()
    {
        GameObject snowGO = new GameObject("SnowParticles");
        snowGO.transform.SetParent(transform);
        snowGO.transform.localPosition = Vector3.zero;
        
        secondaryParticleSystem = snowGO.AddComponent<ParticleSystem>();
        secondaryParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        
        var main = secondaryParticleSystem.main;
        main.maxParticles = 500;
        main.startLifetime = 12f;
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 1.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.1f);
        main.startColor = new Color(1f, 1f, 1f, 0.9f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;
        main.loop = true;
        main.prewarm = true;
        main.gravityModifier = 0.05f; // Gentle fall
        
        var emission = secondaryParticleSystem.emission;
        emission.rateOverTime = 50f;
        
        var shape = secondaryParticleSystem.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(spawnAreaSize, 2f, spawnAreaSize);
        shape.position = new Vector3(0, 30f, 0); // Spawn from above
        
        // Drift with wind
        var velocityOverLifetime = secondaryParticleSystem.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(windDirection.x * 0.5f - 0.3f, windDirection.x * 0.5f + 0.3f);
        velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(windDirection.z * 0.5f - 0.3f, windDirection.z * 0.5f + 0.3f);
        
        // Swirling motion
        var noise = secondaryParticleSystem.noise;
        noise.enabled = true;
        noise.strength = 0.4f;
        noise.frequency = 0.3f;
        
        // Rotation for tumbling effect
        var rotationOverLifetime = secondaryParticleSystem.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(-180f, 180f);
        
        var renderer = snowGO.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        Material snowMat = CreateParticleMaterial();
        if (snowMat == null)
        {
            Debug.LogError("[BattlefieldAmbientParticles] Failed to create snow material. Snow will not render.");
            return; // Cannot create particle system without material
        }
        renderer.material = snowMat;
        
        secondaryParticleSystem.Play();
    }
    
    /// <summary>
    /// Create standard particle material
    /// </summary>
    private Material CreateParticleMaterial()
    {
        // Try to find appropriate shader
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError("[BattlefieldAmbientParticles] No shader found! Particles will not render. Make sure URP or Standard shaders are available.");
            return null; // Cannot create material without shader
        }
        
        Material mat = new Material(shader);
        
        // Create a simple soft circle texture
        mat.mainTexture = CreateSoftCircleTexture();
        mat.SetColor("_BaseColor", particleColor);
        mat.SetColor("_Color", particleColor);
        
        // Transparency
        mat.SetFloat("_Surface", 1);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.renderQueue = 3000;
        
        return mat;
    }
    
    /// <summary>
    /// Create emissive particle material for glowing effects
    /// </summary>
    private Material CreateEmissiveParticleMaterial(Color emissionColor, float intensity)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null) shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            Debug.LogError("[BattlefieldAmbientParticles] No shader found for emissive particles! Particles will not render.");
            return null; // Cannot create material without shader
        }
        
        Material mat = new Material(shader);
        mat.mainTexture = CreateSoftCircleTexture();
        
        Color brightColor = emissionColor * intensity;
        mat.SetColor("_BaseColor", brightColor);
        mat.SetColor("_Color", brightColor);
        
        // Additive blending for glow effect
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One); // Additive
        mat.renderQueue = 3100;
        
        return mat;
    }
    
    /// <summary>
    /// Create a soft circle texture for particles
    /// </summary>
    private Texture2D CreateSoftCircleTexture()
    {
        int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        
        float center = size / 2f;
        float radius = size / 2f;
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = 1f - Mathf.Clamp01(dist / radius);
                alpha = alpha * alpha; // Softer falloff
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        
        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        
        return texture;
    }
    
    /// <summary>
    /// Adapt particle settings to biome
    /// </summary>
    private void AdaptToBiome(Biome biome)
    {
        switch (biome)
        {
            // Forest biomes - pollen/spores
                case Biome.Forest:
                case Biome.Taiga:
                particleCount = 300;
                particleColor = new Color(1f, 1f, 0.8f, 0.4f);
                particleSize = 0.03f;
                driftSpeed = 0.1f;
                break;
                
            // Jungle - dense spores
            case Biome.Jungle:
            case Biome.Rainforest:
                particleCount = 500;
                particleColor = new Color(0.9f, 1f, 0.7f, 0.5f);
                particleSize = 0.04f;
                driftSpeed = 0.15f;
                break;
                
            // Swamp/marsh - mist particles
            case Biome.Swamp:
            case Biome.Marsh:
            case Biome.Floodlands:
                particleCount = 400;
                particleColor = new Color(0.8f, 0.9f, 0.8f, 0.3f);
                particleSize = 0.1f;
                heightRange = new Vector2(0.2f, 4f);
                driftSpeed = 0.05f;
                break;
                
            // Desert - dust
                case Biome.Desert:
                particleCount = 200;
                particleColor = new Color(0.9f, 0.85f, 0.7f, 0.35f);
                particleSize = 0.04f;
                windInfluence = 1f;
                driftSpeed = 0.3f;
                break;
                
            // Volcanic - ash
            case Biome.Volcanic:
            case Biome.Ashlands:
            case Biome.CharredForest:
            case Biome.Scorched:
                particleCount = 400;
                particleColor = new Color(0.3f, 0.3f, 0.3f, 0.5f);
                particleSize = 0.05f;
                windInfluence = 0.8f;
                driftSpeed = 0.2f;
                break;
                
            // Hellscape - thick ash + embers handled separately
            case Biome.Hellscape:
                particleCount = 500;
                particleColor = new Color(0.2f, 0.15f, 0.1f, 0.6f);
                particleSize = 0.06f;
                windInfluence = 0.5f;
                break;
                
            // Plains/grassland - light dust
            case Biome.Plains:
            case Biome.Grassland:
            case Biome.Savannah:
                particleCount = 150;
                particleColor = new Color(1f, 1f, 0.9f, 0.25f);
                particleSize = 0.03f;
                driftSpeed = 0.15f;
                break;
                
            // Snow/ice - handled by secondary snow system
                case Biome.Arctic:
                case Biome.Glacier:
                case Biome.Tundra:
                case Biome.IcicleField:
                case Biome.CryoForest:
                particleCount = 100; // Just some ice crystals
                particleColor = new Color(0.9f, 0.95f, 1f, 0.3f);
                particleSize = 0.02f;
                break;
                
            // Steam
            case Biome.Steam:
                particleCount = 300;
                particleColor = new Color(1f, 1f, 1f, 0.4f);
                particleSize = 0.15f;
                heightRange = new Vector2(0.5f, 8f);
                driftSpeed = 0.3f;
                break;
                
            // Mars - red dust
            case Biome.MartianRegolith:
            case Biome.MartianCanyon:
            case Biome.MartianDunes:
                particleCount = 300;
                particleColor = new Color(0.8f, 0.5f, 0.3f, 0.4f);
                particleSize = 0.04f;
                windInfluence = 1.2f;
                break;
                
            // Venus - thick haze
            case Biome.VenusLava:
            case Biome.VenusianPlains:
            case Biome.VenusHighlands:
                particleCount = 600;
                particleColor = new Color(0.9f, 0.8f, 0.5f, 0.5f);
                particleSize = 0.08f;
                heightRange = new Vector2(1f, 20f);
                break;
                
            // Gas giants - swirling particles
            case Biome.JovianClouds:
            case Biome.JovianStorm:
            case Biome.SaturnSurface:
                particleCount = 400;
                particleColor = new Color(0.9f, 0.85f, 0.7f, 0.4f);
                windInfluence = 2f;
                driftSpeed = 0.5f;
                break;
                
            // Ice giants
            case Biome.UranusIce:
            case Biome.NeptuneWinds:
            case Biome.NeptuneIce:
                particleCount = 300;
                particleColor = new Color(0.7f, 0.85f, 1f, 0.4f);
                windInfluence = 2.5f;
                break;
                
            // Airless moons - no particles
                case Biome.MoonDunes:
                case Biome.MoonCraters:
                case Biome.MercuryPlains:
            case Biome.MercuryBasalt:
            case Biome.EuropaIce:
            case Biome.IoVolcanic:
            case Biome.PlutoCryo:
                particleCount = 0;
                break;
                
            // Titan - orange haze
            case Biome.TitanLakes:
            case Biome.TitanDunes:
            case Biome.TitanIce:
                particleCount = 400;
                particleColor = new Color(0.9f, 0.6f, 0.3f, 0.4f);
                particleSize = 0.06f;
                break;
                
            // Io - sulfur particles
            case Biome.IoSulfur:
                particleCount = 200;
                particleColor = new Color(0.9f, 0.9f, 0.3f, 0.4f);
                break;
                
            default:
                particleCount = 200;
                particleColor = new Color(1f, 1f, 1f, 0.3f);
                break;
        }
    }
    
    /// <summary>
    /// Set wind direction (sync with grass/clouds)
    /// </summary>
    public void SetWindDirection(Vector3 wind)
    {
        windDirection = wind;
    }
    
    /// <summary>
    /// Clear all particle systems
    /// </summary>
    public void ClearParticles()
    {
        if (ambientParticleSystem != null)
        {
            if (Application.isPlaying)
                Destroy(ambientParticleSystem.gameObject);
            else
                DestroyImmediate(ambientParticleSystem.gameObject);
            ambientParticleSystem = null;
        }
        
        if (secondaryParticleSystem != null)
        {
            if (Application.isPlaying)
                Destroy(secondaryParticleSystem.gameObject);
            else
                DestroyImmediate(secondaryParticleSystem.gameObject);
            secondaryParticleSystem = null;
        }
    }
    
    void OnDestroy()
    {
        ClearParticles();
    }
}

