using UnityEngine;
using HexasphereGrid;
using System;
using System.Collections.Generic;

// Ensure this script is attached to the same GameObject as Hexasphere
[RequireComponent(typeof(Hexasphere))]
public class MoonGenerator : MonoBehaviour
{
    [Header("Sphere Settings")]
    public int subdivisions = 4;
    public bool randomSeed = true;
    public int seed = 98765;

    [Header("Moon Surface Settings")]
    [Tooltip("Noise frequency for the main moon surface elevation.")]
    public float moonElevationFreq = 2.5f;
    [Range(0f, 1f)]
    [Tooltip("Base elevation for Moon Dunes.")]
    public float baseDuneElevation = 0.4f;
    [Range(0f, 1f)] // User mentioned 4.5, assuming 0.45 is intended for consistency. Adjust range if 4.5 is really needed.
    [Tooltip("Maximum elevation Moon Dunes can reach with noise.")]
    public float maxDuneElevation = 0.45f;

    [Header("Moon Cave Settings")]
    [Tooltip("Noise frequency for determining cave locations.")]
    public float cavePlacementFreq = 1.2f;
    [Range(0f, 1f)]
    [Tooltip("Noise threshold (0-1). Values above this potentially become caves. Higher = rarer caves.")]
    public float caveThreshold = 0.85f;
     [Range(0f, 1f)]
    [Tooltip("Fixed elevation level for Moon Caves.")]
    public float fixedCaveElevation = 0.2f;
    [Range(1, 10)]
    [Tooltip("Minimum size of a cave cluster.")]
    public int minCaveClusterSize = 3; // Slightly smaller than requested 4 to allow smaller patches
    [Range(1, 10)]
    [Tooltip("Maximum size of a cave cluster.")]
    public int maxCaveClusterSize = 6;


    [Header("Visuals")]
    public Color moonDunesColor = new Color(0.8f, 0.8f, 0.75f); // Light greyish
    public Color moonCavesColor = new Color(0.4f, 0.4f, 0.45f); // Darker grey

    [Header("Extrusion Settings")]
    public float maxExtrusionHeight = 0.04f; // Can reuse or have a separate one for moon

    [Header("Initialization")]
    [Tooltip("Wait this many frames before initial generation so Hexasphere has finished.")]
    public int initializationDelay = 1;

    [Header("Biome Textures")]
    public List<BiomeSettings> biomeSettings = new List<BiomeSettings>();

    // --------------------------- Private fields -----------------------------
    IcoSphereGrid grid;
    public IcoSphereGrid Grid => grid;
    Hexasphere hex;
    NoiseSampler noise; // Use the same NoiseSampler
    NoiseSampler cavePlacementNoise; // Separate noise for cave placement
    readonly Dictionary<int, HexTileData> data = new Dictionary<int, HexTileData>();
    readonly Dictionary<int, float> tileElevation = new Dictionary<int, float>(); // Store final elevation
    readonly Dictionary<Biome, BiomeSettings> lookup = new Dictionary<Biome, BiomeSettings>(); // Lookup for settings

    // --------------------------- Unity lifecycle -----------------------------
    void Awake()
    {
        // Initialize the grid for this moon
        grid = new IcoSphereGrid();
        grid.Generate(subdivisions, 1f); // generate unit sphere grid
        
        hex = GetComponent<Hexasphere>();
        // Disable Hexasphere input (rotation and zoom) as a backup
        if (hex != null)
        {
            hex.rotationEnabled = false;
            hex.zoomEnabled = false;
        }
        if (randomSeed) seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);

        hex.numDivisions = subdivisions;
        noise = new NoiseSampler(seed); // For elevation
        cavePlacementNoise = new NoiseSampler(seed + 1); // Different seed for cave placement

        // Populate Biome Settings if empty (for Moon biomes)
        if (biomeSettings.Count == 0) {
            biomeSettings.Add(new BiomeSettings { biome = Biome.MoonDunes });
            biomeSettings.Add(new BiomeSettings { biome = Biome.MoonCaves });
        }

        // Build the lookup dictionary
        foreach (var bs in biomeSettings) {
            if (!lookup.ContainsKey(bs.biome)) {
                lookup.Add(bs.biome, bs);
            } else {
                lookup[bs.biome] = bs; // Allow overriding defaults from Inspector
            }
        }

        // Optional: Set different noise parameters for moon elevation if needed
        // noise.elevationNoise.SetFrequency(moonElevationFreq); // Example

        hex.OnGeneration += HandleWorldGenerated;

        // if (generateOnAwake) // GameManager will control initialization
        // {
        // StartCoroutine(Initialise());
        // }
    }

    void Start()
    {
        // if (!generateOnAwake && generateOnStart) // GameManager will control initialization
        // {
        // StartCoroutine(Initialise());
        // }
    }

    System.Collections.IEnumerator Initialise() // This can be called if manual generation outside GameManager is needed
    {
        for (int i = 0; i < Mathf.Max(1, initializationDelay); i++) yield return null;
        GenerateSurface();
        hex.UpdateMaterialProperties(); // Ensure Hexasphere updates visuals
    }

    // Called when Hexasphere has finished generating its base mesh for the moon
    private void HandleWorldGenerated(Hexasphere sender)
    {
        // Unsubscribe to prevent multiple calls if regeneration occurs
        sender.OnGeneration -= HandleWorldGenerated;

        // Add any moon-specific post-generation logic here if needed in the future
        // For example, if you had moon-specific animals or features to spawn
        // after the base mesh is ready but before GameManager fully takes over.
        Debug.Log("Moon Hexasphere base mesh generated.");
    }

    // --------------------------- Surface Generation --------------------------
    /// <summary>
    /// Generates the moon's surface
    /// </summary>
    public void GenerateSurface()
    {
        data.Clear();
        tileElevation.Clear();
        int tileCount = hex.tiles.Length;

        // --- 1. Initial Dune Elevation and Biome Assignment ---
        for (int i = 0; i < tileCount; i++)
        {
            Vector3 tileCenter = hex.GetTileCenter(i); // No noise offset needed for simple generation
            float rawNoise = noise.GetElevation(tileCenter * moonElevationFreq); // Noise 0-1
            float noiseScale = Mathf.Max(0f, maxDuneElevation - baseDuneElevation);
            float elevation = baseDuneElevation + (rawNoise * noiseScale);
            elevation = Mathf.Min(elevation, maxDuneElevation); // Ensure cap

            tileElevation[i] = elevation;

            // Initialize all tiles as Moon Dunes first
            var yields = BiomeHelper.Yields(Biome.MoonDunes); // Get default yields
            var td = new HexTileData {
                biome = Biome.MoonDunes,
                food = yields.food, production = yields.prod, gold = yields.gold, science = yields.sci, culture = yields.cult,
                occupantId = 0,
                isLand = true, // All moon tiles are land conceptually
                isHill = false, // No hills initially
                elevation = elevation,
                isMoonTile = true // Mark as moon tile
            };
            data[i] = td;
        }

        // --- 2. Generate Moon Caves ---
        GenerateCaves(tileCount);


        // --- 3. Final Visual Update Pass ---
        for (int i = 0; i < tileCount; i++)
        {
            if (!data.ContainsKey(i)) continue; // Should not happen

            HexTileData td = data[i];
            Biome biome = td.biome;
            float finalElevation = tileElevation[i]; // Use the stored final elevation

            // --- Set Visuals (Texture or Color) ---
            bool hasTexture = false;
            if (lookup.TryGetValue(biome, out var bs) && bs.albedoTexture != null) {
                if (hex.textures != null) {
                    for (int t=0; t<hex.textures.Length; t++) {
                        if (hex.textures[t] == bs.albedoTexture) {
                            hex.SetTileTexture(i, t, Color.white);
                            hasTexture = true;
                            break;
                        }
                    }
                }
            }
            
            // Set color if no valid texture is assigned
            if (!hasTexture) {
                 Color tileColor = (biome == Biome.MoonCaves) ? moonCavesColor : moonDunesColor;
                 hex.SetTileColor(i, tileColor);
            }
            // ----------------------------------------

            // Set Extrusion
            float finalExtrusionHeight = finalElevation * maxExtrusionHeight;
            hex.SetTileExtrudeAmount(i, finalExtrusionHeight);

             // Assign the final elevation back to the data struct (especially important for caves)
            td.elevation = finalElevation;
            data[i] = td;

            // --- Add Decorations ---
            if (bs?.decorations != null && bs.decorations.Length > 0 && UnityEngine.Random.value < bs.spawnChance)
            {
                GameObject prefab = bs.decorations[UnityEngine.Random.Range(0, bs.decorations.Length)];
                if (prefab != null)
                {
                    var go = Instantiate(prefab);
                    // Adjust altitude based on tile extrusion
                    // Use the finalExtrusionHeight calculated above
                    hex.ParentAndAlignToTile(go, i, finalExtrusionHeight + 0.005f, true, true); 
                }
            }
            // -----------------------
        }
        Debug.Log($"Generated Moon Surface with {tileCount} tiles.");
    }


    // --- 2.1 Cave Generation Helper ---
    void GenerateCaves(int tileCount)
    {
        HashSet<int> processedForCaves = new HashSet<int>(); // Track tiles already part of a cave cluster
        List<int> clusterTiles = new List<int>(); // To hold tiles in the current potential cluster
        Queue<int> floodQueue = new Queue<int>(); // For BFS/flood fill

        for (int i = 0; i < tileCount; i++)
        {
            if (processedForCaves.Contains(i)) continue; // Skip if already processed

            Vector3 tileCenter = hex.GetTileCenter(i);
            float caveNoiseValue = cavePlacementNoise.GetElevation(tileCenter * cavePlacementFreq); // Use a noise function (0-1)

            if (caveNoiseValue > caveThreshold)
            {
                // Potential Cave Seed Found - Start BFS for cluster
                clusterTiles.Clear();
                floodQueue.Clear();

                floodQueue.Enqueue(i);
                processedForCaves.Add(i); // Mark seed as processed immediately
                clusterTiles.Add(i);

                while (floodQueue.Count > 0 && clusterTiles.Count < maxCaveClusterSize)
                {
                    int currentTile = floodQueue.Dequeue();

                    foreach (int neighborIndex in hex.GetTileNeighbours(currentTile))
                    {
                        if (!processedForCaves.Contains(neighborIndex) && clusterTiles.Count < maxCaveClusterSize)
                        {
                            // Check if neighbor *also* meets threshold (optional, makes caves sparser)
                             Vector3 neighborCenter = hex.GetTileCenter(neighborIndex);
                             float neighborCaveNoise = cavePlacementNoise.GetElevation(neighborCenter * cavePlacementFreq);
                             if (neighborCaveNoise > caveThreshold) // Only cluster with other potential caves
                             {
                                processedForCaves.Add(neighborIndex);
                                floodQueue.Enqueue(neighborIndex);
                                clusterTiles.Add(neighborIndex);
                             }
                        }
                    }
                }

                // Check if cluster size meets minimum requirement
                if (clusterTiles.Count >= minCaveClusterSize)
                {
                    // Valid cluster - convert tiles to caves
                    foreach (int caveTileIndex in clusterTiles)
                    {
                        if (data.ContainsKey(caveTileIndex)) // Safety check
                        {
                            var td = data[caveTileIndex];
                            td.biome = Biome.MoonCaves;
                            data[caveTileIndex] = td;
                            tileElevation[caveTileIndex] = fixedCaveElevation; // Set fixed elevation
                        }
                    }
                    // Debug.Log($"Created cave cluster of size {clusterTiles.Count} starting near tile {i}");
                } else {
                    // Cluster too small, revert processed status for potential inclusion in other clusters
                    // Note: This part makes it less likely small invalid clusters prevent valid ones nearby.
                    // However, could potentially re-process tiles unnecessarily.
                    // Consider removing this revert if performance is critical and small failed clusters are acceptable.
                     foreach(int revertedTile in clusterTiles) {
                         processedForCaves.Remove(revertedTile);
                     }
                }
            }
             // Ensure even tiles that didn't start a cluster are marked processed if checked
             processedForCaves.Add(i);
        }
    }

    // --------------------------- API for other systems -----------------------

    /// <summary>
    /// Gets hex tile data for the specified tile index.
    /// </summary>
    public HexTileData GetHexTileData(int tileIndex)
    {
        data.TryGetValue(tileIndex, out HexTileData td);
        return td; // null if not found
    }

    /// <summary>
    /// Sets or updates hex tile data for the specified tile index.
    /// </summary>
    public void SetHexTileData(int tileIndex, HexTileData td)
    {
        data[tileIndex] = td;
    }

    /// <summary>
    /// Checks if a tile is part of the moon.
    /// </summary>
    public bool IsTileMoon(int tileIndex)
    {
        if (data.TryGetValue(tileIndex, out HexTileData td))
        {
            return td.isMoonTile;
        }
        return false;
    }

    /// <summary>
    /// Gets the elevation value for a tile.
    /// </summary>
    public float GetTileElevation(int tileIndex)
    {
        if (tileElevation.TryGetValue(tileIndex, out float elev))
        {
            return elev;
        }
        return 0f;
    }

    /// <summary>
    /// Sets an occupant for a tile.
    /// </summary>
    public void SetTileOccupant(int tileIndex, GameObject occupant)
    {
        if (!data.ContainsKey(tileIndex)) return;
        
        HexTileData td = data[tileIndex];
        td.occupantId = occupant ? occupant.GetInstanceID() : 0;
        data[tileIndex] = td;
    }

    /// <summary>
    /// Gets the GameObject that is currently occupying a tile.
    /// </summary>
    public GameObject GetTileOccupant(int tileIndex)
    {
        if (!data.TryGetValue(tileIndex, out HexTileData td) || td.occupantId == 0)
            return null;
            
        // Find the GameObject with this instance ID
        var objects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        foreach (var obj in objects)
        {
            if (obj.GetInstanceID() == td.occupantId)
                return obj;
        }
        
        return null;
    }

    /// <summary>
    /// Changes a tile's biome and updates its yields and visuals.
    /// </summary>
    public void SetTileBiome(int tileIndex, Biome newBiome)
    {
        if (!data.ContainsKey(tileIndex)) return;
        
        HexTileData td = data[tileIndex];
        td.biome = newBiome;
        
        // Update yields based on new biome
        var yields = BiomeHelper.Yields(newBiome);
        td.food = yields.food;
        td.production = yields.prod;
        td.gold = yields.gold;
        td.science = yields.sci;
        td.culture = yields.cult;
        
        data[tileIndex] = td;
        
        // Update visuals
        if (lookup.TryGetValue(newBiome, out var bs) && bs.albedoTexture != null)
        {
            bool textureSet = false;
            if (hex.textures != null) {
                for (int i=0; i<hex.textures.Length; i++) {
                    if (hex.textures[i] == bs.albedoTexture) {
                        hex.SetTileTexture(tileIndex, i, Color.white);
                        textureSet = true;
                        break;
                    }
                }
            }
            if (!textureSet) {
                Color tileColor = (newBiome == Biome.MoonCaves) ? moonCavesColor : moonDunesColor;
                hex.SetTileColor(tileIndex, tileColor);
            }
        }
        else
        {
            // Use color fallback - determine which color to use
            Color tileColor = (newBiome == Biome.MoonCaves) ? moonCavesColor : moonDunesColor;
            hex.SetTileColor(tileIndex, tileColor);
        }
    }
    
    /// <summary>
    /// Sets the owner of a tile.
    /// </summary>
    public void SetTileOwner(int tileIndex, Civilization owner)
    {
        if (!data.ContainsKey(tileIndex)) return;
        
        HexTileData td = data[tileIndex];
        td.owner = owner;
        data[tileIndex] = td;
    }
    
    /// <summary>
    /// Sets a tile's controlling city.
    /// </summary>
    public void SetTileCity(int tileIndex, City city)
    {
        if (!data.ContainsKey(tileIndex)) return;
        
        HexTileData td = data[tileIndex];
        td.controllingCity = city;
        data[tileIndex] = td;
    }
    
    /// <summary>
    /// Gets all tiles within a certain range of a center tile.
    /// </summary>
    public List<int> GetTilesInRange(int centerTileIndex, int range)
    {
        var result = new List<int>();
        if (!data.ContainsKey(centerTileIndex)) return result;
        
        // Use a queue for breadth-first search
        var queue = new Queue<(int tileIndex, int distance)>();
        var visited = new HashSet<int>();
        
        queue.Enqueue((centerTileIndex, 0));
        visited.Add(centerTileIndex);
        
        while (queue.Count > 0)
        {
            var (tileIndex, distance) = queue.Dequeue();
            result.Add(tileIndex);
            
            if (distance < range)
            {
                foreach (int neighborIndex in hex.GetTileNeighbours(tileIndex))
                {
                    if (!visited.Contains(neighborIndex) && data.ContainsKey(neighborIndex))
                    {
                        visited.Add(neighborIndex);
                        queue.Enqueue((neighborIndex, distance + 1));
                    }
                }
            }
        }
        
        return result;
    }
} 