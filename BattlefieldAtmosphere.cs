using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Creates HDRP-like atmospheric effects in URP.
/// Includes volumetric-style fog, light shafts (god rays), height fog, and atmospheric scattering.
/// Attach to BattleMapGenerator or a dedicated Atmosphere parent object.
/// </summary>
public class BattlefieldAtmosphere : MonoBehaviour
{
    [Header("Height Fog")]
    [Tooltip("Enable height-based fog (thicker near ground)")]
    public bool enableHeightFog = true;
    
    [Tooltip("Fog density at ground level")]
    [Range(0f, 1f)]
    public float groundFogDensity = 0.3f;
    
    [Tooltip("Height at which fog fades out")]
    [Range(1f, 50f)]
    public float fogFadeHeight = 10f;
    
    [Tooltip("Ground fog color")]
    public Color groundFogColor = new Color(0.8f, 0.85f, 0.9f, 1f);
    
    [Header("Light Shafts (God Rays)")]
    [Tooltip("Enable light shaft effect")]
    public bool enableLightShafts = true;
    
    [Tooltip("Number of light shaft planes")]
    [Range(1, 8)]
    public int lightShaftCount = 4;
    
    [Tooltip("Light shaft intensity")]
    [Range(0f, 1f)]
    public float lightShaftIntensity = 0.3f;
    
    [Tooltip("Light shaft color (usually warm sun color)")]
    public Color lightShaftColor = new Color(1f, 0.95f, 0.8f, 0.3f);
    
    [Tooltip("Light shaft length")]
    [Range(10f, 100f)]
    public float lightShaftLength = 50f;
    
    [Header("Atmospheric Scattering")]
    [Tooltip("Enable atmospheric scattering (blue haze in distance)")]
    public bool enableAtmosphericScattering = true;
    
    [Tooltip("Scattering color (usually blue for Earth atmosphere)")]
    public Color scatteringColor = new Color(0.6f, 0.75f, 1f, 1f);
    
    [Tooltip("Scattering intensity")]
    [Range(0f, 1f)]
    public float scatteringIntensity = 0.2f;
    
    [Tooltip("Distance at which scattering starts")]
    [Range(10f, 200f)]
    public float scatteringStartDistance = 50f;
    
    [Header("Dust Motes in Light")]
    [Tooltip("Enable dust particles visible in light shafts")]
    public bool enableDustInLight = true;
    
    [Tooltip("Dust particle count")]
    [Range(50, 500)]
    public int dustParticleCount = 200;
    
    [Header("Heat Distortion (Hot Biomes)")]
    [Tooltip("Enable heat shimmer effect for hot biomes")]
    public bool enableHeatDistortion = false;
    
    [Tooltip("Heat distortion intensity")]
    [Range(0f, 1f)]
    public float heatDistortionIntensity = 0.3f;
    
    [Header("Biome Adaptation")]
    [Tooltip("Automatically adjust atmosphere based on biome")]
    public bool adaptToBiome = true;
    
    // Internal references
    private GameObject heightFogPlane;
    private GameObject[] lightShaftPlanes;
    private ParticleSystem dustParticles;
    private Material heightFogMaterial;
    private Material[] lightShaftMaterials;
    private Light mainLight;
    private float mapSize;
    
    /// <summary>
    /// Create atmospheric effects for the battlefield
    /// </summary>
    public void CreateAtmosphere(float mapSize, Biome biome = Biome.Plains)
    {
        this.mapSize = mapSize;
        
        // Clear existing effects
        ClearAtmosphere();
        
        // Find main directional light
        mainLight = FindMainLight();
        
        // Adapt settings to biome
        if (adaptToBiome)
        {
            AdaptToBiome(biome);
        }
        
        // Create height fog
        if (enableHeightFog)
        {
            CreateHeightFog();
        }
        
        // Create light shafts
        if (enableLightShafts && mainLight != null)
        {
            CreateLightShafts();
        }
        
        // Create dust particles in light
        if (enableDustInLight && mainLight != null)
        {
            CreateDustInLight();
        }
}
    
    /// <summary>
    /// Find the main directional light in the scene
    /// </summary>
    private Light FindMainLight()
    {
        // Try to find sun/directional light
        Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        foreach (var light in lights)
        {
            if (light.type == LightType.Directional)
            {
                return light;
            }
        }
        return null;
    }
    
    /// <summary>
    /// Create height-based fog plane
    /// </summary>
    private void CreateHeightFog()
    {
        heightFogPlane = new GameObject("HeightFog");
        heightFogPlane.transform.SetParent(transform);
        heightFogPlane.transform.position = new Vector3(0, fogFadeHeight * 0.3f, 0);
        heightFogPlane.transform.rotation = Quaternion.Euler(90, 0, 0);
        
        // Create mesh
        MeshFilter meshFilter = heightFogPlane.AddComponent<MeshFilter>();
        meshFilter.mesh = CreateFogMesh(mapSize * 1.5f);
        
        MeshRenderer renderer = heightFogPlane.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        
        // Create fog material
        heightFogMaterial = CreateHeightFogMaterial();
        renderer.material = heightFogMaterial;
    }
    
    /// <summary>
    /// Create the height fog material with gradient falloff
    /// </summary>
    private Material CreateHeightFogMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        
        Material mat = new Material(shader);
        
        // Create gradient fog texture (dense at bottom, fading up)
        Texture2D fogTex = CreateFogGradientTexture();
        mat.mainTexture = fogTex;
        
        Color fogColorWithAlpha = groundFogColor;
        fogColorWithAlpha.a = groundFogDensity;
        mat.SetColor("_BaseColor", fogColorWithAlpha);
        mat.SetColor("_Color", fogColorWithAlpha);
        
        // Transparency settings
        mat.SetFloat("_Surface", 1);
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        
        return mat;
    }
    
    /// <summary>
    /// Create a gradient texture for height fog
    /// </summary>
    private Texture2D CreateFogGradientTexture()
    {
        int size = 64;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                // Radial gradient from center
                float distFromCenter = Vector2.Distance(new Vector2(x, y), new Vector2(size / 2f, size / 2f)) / (size / 2f);
                float alpha = 1f - Mathf.Clamp01(distFromCenter);
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
    /// Create fog mesh (large quad)
    /// </summary>
    private Mesh CreateFogMesh(float size)
    {
        Mesh mesh = new Mesh();
        mesh.name = "FogMesh";
        
        float halfSize = size / 2f;
        
        mesh.vertices = new Vector3[]
        {
            new Vector3(-halfSize, -halfSize, 0),
            new Vector3(halfSize, -halfSize, 0),
            new Vector3(-halfSize, halfSize, 0),
            new Vector3(halfSize, halfSize, 0)
        };
        
        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };
        
        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateNormals();
        
        return mesh;
    }
    
    /// <summary>
    /// Create light shaft (god ray) planes
    /// </summary>
    private void CreateLightShafts()
    {
        if (mainLight == null) return;
        
        lightShaftPlanes = new GameObject[lightShaftCount];
        lightShaftMaterials = new Material[lightShaftCount];
        
        Vector3 lightDir = mainLight.transform.forward;
        
        for (int i = 0; i < lightShaftCount; i++)
        {
            GameObject shaft = new GameObject($"LightShaft_{i}");
            shaft.transform.SetParent(transform);
            
            // Position shafts at different distances
            float distance = (i + 1) * (mapSize * 0.3f / lightShaftCount);
            Vector3 offset = new Vector3(
                Random.Range(-mapSize * 0.3f, mapSize * 0.3f),
                Random.Range(10f, 30f),
                Random.Range(-mapSize * 0.3f, mapSize * 0.3f)
            );
            shaft.transform.position = offset;
            
            // Orient towards light
            shaft.transform.rotation = Quaternion.LookRotation(-lightDir);
            
            // Create mesh
            MeshFilter meshFilter = shaft.AddComponent<MeshFilter>();
            meshFilter.mesh = CreateLightShaftMesh(lightShaftLength, 5f + i * 2f);
            
            MeshRenderer renderer = shaft.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            
            // Create material with varying intensity
            float intensityVariation = lightShaftIntensity * (0.7f + Random.Range(0f, 0.6f));
            Material mat = CreateLightShaftMaterial(intensityVariation);
            renderer.material = mat;
            
            lightShaftPlanes[i] = shaft;
            lightShaftMaterials[i] = mat;
        }
    }
    
    /// <summary>
    /// Create light shaft mesh (elongated cone/beam)
    /// </summary>
    private Mesh CreateLightShaftMesh(float length, float width)
    {
        Mesh mesh = new Mesh();
        mesh.name = "LightShaftMesh";
        
        // Create a quad that will be the light shaft
        float halfWidth = width / 2f;
        
        mesh.vertices = new Vector3[]
        {
            new Vector3(-halfWidth, 0, 0),
            new Vector3(halfWidth, 0, 0),
            new Vector3(-halfWidth * 0.3f, 0, length),
            new Vector3(halfWidth * 0.3f, 0, length)
        };
        
        mesh.uv = new Vector2[]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0.35f, 1),
            new Vector2(0.65f, 1)
        };
        
        mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }
    
    /// <summary>
    /// Create light shaft material
    /// </summary>
    private Material CreateLightShaftMaterial(float intensity)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        
        Material mat = new Material(shader);
        
        // Create gradient texture for light shaft
        Texture2D shaftTex = CreateLightShaftTexture();
        mat.mainTexture = shaftTex;
        
        Color shaftColorWithAlpha = lightShaftColor;
        shaftColorWithAlpha.a = intensity;
        mat.SetColor("_BaseColor", shaftColorWithAlpha);
        mat.SetColor("_Color", shaftColorWithAlpha);
        
        // Additive blending for glow effect
        mat.SetFloat("_Surface", 1);
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.One); // Additive
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3100;
        
        return mat;
    }
    
    /// <summary>
    /// Create light shaft gradient texture
    /// </summary>
    private Texture2D CreateLightShaftTexture()
    {
        int width = 32;
        int height = 64;
        Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Fade from bottom to top
                float verticalFade = 1f - (y / (float)height);
                verticalFade = verticalFade * verticalFade; // Steeper falloff
                
                // Fade from center horizontally
                float horizontalFade = 1f - Mathf.Abs((x - width / 2f) / (width / 2f));
                horizontalFade = Mathf.Pow(horizontalFade, 0.5f); // Softer horizontal falloff
                
                float alpha = verticalFade * horizontalFade;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        
        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        
        return texture;
    }
    
    /// <summary>
    /// Create dust particles that are visible in light shafts
    /// </summary>
    private void CreateDustInLight()
    {
        GameObject dustGO = new GameObject("DustInLight");
        dustGO.transform.SetParent(transform);
        dustGO.transform.localPosition = Vector3.zero;
        
        dustParticles = dustGO.AddComponent<ParticleSystem>();
        dustParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        
        var main = dustParticles.main;
        main.maxParticles = dustParticleCount;
        main.startLifetime = 8f;
        main.startSpeed = 0.05f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.06f);
        main.startColor = new Color(1f, 1f, 0.9f, 0.8f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.playOnAwake = true;
        main.loop = true;
        main.prewarm = true;
        
        var emission = dustParticles.emission;
        emission.rateOverTime = dustParticleCount / 8f;
        
        // Spawn in light shaft areas
        var shape = dustParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(mapSize * 0.5f, 20f, mapSize * 0.5f);
        shape.position = new Vector3(0, 15f, 0);
        
        // Slow drifting movement
        var velocityOverLifetime = dustParticles.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
        velocityOverLifetime.z = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f);
        
        // Noise for organic floating
        var noise = dustParticles.noise;
        noise.enabled = true;
        noise.strength = 0.2f;
        noise.frequency = 0.3f;
        
        // Fade in/out
        var colorOverLifetime = dustParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) }
        );
        colorOverLifetime.color = gradient;
        
        // Renderer with additive blending (glows in light)
        var renderer = dustGO.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        
        Material dustMat = CreateDustMaterial();
        renderer.material = dustMat;
        
        dustParticles.Play();
    }
    
    /// <summary>
    /// Create dust particle material
    /// </summary>
    private Material CreateDustMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null) shader = Shader.Find("Particles/Standard Unlit");
        
        Material mat = new Material(shader);
        mat.mainTexture = CreateSoftDotTexture();
        
        Color dustColor = lightShaftColor;
        dustColor.a = 0.6f;
        mat.SetColor("_BaseColor", dustColor);
        mat.SetColor("_Color", dustColor);
        
        // Soft additive for catching light
        mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)BlendMode.One);
        mat.renderQueue = 3050;
        
        return mat;
    }
    
    /// <summary>
    /// Create soft dot texture for dust
    /// </summary>
    private Texture2D CreateSoftDotTexture()
    {
        int size = 32;
        Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        
        float center = size / 2f;
        
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center)) / center;
                float alpha = 1f - Mathf.Clamp01(dist);
                alpha = alpha * alpha * alpha; // Very soft falloff
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        
        texture.Apply();
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        
        return texture;
    }
    
    /// <summary>
    /// Adapt atmosphere settings to biome
    /// </summary>
    private void AdaptToBiome(Biome biome)
    {
        switch (biome)
        {
            // Lush biomes - misty, green-tinted
            case Biome.Forest:
            case Biome.Jungle:
            case Biome.Rainforest:
                enableHeightFog = true;
                groundFogDensity = 0.4f;
                groundFogColor = new Color(0.7f, 0.8f, 0.7f, 1f);
                enableLightShafts = true;
                lightShaftIntensity = 0.4f;
                lightShaftColor = new Color(0.9f, 1f, 0.7f, 0.4f); // Green-tinted
                enableDustInLight = true;
                dustParticleCount = 300;
                break;
                
            // Swamp - heavy fog
            case Biome.Swamp:
            case Biome.Marsh:
            case Biome.Floodlands:
                enableHeightFog = true;
                groundFogDensity = 0.6f;
                fogFadeHeight = 6f;
                groundFogColor = new Color(0.6f, 0.7f, 0.6f, 1f);
                enableLightShafts = false; // Too foggy for light shafts
                enableDustInLight = false;
                break;
                
            // Plains - light haze
            case Biome.Plains:
            case Biome.Grassland:
            case Biome.Savannah:
                enableHeightFog = true;
                groundFogDensity = 0.15f;
                groundFogColor = new Color(0.85f, 0.9f, 0.95f, 1f);
                enableLightShafts = true;
                lightShaftIntensity = 0.25f;
                lightShaftColor = new Color(1f, 0.95f, 0.85f, 0.3f);
                enableDustInLight = true;
                dustParticleCount = 150;
                break;
                
            // Desert - heat haze, golden light
            case Biome.Desert:
                enableHeightFog = false;
                enableHeatDistortion = true;
                heatDistortionIntensity = 0.4f;
                enableLightShafts = true;
                lightShaftIntensity = 0.35f;
                lightShaftColor = new Color(1f, 0.9f, 0.6f, 0.35f); // Golden
                enableDustInLight = true;
                dustParticleCount = 250;
                break;
                
            // Arctic - cold blue fog
            case Biome.Arctic:
            case Biome.Glacier:
            case Biome.Tundra:
            case Biome.IcicleField:
            case Biome.CryoForest:
                enableHeightFog = true;
                groundFogDensity = 0.25f;
                groundFogColor = new Color(0.85f, 0.9f, 1f, 1f); // Cold blue
                enableLightShafts = true;
                lightShaftIntensity = 0.2f;
                lightShaftColor = new Color(0.9f, 0.95f, 1f, 0.25f);
                enableDustInLight = false; // Too cold for dust
                break;
                
            // Volcanic - smoky, orange glow
            case Biome.Volcanic:
            case Biome.Hellscape:
            case Biome.Ashlands:
                enableHeightFog = true;
                groundFogDensity = 0.5f;
                fogFadeHeight = 15f;
                groundFogColor = new Color(0.3f, 0.2f, 0.15f, 1f); // Smoky
                enableLightShafts = true;
                lightShaftIntensity = 0.5f;
                lightShaftColor = new Color(1f, 0.5f, 0.2f, 0.5f); // Orange/red
                enableDustInLight = true;
                dustParticleCount = 100;
                break;
                
            // Taiga - misty morning
            case Biome.Taiga:
                enableHeightFog = true;
                groundFogDensity = 0.35f;
                groundFogColor = new Color(0.75f, 0.8f, 0.85f, 1f);
                enableLightShafts = true;
                lightShaftIntensity = 0.35f;
                lightShaftColor = new Color(1f, 0.95f, 0.8f, 0.35f);
                enableDustInLight = true;
                dustParticleCount = 200;
                break;
                
            // Mars - dusty red atmosphere
            case Biome.MartianRegolith:
            case Biome.MartianCanyon:
            case Biome.MartianDunes:
                enableHeightFog = true;
                groundFogDensity = 0.2f;
                groundFogColor = new Color(0.8f, 0.5f, 0.3f, 1f);
                enableLightShafts = false; // Thin atmosphere
                enableDustInLight = true;
                dustParticleCount = 300;
                break;
                
            // Venus - thick yellow haze
            case Biome.VenusLava:
            case Biome.VenusianPlains:
            case Biome.VenusHighlands:
                enableHeightFog = true;
                groundFogDensity = 0.7f;
                fogFadeHeight = 25f;
                groundFogColor = new Color(0.9f, 0.8f, 0.4f, 1f);
                enableLightShafts = false; // Too thick
                enableDustInLight = false;
                break;
                
            // Gas giants - swirling atmosphere
            case Biome.JovianClouds:
            case Biome.JovianStorm:
            case Biome.SaturnSurface:
                enableHeightFog = true;
                groundFogDensity = 0.5f;
                groundFogColor = new Color(0.9f, 0.85f, 0.7f, 1f);
                enableLightShafts = false;
                enableDustInLight = false;
                break;
                
            // Airless moons - no atmosphere
            case Biome.MoonDunes:
            case Biome.MoonCraters:
            case Biome.MercuryPlains:
            case Biome.EuropaIce:
            case Biome.PlutoCryo:
                enableHeightFog = false;
                enableLightShafts = false;
                enableDustInLight = false;
                break;
                
            default:
                // Default Earth-like atmosphere
                enableHeightFog = true;
                groundFogDensity = 0.2f;
                groundFogColor = new Color(0.8f, 0.85f, 0.9f, 1f);
                enableLightShafts = true;
                lightShaftIntensity = 0.25f;
                enableDustInLight = true;
                dustParticleCount = 150;
                break;
        }
    }
    
    void Update()
    {
        // Animate light shafts (subtle movement)
        if (lightShaftPlanes != null)
        {
            float time = Time.time;
            for (int i = 0; i < lightShaftPlanes.Length; i++)
            {
                if (lightShaftPlanes[i] != null)
                {
                    // Gentle swaying
                    float sway = Mathf.Sin(time * 0.2f + i * 1.5f) * 0.5f;
                    Vector3 rot = lightShaftPlanes[i].transform.localEulerAngles;
                    rot.z = sway;
                    lightShaftPlanes[i].transform.localEulerAngles = rot;
                    
                    // Pulse intensity
                    if (lightShaftMaterials != null && lightShaftMaterials[i] != null)
                    {
                        float pulse = 0.8f + Mathf.Sin(time * 0.3f + i) * 0.2f;
                        Color c = lightShaftColor;
                        c.a = lightShaftIntensity * pulse;
                        lightShaftMaterials[i].SetColor("_BaseColor", c);
                        lightShaftMaterials[i].SetColor("_Color", c);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Clear all atmosphere effects
    /// </summary>
    public void ClearAtmosphere()
    {
        if (heightFogPlane != null)
        {
            if (Application.isPlaying) Destroy(heightFogPlane);
            else DestroyImmediate(heightFogPlane);
            heightFogPlane = null;
        }
        
        if (lightShaftPlanes != null)
        {
            foreach (var shaft in lightShaftPlanes)
            {
                if (shaft != null)
                {
                    if (Application.isPlaying) Destroy(shaft);
                    else DestroyImmediate(shaft);
                }
            }
            lightShaftPlanes = null;
        }
        
        if (dustParticles != null)
        {
            if (Application.isPlaying) Destroy(dustParticles.gameObject);
            else DestroyImmediate(dustParticles.gameObject);
            dustParticles = null;
        }
        
        heightFogMaterial = null;
        lightShaftMaterials = null;
    }
    
    void OnDestroy()
    {
        ClearAtmosphere();
    }
}

