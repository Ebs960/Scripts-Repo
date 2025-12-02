using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// GPU Instanced Grass System - High performance grass rendering using Graphics.DrawMeshInstanced
/// Bypasses Unity's Terrain Detail system for reliable, controllable grass.
/// Features: Voronoi clumping, multiple blade types, natural distribution.
/// Renders thousands of grass blades with minimal draw calls.
/// </summary>
public class GPUInstancedGrass : MonoBehaviour
{
    [Header("Grass Appearance")]
    [Tooltip("Number of grass instances to spawn")]
    [Range(1000, 100000)]
    public int grassCount = 20000;
    
    [Tooltip("Minimum grass blade height")]
    [Range(0.1f, 1f)]
    public float minHeight = 0.3f;
    
    [Tooltip("Maximum grass blade height")]
    [Range(0.5f, 2f)]
    public float maxHeight = 0.8f;
    
    [Tooltip("Minimum grass blade width")]
    [Range(0.05f, 0.5f)]
    public float minWidth = 0.1f;
    
    [Tooltip("Maximum grass blade width")]
    [Range(0.1f, 1f)]
    public float maxWidth = 0.3f;
    
    [Header("Voronoi Clumping")]
    [Tooltip("Enable natural clumping (groups grass into Voronoi cells)")]
    public bool enableClumping = true;
    
    [Tooltip("Number of Voronoi cells (clumps) to create")]
    [Range(10, 200)]
    public int clumpCount = 50;
    
    [Tooltip("How much grass density varies between clumps (0 = uniform, 1 = very patchy)")]
    [Range(0f, 1f)]
    public float clumpDensityVariation = 0.6f;
    
    [Tooltip("How much grass height varies within clumps")]
    [Range(0f, 0.5f)]
    public float clumpHeightVariation = 0.25f;
    
    [Tooltip("How much grass color varies between clumps")]
    [Range(0f, 0.5f)]
    public float clumpColorVariation = 0.3f;
    
    [Header("Blade Variety")]
    [Tooltip("Use multiple grass blade shapes for variety")]
    public bool useMultipleBladeTypes = true;
    
    [Tooltip("Number of different blade types (1-6)")]
    [Range(1, 6)]
    public int bladeTypeCount = 4;
    
    [Header("Distribution")]
    [Tooltip("Area size for grass spawning (will be set by BattleMapGenerator)")]
    public float spawnAreaSize = 100f;
    
    [Tooltip("Maximum slope angle for grass placement (degrees)")]
    [Range(0f, 60f)]
    public float maxSlopeAngle = 45f;
    
    [Tooltip("Density multiplier (1.0 = normal, 2.0 = double density)")]
    [Range(0.1f, 3f)]
    public float densityMultiplier = 1f;
    
    [Header("Colors")]
    [Tooltip("Base grass color (healthy)")]
    public Color grassColorBase = new Color(0.4f, 0.7f, 0.3f);
    
    [Tooltip("Tip grass color (sun-bleached)")]
    public Color grassColorTip = new Color(0.6f, 0.8f, 0.4f);
    
    [Tooltip("Dry grass color variation")]
    public Color grassColorDry = new Color(0.7f, 0.6f, 0.4f);
    
    [Tooltip("Color variation amount")]
    [Range(0f, 0.5f)]
    public float colorVariation = 0.15f;
    
    [Header("Wind")]
    [Tooltip("Wind strength")]
    [Range(0f, 2f)]
    public float windStrength = 0.5f;
    
    [Tooltip("Wind speed")]
    [Range(0f, 5f)]
    public float windSpeed = 1f;
    
    [Tooltip("Wind direction")]
    public Vector3 windDirection = new Vector3(1f, 0f, 0.3f);
    
    [Header("Performance")]
    [Tooltip("Maximum render distance for grass")]
    [Range(50f, 500f)]
    public float renderDistance = 150f;
    
    [Tooltip("LOD distance (grass gets simpler beyond this)")]
    [Range(20f, 100f)]
    public float lodDistance = 50f;
    
    [Tooltip("Batch size for GPU instancing (max 1023)")]
    [Range(100, 1023)]
    public int batchSize = 1000;
    
    // Internal data - now supports multiple blade types
    private Mesh grassMesh; // Single mesh for legacy/fallback
    private Mesh[] grassMeshes; // Multiple blade meshes for variety
    private Material grassMaterial;
    private List<List<Matrix4x4[]>> grassBatchesByType = new List<List<Matrix4x4[]>>(); // Per blade type
    private List<Vector4[]> grassColorBatches = new List<Vector4[]>(); // Per-instance colors (NOTE: Currently unused - requires custom shader with per-instance color support)
    private MaterialPropertyBlock propertyBlock;
    private Terrain terrain;
    private Camera mainCamera;
    private bool isInitialized = false;
    
    // Voronoi clump data
    private Vector2[] voronoiCenters;
    private float[] voronoiDensity;      // Density multiplier per clump (0-1)
    private float[] voronoiHeightBias;   // Height adjustment per clump (-0.5 to +0.5)
    private Color[] voronoiColorBias;    // Color tint per clump
    
    // Legacy single-batch support
    private List<Matrix4x4[]> grassBatches = new List<Matrix4x4[]>();
    
    // Shader property IDs
    private static readonly int WindStrengthID = Shader.PropertyToID("_WindStrength");
    private static readonly int WindSpeedID = Shader.PropertyToID("_WindSpeed");
    private static readonly int WindDirectionID = Shader.PropertyToID("_WindDirection");
    private static readonly int TimeID = Shader.PropertyToID("_Time");
    private static readonly int ColorArrayID = Shader.PropertyToID("_Colors");
    
    /// <summary>
    /// Initialize grass for the battlefield
    /// </summary>
    public void CreateGrass(float mapSize, Biome biome, Terrain terrainRef = null)
    {
        // Clear existing grass
        ClearGrass();
        
        spawnAreaSize = mapSize;
        terrain = terrainRef;
        mainCamera = Camera.main;
        
        // Adapt colors to biome
        AdaptToBiome(biome);
        
        // Initialize Voronoi clumps for natural distribution
        if (enableClumping)
        {
            InitializeVoronoiClumps();
        }
        
        // Create grass meshes (multiple types for variety)
        if (useMultipleBladeTypes)
        {
            CreateMultipleGrassMeshes();
        }
        else
        {
            CreateGrassMesh();
        }
        
        // Create grass material
        CreateGrassMaterial();
        
        // Generate grass positions with clumping
        GenerateGrassPositions();
        
        propertyBlock = new MaterialPropertyBlock();
        isInitialized = true;
        
        // GPU grass initialized for biome
    }
    
    /// <summary>
    /// Initialize Voronoi cell centers and properties for natural clumping
    /// Each cell will have its own density, height, and color characteristics
    /// </summary>
    private void InitializeVoronoiClumps()
    {
        System.Random rand = new System.Random(42); // Deterministic
        
        voronoiCenters = new Vector2[clumpCount];
        voronoiDensity = new float[clumpCount];
        voronoiHeightBias = new float[clumpCount];
        voronoiColorBias = new Color[clumpCount];
        
        float halfSize = spawnAreaSize / 2f;
        
        for (int i = 0; i < clumpCount; i++)
        {
            // Random cell center position
            voronoiCenters[i] = new Vector2(
                (float)(rand.NextDouble() * 2 - 1) * halfSize,
                (float)(rand.NextDouble() * 2 - 1) * halfSize
            );
            
            // Random density (some clumps are sparse, some are dense)
            // Using bell curve distribution for more natural feel
            float densityRoll = (float)(rand.NextDouble() + rand.NextDouble() + rand.NextDouble()) / 3f;
            voronoiDensity[i] = Mathf.Lerp(1f - clumpDensityVariation, 1f + clumpDensityVariation * 0.5f, densityRoll);
            
            // Random height bias
            voronoiHeightBias[i] = ((float)rand.NextDouble() - 0.5f) * 2f * clumpHeightVariation;
            
            // Random color bias (shift toward healthy, dry, or slightly different hue)
            float colorType = (float)rand.NextDouble();
            if (colorType < 0.4f)
            {
                // Healthy patch - slightly more green
                voronoiColorBias[i] = Color.Lerp(grassColorBase, 
                    new Color(grassColorBase.r - 0.1f, grassColorBase.g + 0.1f, grassColorBase.b - 0.05f), 
                    clumpColorVariation);
            }
            else if (colorType < 0.7f)
            {
                // Normal patch
                voronoiColorBias[i] = grassColorBase;
            }
            else if (colorType < 0.9f)
            {
                // Dry patch
                voronoiColorBias[i] = Color.Lerp(grassColorBase, grassColorDry, clumpColorVariation);
            }
            else
            {
                // Very dry patch (sparse)
                voronoiColorBias[i] = Color.Lerp(grassColorDry, 
                    new Color(grassColorDry.r + 0.1f, grassColorDry.g, grassColorDry.b - 0.05f), 
                    clumpColorVariation);
                voronoiDensity[i] *= 0.5f; // Also reduce density
            }
        }
    }
    
    /// <summary>
    /// Find which Voronoi clump a position belongs to
    /// Returns the clump index
    /// </summary>
    private int GetVoronoiClump(float x, float z)
    {
        if (voronoiCenters == null || voronoiCenters.Length == 0)
            return 0;
        
        int nearestClump = 0;
        float nearestDist = float.MaxValue;
        
        for (int i = 0; i < voronoiCenters.Length; i++)
        {
            float dx = x - voronoiCenters[i].x;
            float dz = z - voronoiCenters[i].y;
            float dist = dx * dx + dz * dz; // Squared distance is fine for comparison
            
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearestClump = i;
            }
        }
        
        return nearestClump;
    }
    
    /// <summary>
    /// Get smooth blend between Voronoi clumps for gradual transitions
    /// Returns interpolated properties based on nearby clumps
    /// </summary>
    private void GetBlendedClumpProperties(float x, float z, out float density, out float heightBias, out Color colorBias)
    {
        if (!enableClumping || voronoiCenters == null || voronoiCenters.Length == 0)
        {
            density = 1f;
            heightBias = 0f;
            colorBias = grassColorBase;
            return;
        }
        
        // Find two nearest clumps and blend between them
        int nearest1 = 0, nearest2 = 0;
        float dist1 = float.MaxValue, dist2 = float.MaxValue;
        
        for (int i = 0; i < voronoiCenters.Length; i++)
        {
            float dx = x - voronoiCenters[i].x;
            float dz = z - voronoiCenters[i].y;
            float dist = dx * dx + dz * dz;
            
            if (dist < dist1)
            {
                dist2 = dist1;
                nearest2 = nearest1;
                dist1 = dist;
                nearest1 = i;
            }
            else if (dist < dist2)
            {
                dist2 = dist;
                nearest2 = i;
            }
        }
        
        // Calculate blend factor (0 = fully nearest1, 1 = 50/50 blend)
        float d1 = Mathf.Sqrt(dist1);
        float d2 = Mathf.Sqrt(dist2);
        float totalDist = d1 + d2;
        float blend = (totalDist > 0.01f) ? (d1 / totalDist) : 0f;
        blend = Mathf.Clamp01(blend);
        
        // Only blend significantly at cell boundaries
        float edgeBlend = Mathf.SmoothStep(0f, 1f, blend * 2f);
        
        // Interpolate properties
        density = Mathf.Lerp(voronoiDensity[nearest1], voronoiDensity[nearest2], edgeBlend);
        heightBias = Mathf.Lerp(voronoiHeightBias[nearest1], voronoiHeightBias[nearest2], edgeBlend);
        colorBias = Color.Lerp(voronoiColorBias[nearest1], voronoiColorBias[nearest2], edgeBlend);
    }
    
    /// <summary>
    /// Create multiple grass blade mesh types for visual variety
    /// Types: Thin straight, Wide curved, Bent, Split tip, Wispy, Thick
    /// </summary>
    private void CreateMultipleGrassMeshes()
    {
        int types = Mathf.Clamp(bladeTypeCount, 1, 6);
        grassMeshes = new Mesh[types];
        grassBatchesByType.Clear();
        
        for (int t = 0; t < types; t++)
        {
            grassMeshes[t] = CreateBladeType(t);
            grassBatchesByType.Add(new List<Matrix4x4[]>());
        }
    }
    
    /// <summary>
    /// Create a specific grass blade type mesh
    /// </summary>
    private Mesh CreateBladeType(int type)
    {
        Mesh mesh = new Mesh();
        mesh.name = $"GrassBlade_Type{type}";
        
        Vector3[] vertices;
        Vector2[] uvs;
        Vector2[] uv2;
        int[] triangles;
        
        switch (type)
        {
            case 0: // Standard thin blade
                vertices = CreateThinBladeVertices();
                break;
            case 1: // Wide blade
                vertices = CreateWideBladeVertices();
                break;
            case 2: // Curved/bent blade
                vertices = CreateCurvedBladeVertices();
                break;
            case 3: // Split tip (forked)
                vertices = CreateSplitTipVertices();
                break;
            case 4: // Wispy thin blade
                vertices = CreateWispyBladeVertices();
                break;
            case 5: // Thick robust blade
                vertices = CreateThickBladeVertices();
                break;
            default:
                vertices = CreateThinBladeVertices();
                break;
        }
        
        // Generate UVs and triangles based on vertex count
        GenerateBladeUVsAndTriangles(vertices, out uvs, out uv2, out triangles);
        
        Vector3[] normals = new Vector3[vertices.Length];
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = Vector3.up;
        }
        
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.uv2 = uv2;
        mesh.triangles = triangles;
        mesh.normals = normals;
        mesh.RecalculateBounds();
        
        return mesh;
    }
    
    private Vector3[] CreateThinBladeVertices()
    {
        return new Vector3[]
        {
            new Vector3(-0.3f, 0f, 0f),
            new Vector3(0.3f, 0f, 0f),
            new Vector3(-0.25f, 0.33f, 0f),
            new Vector3(0.25f, 0.33f, 0f),
            new Vector3(-0.15f, 0.66f, 0f),
            new Vector3(0.15f, 0.66f, 0f),
            new Vector3(0f, 1f, 0f)
        };
    }
    
    private Vector3[] CreateWideBladeVertices()
    {
        return new Vector3[]
        {
            new Vector3(-0.5f, 0f, 0f),
            new Vector3(0.5f, 0f, 0f),
            new Vector3(-0.45f, 0.3f, 0.05f),
            new Vector3(0.45f, 0.3f, 0.05f),
            new Vector3(-0.35f, 0.6f, 0.08f),
            new Vector3(0.35f, 0.6f, 0.08f),
            new Vector3(-0.15f, 0.85f, 0.1f),
            new Vector3(0.15f, 0.85f, 0.1f),
            new Vector3(0f, 1f, 0.12f)
        };
    }
    
    private Vector3[] CreateCurvedBladeVertices()
    {
        // Blade curves to one side as it goes up
        return new Vector3[]
        {
            new Vector3(-0.35f, 0f, 0f),
            new Vector3(0.35f, 0f, 0f),
            new Vector3(-0.2f, 0.33f, 0.1f),
            new Vector3(0.4f, 0.33f, 0.1f),
            new Vector3(-0.05f, 0.66f, 0.2f),
            new Vector3(0.35f, 0.66f, 0.2f),
            new Vector3(0.15f, 1f, 0.25f)
        };
    }
    
    private Vector3[] CreateSplitTipVertices()
    {
        // Two tips instead of one
        return new Vector3[]
        {
            new Vector3(-0.4f, 0f, 0f),
            new Vector3(0.4f, 0f, 0f),
            new Vector3(-0.35f, 0.4f, 0f),
            new Vector3(0.35f, 0.4f, 0f),
            new Vector3(-0.3f, 0.7f, 0f),
            new Vector3(0.3f, 0.7f, 0f),
            // Split into two tips
            new Vector3(-0.2f, 1f, 0.05f),
            new Vector3(0.2f, 1f, -0.05f)
        };
    }
    
    private Vector3[] CreateWispyBladeVertices()
    {
        // Very thin, elegant blade
        return new Vector3[]
        {
            new Vector3(-0.15f, 0f, 0f),
            new Vector3(0.15f, 0f, 0f),
            new Vector3(-0.1f, 0.4f, 0.02f),
            new Vector3(0.1f, 0.4f, 0.02f),
            new Vector3(-0.05f, 0.75f, 0.05f),
            new Vector3(0.05f, 0.75f, 0.05f),
            new Vector3(0f, 1f, 0.08f)
        };
    }
    
    private Vector3[] CreateThickBladeVertices()
    {
        // Robust, thick blade
        return new Vector3[]
        {
            new Vector3(-0.6f, 0f, 0f),
            new Vector3(0.6f, 0f, 0f),
            new Vector3(-0.55f, 0.25f, 0.02f),
            new Vector3(0.55f, 0.25f, 0.02f),
            new Vector3(-0.45f, 0.5f, 0.04f),
            new Vector3(0.45f, 0.5f, 0.04f),
            new Vector3(-0.3f, 0.75f, 0.06f),
            new Vector3(0.3f, 0.75f, 0.06f),
            new Vector3(0f, 1f, 0.08f)
        };
    }
    
    /// <summary>
    /// Generate UVs and triangles for any blade vertex configuration
    /// </summary>
    private void GenerateBladeUVsAndTriangles(Vector3[] vertices, out Vector2[] uvs, out Vector2[] uv2, out int[] triangles)
    {
        uvs = new Vector2[vertices.Length];
        uv2 = new Vector2[vertices.Length];
        
        // Generate UVs based on vertex positions
        float maxHeight = 0f;
        foreach (var v in vertices)
        {
            if (v.y > maxHeight) maxHeight = v.y;
        }
        
        for (int i = 0; i < vertices.Length; i++)
        {
            float heightRatio = maxHeight > 0 ? vertices[i].y / maxHeight : 0f;
            uvs[i] = new Vector2(vertices[i].x + 0.5f, heightRatio);
            uv2[i] = new Vector2(0f, heightRatio); // Height for wind bending
        }
        
        // Generate triangles (pairs of vertices form quads, plus tip)
        List<int> triList = new List<int>();
        int vertexPairs = (vertices.Length - 1) / 2;
        
        for (int i = 0; i < vertexPairs; i++)
        {
            int bl = i * 2;
            int br = i * 2 + 1;
            int tl = i * 2 + 2;
            int tr = i * 2 + 3;
            
            // Handle odd vertex counts (single tip vertex)
            if (tl >= vertices.Length) break;
            if (tr >= vertices.Length) tr = tl;
            
            // Front face
            triList.Add(bl);
            triList.Add(tl);
            triList.Add(br);
            
            if (tr != tl)
            {
                triList.Add(br);
                triList.Add(tl);
                triList.Add(tr);
            }
            
            // Back face (for double-sided)
            triList.Add(bl);
            triList.Add(br);
            triList.Add(tl);
            
            if (tr != tl)
            {
                triList.Add(br);
                triList.Add(tr);
                triList.Add(tl);
            }
        }
        
        // Handle single tip vertex
        if (vertices.Length % 2 == 1)
        {
            int tipIdx = vertices.Length - 1;
            int leftIdx = vertices.Length - 3;
            int rightIdx = vertices.Length - 2;
            
            if (leftIdx >= 0 && rightIdx >= 0)
            {
                triList.Add(leftIdx);
                triList.Add(tipIdx);
                triList.Add(rightIdx);
                triList.Add(leftIdx);
                triList.Add(rightIdx);
                triList.Add(tipIdx);
            }
        }
        
        triangles = triList.ToArray();
    }
    
    /// <summary>
    /// Create a simple grass blade mesh
    /// </summary>
    private void CreateGrassMesh()
    {
        grassMesh = new Mesh();
        grassMesh.name = "GrassBlade";
        
        // Simple quad-based grass blade with 3 segments for bending
        Vector3[] vertices = new Vector3[]
        {
            // Bottom
            new Vector3(-0.5f, 0f, 0f),
            new Vector3(0.5f, 0f, 0f),
            // Lower middle
            new Vector3(-0.4f, 0.33f, 0f),
            new Vector3(0.4f, 0.33f, 0f),
            // Upper middle
            new Vector3(-0.25f, 0.66f, 0f),
            new Vector3(0.25f, 0.66f, 0f),
            // Top (pointed)
            new Vector3(0f, 1f, 0f)
        };
        
        Vector2[] uvs = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(1f, 0f),
            new Vector2(0f, 0.33f),
            new Vector2(1f, 0.33f),
            new Vector2(0f, 0.66f),
            new Vector2(1f, 0.66f),
            new Vector2(0.5f, 1f)
        };
        
        // UV2 stores height for wind bending (0 at bottom, 1 at top)
        Vector2[] uv2 = new Vector2[]
        {
            new Vector2(0f, 0f),
            new Vector2(0f, 0f),
            new Vector2(0f, 0.33f),
            new Vector2(0f, 0.33f),
            new Vector2(0f, 0.66f),
            new Vector2(0f, 0.66f),
            new Vector2(0f, 1f)
        };
        
        int[] triangles = new int[]
        {
            // Front faces
            0, 2, 1,
            1, 2, 3,
            2, 4, 3,
            3, 4, 5,
            4, 6, 5,
            // Back faces (for double-sided rendering)
            0, 1, 2,
            1, 3, 2,
            2, 3, 4,
            3, 5, 4,
            4, 5, 6
        };
        
        Vector3[] normals = new Vector3[vertices.Length];
        for (int i = 0; i < normals.Length; i++)
        {
            normals[i] = Vector3.up; // All normals point up for consistent lighting
        }
        
        grassMesh.vertices = vertices;
        grassMesh.uv = uvs;
        grassMesh.uv2 = uv2;
        grassMesh.triangles = triangles;
        grassMesh.normals = normals;
        grassMesh.RecalculateBounds();
    }
    
    /// <summary>
    /// Create grass material with wind animation support
    /// </summary>
    private void CreateGrassMaterial()
    {
        // Try to find appropriate shader with fallbacks
        Shader grassShader = Shader.Find("Universal Render Pipeline/Lit");
        if (grassShader == null)
        {
            grassShader = Shader.Find("Standard");
        }
        if (grassShader == null)
        {
            grassShader = Shader.Find("Unlit/Color");
        }
        if (grassShader == null)
        {
            Debug.LogError("[GPUInstancedGrass] No shader found! Grass will not render. Make sure URP or Standard shaders are available.");
            return; // Cannot create material without shader
        }
        
        grassMaterial = new Material(grassShader);
        grassMaterial.name = "GPUGrassMaterial";
        
        // Enable GPU instancing
        grassMaterial.enableInstancing = true;
        
        // Set base color (check property existence first)
        if (grassMaterial.HasProperty("_BaseColor"))
            grassMaterial.SetColor("_BaseColor", grassColorBase);
        else if (grassMaterial.HasProperty("_Color"))
            grassMaterial.SetColor("_Color", grassColorBase);
        
        // Disable shadows for performance (optional - can enable if needed)
        if (grassMaterial.HasProperty("_Cutoff"))
            grassMaterial.SetFloat("_Cutoff", 0.1f);
        
        // Double-sided rendering
        if (grassMaterial.HasProperty("_Cull"))
            grassMaterial.SetFloat("_Cull", 0); // Off
        
        // Set smoothness low for matte grass look
        if (grassMaterial.HasProperty("_Smoothness"))
            grassMaterial.SetFloat("_Smoothness", 0.1f);
    }
    
    /// <summary>
    /// Generate grass positions across the terrain with Voronoi clumping
    /// Uses multiple blade types for natural variety
    /// </summary>
    private void GenerateGrassPositions()
    {
        grassBatches.Clear();
        grassColorBatches.Clear();
        grassBatchesByType.Clear();
        
        // Initialize per-type batch lists
        int typeCount = (useMultipleBladeTypes && grassMeshes != null) ? grassMeshes.Length : 1;
        for (int t = 0; t < typeCount; t++)
        {
            grassBatchesByType.Add(new List<Matrix4x4[]>());
        }
        
        // Current batches per type
        List<List<Matrix4x4>> currentBatchesByType = new List<List<Matrix4x4>>();
        for (int t = 0; t < typeCount; t++)
        {
            currentBatchesByType.Add(new List<Matrix4x4>());
        }
        
        int actualGrassCount = Mathf.RoundToInt(grassCount * densityMultiplier);
        
        List<Vector4> currentColorBatch = new List<Vector4>();
        
        float halfSize = spawnAreaSize / 2f;
        int attempts = 0;
        int maxAttempts = actualGrassCount * 3;
        int placed = 0;
        
        System.Random rand = new System.Random(42);
        
        while (placed < actualGrassCount && attempts < maxAttempts)
        {
            attempts++;
            
            // Random position within area
            float x = (float)(rand.NextDouble() * 2 - 1) * halfSize;
            float z = (float)(rand.NextDouble() * 2 - 1) * halfSize;
            
            // Get clump properties for this position
            float clumpDensity, clumpHeightBias;
            Color clumpColor;
            GetBlendedClumpProperties(x, z, out clumpDensity, out clumpHeightBias, out clumpColor);
            
            // Skip grass based on clump density (probabilistic culling)
            if ((float)rand.NextDouble() > clumpDensity)
            {
                continue; // This spot is in a sparse area of the clump
            }
            
            // Get terrain height if available
            float y = 0f;
            Vector3 normal = Vector3.up;
            
            if (terrain != null)
            {
                Vector3 worldPos = new Vector3(x, 0, z);
                y = terrain.SampleHeight(worldPos);
                
                Vector3 terrainPos = worldPos - terrain.transform.position;
                float normX = Mathf.Clamp01(terrainPos.x / terrain.terrainData.size.x);
                float normZ = Mathf.Clamp01(terrainPos.z / terrain.terrainData.size.z);
                normal = terrain.terrainData.GetInterpolatedNormal(normX, normZ);
            }
            else
            {
                RaycastHit hit;
                if (Physics.Raycast(new Vector3(x, 100f, z), Vector3.down, out hit, 200f))
                {
                    y = hit.point.y;
                    normal = hit.normal;
                }
            }
            
            // Check slope
            float slopeAngle = Vector3.Angle(Vector3.up, normal);
            if (slopeAngle > maxSlopeAngle)
            {
                continue;
            }
            
            // Random size with clump height bias
            float baseHeight = Mathf.Lerp(minHeight, maxHeight, (float)rand.NextDouble());
            float height = baseHeight * (1f + clumpHeightBias);
            height = Mathf.Clamp(height, minHeight * 0.5f, maxHeight * 1.5f);
            
            float width = Mathf.Lerp(minWidth, maxWidth, (float)rand.NextDouble());
            
            // Random rotation
            float rotation = (float)rand.NextDouble() * 360f;
            
            // Random tilt (affected by slope for natural alignment)
            float tiltX = ((float)rand.NextDouble() - 0.5f) * 15f + (normal.x * 10f);
            float tiltZ = ((float)rand.NextDouble() - 0.5f) * 15f + (normal.z * 10f);
            
            // Create transformation matrix
            Vector3 position = new Vector3(x, y, z);
            Quaternion rot = Quaternion.Euler(tiltX, rotation, tiltZ);
            Vector3 scale = new Vector3(width, height, width);
            
            Matrix4x4 matrix = Matrix4x4.TRS(position, rot, scale);
            
            // Choose blade type based on position and random factor
            // Blades in same area tend to be similar type (more natural)
            int bladeType = 0;
            if (typeCount > 1)
            {
                int clumpIdx = GetVoronoiClump(x, z);
                float typeRandom = (float)rand.NextDouble();
                // Primary type for this clump, with chance of variation
                int primaryType = clumpIdx % typeCount;
                if (typeRandom < 0.7f)
                {
                    bladeType = primaryType;
                }
                else
                {
                    bladeType = rand.Next(0, typeCount);
                }
            }
            
            currentBatchesByType[bladeType].Add(matrix);
            
            // Color with clump variation and individual variation
            float individualColorVar = (float)rand.NextDouble() * colorVariation * 0.5f;
            Color grassColor = Color.Lerp(clumpColor, grassColorDry, individualColorVar);
            
            // Add slight random hue shift for even more natural look
            float hueShift = ((float)rand.NextDouble() - 0.5f) * 0.05f;
            grassColor.r = Mathf.Clamp01(grassColor.r + hueShift);
            grassColor.g = Mathf.Clamp01(grassColor.g + hueShift * 0.5f);
            
            currentColorBatch.Add(new Vector4(grassColor.r, grassColor.g, grassColor.b, grassColor.a));
            
            placed++;
            
            // Create new batches when full
            for (int t = 0; t < typeCount; t++)
            {
                if (currentBatchesByType[t].Count >= batchSize)
                {
                    grassBatchesByType[t].Add(currentBatchesByType[t].ToArray());
                    currentBatchesByType[t].Clear();
                }
            }
            
            if (currentColorBatch.Count >= batchSize)
            {
                grassColorBatches.Add(currentColorBatch.ToArray());
                currentColorBatch.Clear();
            }
        }
        
        // Add remaining grass
        for (int t = 0; t < typeCount; t++)
        {
            if (currentBatchesByType[t].Count > 0)
            {
                grassBatchesByType[t].Add(currentBatchesByType[t].ToArray());
            }
        }
        
        if (currentColorBatch.Count > 0)
        {
            grassColorBatches.Add(currentColorBatch.ToArray());
        }
        
        // Build legacy single batch list for fallback
        grassBatches.Clear();
        foreach (var typeBatches in grassBatchesByType)
        {
            grassBatches.AddRange(typeBatches);
        }
        
        // Grass placement complete with Voronoi clumping
    }
    
    /// <summary>
    /// Adapt grass colors and density to biome
    /// </summary>
    private void AdaptToBiome(Biome biome)
    {
        switch (biome)
        {
            case Biome.Grassland:
                grassColorBase = new Color(0.4f, 0.75f, 0.35f);
                grassColorTip = new Color(0.55f, 0.85f, 0.45f);
                grassColorDry = new Color(0.65f, 0.6f, 0.4f);
                densityMultiplier = 1.5f;
                maxHeight = 0.9f;
                break;
                
            case Biome.Plains:
                grassColorBase = new Color(0.5f, 0.7f, 0.4f);
                grassColorTip = new Color(0.65f, 0.8f, 0.5f);
                grassColorDry = new Color(0.75f, 0.65f, 0.45f);
                densityMultiplier = 1.2f;
                break;
                
            case Biome.Forest:
                grassColorBase = new Color(0.35f, 0.6f, 0.3f);
                grassColorTip = new Color(0.45f, 0.7f, 0.4f);
                grassColorDry = new Color(0.55f, 0.5f, 0.35f);
                densityMultiplier = 1.0f;
                maxHeight = 0.6f;
                break;
                
            case Biome.Jungle:
            case Biome.Rainforest:
                grassColorBase = new Color(0.3f, 0.65f, 0.35f);
                grassColorTip = new Color(0.4f, 0.75f, 0.45f);
                grassColorDry = new Color(0.45f, 0.55f, 0.35f);
                densityMultiplier = 0.8f; // Less grass, more undergrowth
                maxHeight = 0.5f;
                break;
                
            case Biome.Savannah:
                grassColorBase = new Color(0.7f, 0.6f, 0.35f);
                grassColorTip = new Color(0.8f, 0.7f, 0.45f);
                grassColorDry = new Color(0.85f, 0.75f, 0.5f);
                densityMultiplier = 0.9f;
                maxHeight = 1.2f; // Tall savannah grass
                break;
                
            case Biome.Steppe:
                grassColorBase = new Color(0.65f, 0.55f, 0.4f);
                grassColorTip = new Color(0.75f, 0.65f, 0.5f);
                grassColorDry = new Color(0.8f, 0.7f, 0.55f);
                densityMultiplier = 1.0f;
                maxHeight = 0.7f;
                break;
                
            case Biome.Swamp:
            case Biome.Marsh:
                grassColorBase = new Color(0.35f, 0.5f, 0.3f);
                grassColorTip = new Color(0.45f, 0.6f, 0.4f);
                grassColorDry = new Color(0.5f, 0.5f, 0.35f);
                densityMultiplier = 0.8f;
                maxHeight = 0.8f;
                break;
                
            case Biome.Taiga:
            case Biome.PineForest:
                grassColorBase = new Color(0.4f, 0.55f, 0.4f);
                grassColorTip = new Color(0.5f, 0.65f, 0.5f);
                grassColorDry = new Color(0.55f, 0.55f, 0.45f);
                densityMultiplier = 0.7f;
                break;
                
            case Biome.Tundra:
                grassColorBase = new Color(0.5f, 0.55f, 0.45f);
                grassColorTip = new Color(0.6f, 0.65f, 0.55f);
                grassColorDry = new Color(0.65f, 0.6f, 0.5f);
                densityMultiplier = 0.4f;
                maxHeight = 0.3f;
                break;
                
            case Biome.Snow:
            case Biome.Frozen:
            case Biome.Arctic:
                grassColorBase = new Color(0.6f, 0.65f, 0.55f);
                grassColorTip = new Color(0.7f, 0.75f, 0.65f);
                grassColorDry = new Color(0.75f, 0.7f, 0.6f);
                densityMultiplier = 0.2f;
                maxHeight = 0.25f;
                break;
                
            case Biome.Desert:
                grassColorBase = new Color(0.75f, 0.65f, 0.45f);
                grassColorTip = new Color(0.85f, 0.75f, 0.55f);
                grassColorDry = new Color(0.9f, 0.8f, 0.6f);
                densityMultiplier = 0.15f;
                maxHeight = 0.3f;
                break;
                
            // No grass for these biomes
            case Biome.Ocean:
            case Biome.Volcanic:
            case Biome.VenusLava:
            case Biome.IoVolcanic:
            case Biome.MartianRegolith:
            case Biome.MartianDunes:
            case Biome.MartianCanyon:
            case Biome.MartianPolarIce:
            case Biome.MercuryBasalt:
            case Biome.MercuryCraters:
            case Biome.MercuryScarp:
            case Biome.MercurianIce:
            case Biome.MoonDunes:
            case Biome.MoonCaves:
                densityMultiplier = 0f;
                break;
                
            default:
                // Default grass settings
                densityMultiplier = 1f;
                break;
        }
    }
    
    void Update()
    {
        if (!isInitialized || grassMaterial == null) return;
        
        // Update wind parameters (only if shader supports them)
        // Note: Standard URP/Lit shaders don't have these properties by default
        // These would need to be added via a custom shader for wind animation
        if (grassMaterial.HasProperty(WindStrengthID))
            grassMaterial.SetFloat(WindStrengthID, windStrength);
        if (grassMaterial.HasProperty(WindSpeedID))
            grassMaterial.SetFloat(WindSpeedID, windSpeed);
        if (grassMaterial.HasProperty(WindDirectionID))
            grassMaterial.SetVector(WindDirectionID, windDirection.normalized);
        
        // Render grass with multiple blade types
        if (useMultipleBladeTypes && grassMeshes != null && grassBatchesByType.Count > 0)
        {
            // Render each blade type
            for (int t = 0; t < grassMeshes.Length && t < grassBatchesByType.Count; t++)
            {
                if (grassMeshes[t] == null) continue;
                
                foreach (var batch in grassBatchesByType[t])
                {
                    if (batch != null && batch.Length > 0)
                    {
                        // NOTE: Per-instance colors (grassColorBatches) are not currently applied
                        // To enable per-instance colors, use a custom shader that supports _Colors array
                        // and set it via MaterialPropertyBlock.SetVectorArray("_Colors", colorBatch)
                        Graphics.DrawMeshInstanced(grassMeshes[t], 0, grassMaterial, batch, batch.Length, propertyBlock);
                    }
                }
            }
        }
        else
        {
            // Fallback: single mesh type
            if (grassMesh == null) return;
            if (grassBatches.Count == 0) return;
            
            foreach (var batch in grassBatches)
            {
                if (batch != null && batch.Length > 0)
                {
                    // NOTE: Per-instance colors (grassColorBatches) are not currently applied
                    // To enable per-instance colors, use a custom shader that supports _Colors array
                    Graphics.DrawMeshInstanced(grassMesh, 0, grassMaterial, batch, batch.Length, propertyBlock);
                }
            }
        }
    }
    
    /// <summary>
    /// Set wind direction (sync with other systems)
    /// </summary>
    public void SetWindDirection(Vector3 wind)
    {
        windDirection = wind.normalized;
    }
    
    /// <summary>
    /// Clear all grass data
    /// </summary>
    public void ClearGrass()
    {
        grassBatches.Clear();
        grassColorBatches.Clear();
        grassBatchesByType.Clear();
        
        // Clear Voronoi data
        voronoiCenters = null;
        voronoiDensity = null;
        voronoiHeightBias = null;
        voronoiColorBias = null;
        
        // Clear multiple meshes
        if (grassMeshes != null)
        {
            foreach (var mesh in grassMeshes)
            {
                if (mesh != null)
                {
                    if (Application.isPlaying)
                        Destroy(mesh);
                    else
                        DestroyImmediate(mesh);
                }
            }
            grassMeshes = null;
        }
        
        // Clear single mesh (legacy)
        if (grassMesh != null)
        {
            if (Application.isPlaying)
                Destroy(grassMesh);
            else
                DestroyImmediate(grassMesh);
            grassMesh = null;
        }
        
        if (grassMaterial != null)
        {
            if (Application.isPlaying)
                Destroy(grassMaterial);
            else
                DestroyImmediate(grassMaterial);
            grassMaterial = null;
        }
        
        isInitialized = false;
    }
    
    void OnDestroy()
    {
        ClearGrass();
    }
    
    void OnDisable()
    {
        // Stop rendering when disabled
        isInitialized = false;
    }
    
    void OnEnable()
    {
        // Resume rendering when enabled (if we have data)
        bool hasMesh = (grassMesh != null) || (grassMeshes != null && grassMeshes.Length > 0);
        bool hasBatches = (grassBatches.Count > 0) || (grassBatchesByType.Count > 0 && grassBatchesByType[0].Count > 0);
        
        if (hasBatches && grassMaterial != null && hasMesh)
        {
            isInitialized = true;
        }
    }
    
    /// <summary>
    /// Get debug info about current grass state
    /// </summary>
    public string GetDebugInfo()
    {
        int totalBlades = 0;
        int totalBatches = 0;
        
        if (useMultipleBladeTypes && grassBatchesByType != null)
        {
            for (int t = 0; t < grassBatchesByType.Count; t++)
            {
                totalBatches += grassBatchesByType[t].Count;
                foreach (var batch in grassBatchesByType[t])
                {
                    totalBlades += batch.Length;
                }
            }
        }
        else
        {
            totalBatches = grassBatches.Count;
            foreach (var batch in grassBatches)
            {
                totalBlades += batch.Length;
            }
        }
        
        return $"Grass: {totalBlades} blades in {totalBatches} batches, " +
               $"{(useMultipleBladeTypes ? bladeTypeCount : 1)} blade types, " +
               $"Clumping: {(enableClumping ? clumpCount + " cells" : "off")}";
    }
}

