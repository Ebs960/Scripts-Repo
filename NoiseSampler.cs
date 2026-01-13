using UnityEngine;

public class NoiseSampler
{
    FastNoiseLite continentNoise;
    FastNoiseLite elevationNoise;
    FastNoiseLite elevationBroadNoise;  // New: broad FBm for rolling terrain
    FastNoiseLite elevationRidgedNoise; // New: ridged noise for mountains/spines
    FastNoiseLite elevationBillowNoise; // New: billow noise for rolling hills
    FastNoiseLite moistNoise;
    FastNoiseLite coastlineNoise;
    FastNoiseLite coastlineWarpNoise;   // New: for domain warping coastlines
    FastNoiseLite coastlineWarpFine;    // New: fine-scale coastline warp
    FastNoiseLite temperatureNoise;
    FastNoiseLite islandNoise;          // New: dedicated island noise
    FastNoiseLite voronoiNoise;         // New: Voronoi/cellular noise for natural patterns
    FastNoiseLite domainWarpNoise;      // New: large-scale domain warping for organic shapes
    FastNoiseLite domainWarpFine;       // New: fine-scale domain warping
    
    // Configurable parameters for campaign maps
    public float continentFrequency = 0.008f;      // Derived from map size
    public float coastlineFrequency = 0.024f;      // 3x continent frequency
    public float coastlineWarpAmplitude = 0.15f;   // Controls coastline jaggedness
    public float elevationBroadFrequency = 0.012f; // Broad terrain frequency
    public float elevationRidgedFrequency = 0.02f; // Mountain ridge frequency
    public float ridgeWeight = 0.35f;              // Blend weight for ridged noise
    public float billowWeight = 0.25f;             // Blend weight for billow (rolling hills)
    public float voronoiInfluence = 0.15f;         // How much Voronoi affects terrain

    public NoiseSampler(int seed)
    {
        // ---------- Continents (Macro) ----------
        continentNoise = new FastNoiseLite(seed + 1);
        continentNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        continentNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        continentNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        continentNoise.SetFractalOctaves(5);        // 5 octaves for macro shapes
        continentNoise.SetFractalLacunarity(2.0f);  // Standard lacunarity
        continentNoise.SetFractalGain(0.5f);        // Standard gain
        continentNoise.SetFrequency(0.15f);         // Will be overridden by periodic methods

        // ---------- Elevation (Broad FBm - rolling terrain) ----------
        elevationBroadNoise = new FastNoiseLite(seed + 2);
        elevationBroadNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        elevationBroadNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        elevationBroadNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        elevationBroadNoise.SetFractalOctaves(4);
        elevationBroadNoise.SetFractalLacunarity(2.0f);
        elevationBroadNoise.SetFractalGain(0.5f);
        elevationBroadNoise.SetFrequency(1.0f);
        
        // ---------- Elevation (Ridged - mountains/spines) ----------
        elevationRidgedNoise = new FastNoiseLite(seed + 20);
        elevationRidgedNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        elevationRidgedNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        elevationRidgedNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        elevationRidgedNoise.SetFractalOctaves(4);
        elevationRidgedNoise.SetFractalLacunarity(2.0f);
        elevationRidgedNoise.SetFractalGain(0.5f);
        elevationRidgedNoise.SetFrequency(1.2f);
        
        // ---------- Legacy Elevation (for non-periodic calls) ----------
        elevationNoise = new FastNoiseLite(seed + 2);
        elevationNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        elevationNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        elevationNoise.SetFractalType(FastNoiseLite.FractalType.Ridged);
        elevationNoise.SetFractalOctaves(5);
        elevationNoise.SetFrequency(1.2f);

        // ---------- Moisture ----------
        moistNoise = new FastNoiseLite(seed + 3);
        moistNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        moistNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        moistNoise.SetFractalOctaves(4);
        moistNoise.SetFrequency(0.852f);
        
        // ---------- Coastline Detail ----------
        coastlineNoise = new FastNoiseLite(seed + 4);
        coastlineNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        coastlineNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        coastlineNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        coastlineNoise.SetFractalOctaves(3);        // 3 octaves for coastline
        coastlineNoise.SetFractalLacunarity(2.2f);  // Slightly higher for more detail
        coastlineNoise.SetFractalGain(0.5f);
        coastlineNoise.SetFrequency(2.5f);
        
        // ---------- Coastline Warp (for domain warping) ----------
        coastlineWarpNoise = new FastNoiseLite(seed + 40);
        coastlineWarpNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        coastlineWarpNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        coastlineWarpNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        coastlineWarpNoise.SetFractalOctaves(2);
        coastlineWarpNoise.SetFractalLacunarity(2.0f);
        coastlineWarpNoise.SetFractalGain(0.5f);
        coastlineWarpNoise.SetFrequency(4.0f);
        
        // ---------- Temperature ----------
        temperatureNoise = new FastNoiseLite(seed + 5);
        temperatureNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        temperatureNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        temperatureNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        temperatureNoise.SetFractalOctaves(3);
        temperatureNoise.SetFractalLacunarity(2.0f);
        temperatureNoise.SetFractalGain(0.5f);
        temperatureNoise.SetFrequency(1.0f);
        
        // ---------- Island Noise (higher frequency) ----------
        islandNoise = new FastNoiseLite(seed + 6);
        islandNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        islandNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        islandNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        islandNoise.SetFractalOctaves(4);
        islandNoise.SetFractalLacunarity(2.0f);
        islandNoise.SetFractalGain(0.5f);
        islandNoise.SetFrequency(0.25f);  // Higher than continents for smaller features
        
        // ---------- Billow Noise (rolling hills - inverse of ridged) ----------
        elevationBillowNoise = new FastNoiseLite(seed + 21);
        elevationBillowNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        elevationBillowNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        elevationBillowNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        elevationBillowNoise.SetFractalOctaves(4);
        elevationBillowNoise.SetFractalLacunarity(2.2f);
        elevationBillowNoise.SetFractalGain(0.45f);
        elevationBillowNoise.SetFrequency(0.8f);
        
        // ---------- Fine-Scale Coastline Warp ----------
        coastlineWarpFine = new FastNoiseLite(seed + 41);
        coastlineWarpFine.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        coastlineWarpFine.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        coastlineWarpFine.SetFractalType(FastNoiseLite.FractalType.FBm);
        coastlineWarpFine.SetFractalOctaves(3);
        coastlineWarpFine.SetFractalLacunarity(2.5f);
        coastlineWarpFine.SetFractalGain(0.4f);
        coastlineWarpFine.SetFrequency(8.0f);  // Much higher frequency for fine detail
        
        // ---------- Voronoi/Cellular Noise (natural patterns) ----------
        voronoiNoise = new FastNoiseLite(seed + 7);
        voronoiNoise.SetNoiseType(FastNoiseLite.NoiseType.Cellular);
        voronoiNoise.SetCellularDistanceFunction(FastNoiseLite.CellularDistanceFunction.EuclideanSq);
        voronoiNoise.SetCellularReturnType(FastNoiseLite.CellularReturnType.Distance2Sub); // Creates natural cracks/cell patterns
        voronoiNoise.SetCellularJitter(0.85f);  // Some randomness in cell positions
        voronoiNoise.SetFrequency(0.05f);
        
        // ---------- Large-Scale Domain Warp (organic continent shapes) ----------
        domainWarpNoise = new FastNoiseLite(seed + 8);
        domainWarpNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        domainWarpNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        domainWarpNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        domainWarpNoise.SetFractalOctaves(3);
        domainWarpNoise.SetFractalLacunarity(2.0f);
        domainWarpNoise.SetFractalGain(0.5f);
        domainWarpNoise.SetFrequency(0.5f);  // Low frequency for large-scale warping
        
        // ---------- Fine-Scale Domain Warp ----------
        domainWarpFine = new FastNoiseLite(seed + 9);
        domainWarpFine.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        domainWarpFine.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        domainWarpFine.SetFractalType(FastNoiseLite.FractalType.FBm);
        domainWarpFine.SetFractalOctaves(2);
        domainWarpFine.SetFractalLacunarity(2.0f);
        domainWarpFine.SetFractalGain(0.5f);
        domainWarpFine.SetFrequency(2.0f);  // Higher frequency for detail
    }
    
    /// <summary>
    /// Configure noise frequencies based on map dimensions.
    /// Call this after construction to set up proper periodic sampling.
    /// </summary>
    public void ConfigureForMapSize(float mapWidth, float mapHeight)
    {
        // Continent frequency: roughly 1 full wave per 0.75 of map width
        continentFrequency = 1f / (mapWidth * 0.75f);
        // Coastline: 3-6x continent frequency for detail
        coastlineFrequency = continentFrequency * 4f;
        // Elevation frequencies
        elevationBroadFrequency = continentFrequency * 1.5f;
        elevationRidgedFrequency = continentFrequency * 2.5f;
    }

    // ------------ Public helpers (legacy non-periodic) -------------
    public float GetContinent(Vector3 p)
    {
        float n = continentNoise.GetNoise(p.x, p.y, p.z);
        n = Mathf.Sign(n) * Mathf.Pow(Mathf.Abs(n), 0.95f);   // sharpen edges
        return (n + 1f) * 0.5f;                              // map to 0‒1
    }

    public float GetElevation(Vector3 p) =>
        Mathf.InverseLerp(-1.2f, 1.2f, elevationNoise.GetNoise(p.x, p.y, p.z));

    public float GetElevationRaw(Vector3 p) =>
        elevationNoise.GetNoise(p.x, p.y, p.z);

    public float GetMoisture(Vector3 p) =>
        Mathf.InverseLerp(-1f, 1f, moistNoise.GetNoise(p.x, p.y, p.z));
        
    // New method for coastline noise (0-1 range)
    public float GetCoastlineNoise(Vector3 p) =>
        Mathf.InverseLerp(-1f, 1f, coastlineNoise.GetNoise(p.x, p.y, p.z));

    // Get temperature from spatial noise (0-1 range) - for non-polar areas
    public float GetTemperatureFromNoise(Vector3 p) =>
        Mathf.InverseLerp(-1f, 1f, temperatureNoise.GetNoise(p.x, p.y, p.z));

    // absLatitude in 0‒1 (equator = 0, pole = 1) - for polar areas only
    public float GetTemperature(float absLatitude)
    {
        float t = 1f - absLatitude;         // 1 = hot, 0 = cold
        t = Mathf.Pow(t, 1.0f);             // linear: stronger north/south effect (was 0.6f)
        // Add subtle noise to soften zone boundaries
        float noise = moistNoise.GetNoise(absLatitude * 100f, 0f, 0f) * 0.07f; // Range ~[-0.07, +0.07]
        t += noise;
        return Mathf.Clamp01(t);
    }
    
    // ============================================================================
    // PERIODIC / WRAP-SAFE SAMPLING (Campaign Map)
    // ============================================================================
    // These methods use cylindrical mapping to ensure seamless horizontal wrap.
    // X is mapped to a circle (cos/sin of angle), Y remains linear.
    // This guarantees that noise at x=0 matches noise at x=mapWidth.
    // ============================================================================
    
    /// <summary>
    /// Convert flat map position to periodic 3D coordinates.
    /// X wraps seamlessly, Y (map vertical) is linear.
    /// </summary>
    private Vector3 ToPeriodicCoords(Vector2 tilePos, float mapWidth, float mapHeight, float radiusScale = 1f)
    {
        // Normalize X to 0..1 range
        float x01 = (tilePos.x + mapWidth * 0.5f) / mapWidth;
        x01 = Mathf.Repeat(x01, 1f);  // Ensure wrapping
        
        // Convert to angle (0 to 2π)
        float theta = x01 * Mathf.PI * 2f;
        
        // Map X to circle coordinates, scaled by mapWidth for proper frequency
        float radius = mapWidth * 0.5f * radiusScale;
        float cx = Mathf.Cos(theta) * radius;
        float cy = Mathf.Sin(theta) * radius;
        
        // Y (map vertical) stays linear
        float z = tilePos.y;
        
        return new Vector3(cx, cy, z);
    }
    
    /// <summary>
    /// Get continent noise with seamless horizontal wrap (periodic in X).
    /// Returns value in 0..1 range.
    /// </summary>
    public float GetContinentPeriodic(Vector2 tilePos, float mapWidth, float mapHeight, float freq)
    {
        Vector3 periodic = ToPeriodicCoords(tilePos, mapWidth, mapHeight);
        float n = continentNoise.GetNoise(periodic.x * freq, periodic.y * freq, periodic.z * freq);
        n = Mathf.Sign(n) * Mathf.Pow(Mathf.Abs(n), 0.95f);  // Sharpen edges
        return (n + 1f) * 0.5f;  // Map to 0..1
    }
    
    /// <summary>
    /// Get coastline detail noise with seamless horizontal wrap.
    /// Returns value in 0..1 range.
    /// </summary>
    public float GetCoastPeriodic(Vector2 tilePos, float mapWidth, float mapHeight, float freq)
    {
        Vector3 periodic = ToPeriodicCoords(tilePos, mapWidth, mapHeight);
        float n = coastlineNoise.GetNoise(periodic.x * freq, periodic.y * freq, periodic.z * freq);
        return (n + 1f) * 0.5f;
    }
    
    /// <summary>
    /// Get coastline warp offset with seamless horizontal wrap.
    /// Returns value centered around 0 (positive or negative offset).
    /// </summary>
    public Vector2 GetCoastWarpPeriodic(Vector2 tilePos, float mapWidth, float mapHeight, float freq, float amplitude)
    {
        Vector3 periodic = ToPeriodicCoords(tilePos, mapWidth, mapHeight);
        // Sample two different positions for X and Y warp
        float warpX = coastlineWarpNoise.GetNoise(periodic.x * freq, periodic.y * freq, periodic.z * freq);
        float warpY = coastlineWarpNoise.GetNoise(periodic.x * freq + 100f, periodic.y * freq + 100f, periodic.z * freq + 100f);
        return new Vector2(warpX * amplitude, warpY * amplitude);
    }
    
    /// <summary>
    /// Get blended elevation with seamless horizontal wrap.
    /// Blends broad FBm (rolling terrain) with ridged noise (mountains).
    /// Returns value in 0..1 range.
    /// </summary>
    public float GetElevationPeriodic(Vector2 tilePos, float mapWidth, float mapHeight, float broadFreq, float ridgedFreq, float ridgeBlend = 0.35f)
    {
        Vector3 periodic = ToPeriodicCoords(tilePos, mapWidth, mapHeight);
        
        // Broad rolling terrain (FBm)
        float broad = elevationBroadNoise.GetNoise(periodic.x * broadFreq, periodic.y * broadFreq, periodic.z * broadFreq);
        broad = (broad + 1f) * 0.5f;  // Map to 0..1
        
        // Ridged mountains/spines
        float ridged = elevationRidgedNoise.GetNoise(periodic.x * ridgedFreq, periodic.y * ridgedFreq, periodic.z * ridgedFreq);
        ridged = Mathf.InverseLerp(-1.2f, 1.2f, ridged);  // Map to ~0..1
        
        // Blend: mostly broad with some ridged character
        float blended = Mathf.Lerp(broad, ridged, ridgeBlend);
        return Mathf.Clamp01(blended);
    }
    
    /// <summary>
    /// Get island noise with seamless horizontal wrap.
    /// Higher frequency than continents for smaller, more detailed features.
    /// </summary>
    public float GetIslandNoisePeriodic(Vector2 tilePos, float mapWidth, float mapHeight, float freq)
    {
        Vector3 periodic = ToPeriodicCoords(tilePos, mapWidth, mapHeight);
        float n = islandNoise.GetNoise(periodic.x * freq, periodic.y * freq, periodic.z * freq);
        return (n + 1f) * 0.5f;
    }
    
    /// <summary>
    /// Get moisture with seamless horizontal wrap.
    /// </summary>
    public float GetMoisturePeriodic(Vector2 tilePos, float mapWidth, float mapHeight, float freq)
    {
        Vector3 periodic = ToPeriodicCoords(tilePos, mapWidth, mapHeight);
        float n = moistNoise.GetNoise(periodic.x * freq, periodic.y * freq, periodic.z * freq);
        return (n + 1f) * 0.5f;
    }
    
    /// <summary>
    /// Get temperature noise with seamless horizontal wrap.
    /// </summary>
    public float GetTemperaturePeriodic(Vector2 tilePos, float mapWidth, float mapHeight, float freq)
    {
        Vector3 periodic = ToPeriodicCoords(tilePos, mapWidth, mapHeight);
        float n = temperatureNoise.GetNoise(periodic.x * freq, periodic.y * freq, periodic.z * freq);
        return (n + 1f) * 0.5f;
    }
    
    // ============================================================================
    // ADVANCED FRACTAL METHODS - Voronoi, Domain Warping, Billow
    // ============================================================================
    
    /// <summary>
    /// Get Voronoi/cellular noise with seamless horizontal wrap.
    /// Returns value in 0..1 range. Creates natural cell-like patterns.
    /// </summary>
    public float GetVoronoiPeriodic(Vector2 tilePos, float mapWidth, float mapHeight, float freq)
    {
        Vector3 periodic = ToPeriodicCoords(tilePos, mapWidth, mapHeight);
        float n = voronoiNoise.GetNoise(periodic.x * freq, periodic.y * freq, periodic.z * freq);
        // Cellular noise returns -1 to 1, map to 0..1
        return (n + 1f) * 0.5f;
    }
    
    /// <summary>
    /// Get Voronoi distance field - useful for continent clustering and archipelago generation.
    /// Returns raw distance value (higher = further from cell center).
    /// </summary>
    public float GetVoronoiDistance(Vector2 tilePos, float mapWidth, float mapHeight, float freq)
    {
        Vector3 periodic = ToPeriodicCoords(tilePos, mapWidth, mapHeight);
        // Temporarily switch to distance return type
        voronoiNoise.SetCellularReturnType(FastNoiseLite.CellularReturnType.Distance);
        float dist = voronoiNoise.GetNoise(periodic.x * freq, periodic.y * freq, periodic.z * freq);
        // Reset to default pattern
        voronoiNoise.SetCellularReturnType(FastNoiseLite.CellularReturnType.Distance2Sub);
        return (dist + 1f) * 0.5f;
    }
    
    /// <summary>
    /// Get billow noise with seamless horizontal wrap.
    /// Billow = abs(noise), creates rounded, puffy, cloud-like shapes.
    /// Great for rolling hills and softer terrain.
    /// </summary>
    public float GetBillowPeriodic(Vector2 tilePos, float mapWidth, float mapHeight, float freq)
    {
        Vector3 periodic = ToPeriodicCoords(tilePos, mapWidth, mapHeight);
        float n = elevationBillowNoise.GetNoise(periodic.x * freq, periodic.y * freq, periodic.z * freq);
        // Billow: take absolute value to create rounded bumps
        return Mathf.Abs(n);
    }
    
    /// <summary>
    /// Multi-octave cascaded coastline warping for highly fractal coastlines.
    /// Combines coarse and fine warping for natural fjord/peninsula shapes.
    /// </summary>
    public Vector2 GetCascadedCoastWarp(Vector2 tilePos, float mapWidth, float mapHeight, 
        float coarseFreq, float fineFreq, float coarseAmp, float fineAmp)
    {
        Vector3 periodic = ToPeriodicCoords(tilePos, mapWidth, mapHeight);
        
        // Coarse-scale warp (large bays, peninsulas)
        float coarseX = coastlineWarpNoise.GetNoise(periodic.x * coarseFreq, periodic.y * coarseFreq, periodic.z * coarseFreq);
        float coarseY = coastlineWarpNoise.GetNoise(periodic.x * coarseFreq + 100f, periodic.y * coarseFreq + 100f, periodic.z * coarseFreq + 100f);
        
        // Apply coarse warp to get intermediate position
        Vector2 warpedPos = tilePos + new Vector2(coarseX * coarseAmp, coarseY * coarseAmp);
        Vector3 warpedPeriodic = ToPeriodicCoords(warpedPos, mapWidth, mapHeight);
        
        // Fine-scale warp (small inlets, detail)
        float fineX = coastlineWarpFine.GetNoise(warpedPeriodic.x * fineFreq, warpedPeriodic.y * fineFreq, warpedPeriodic.z * fineFreq);
        float fineY = coastlineWarpFine.GetNoise(warpedPeriodic.x * fineFreq + 50f, warpedPeriodic.y * fineFreq + 50f, warpedPeriodic.z * fineFreq + 50f);
        
        // Combine both scales
        return new Vector2(
            coarseX * coarseAmp + fineX * fineAmp,
            coarseY * coarseAmp + fineY * fineAmp
        );
    }
    
    /// <summary>
    /// Domain warp a position using cascaded multi-scale warping.
    /// Creates highly organic, flowing continent shapes.
    /// Returns the warped position.
    /// </summary>
    public Vector2 GetDomainWarpedPosition(Vector2 tilePos, float mapWidth, float mapHeight, 
        float largeAmp, float smallAmp)
    {
        Vector3 periodic = ToPeriodicCoords(tilePos, mapWidth, mapHeight);
        float freqLarge = 1f / (mapWidth * 0.4f);  // Very low frequency for large-scale warp
        float freqSmall = 1f / (mapWidth * 0.15f); // Medium frequency for detail
        
        // Large-scale organic flow
        float warpX1 = domainWarpNoise.GetNoise(periodic.x * freqLarge, periodic.y * freqLarge, periodic.z * freqLarge);
        float warpY1 = domainWarpNoise.GetNoise(periodic.x * freqLarge + 200f, periodic.y * freqLarge + 200f, periodic.z * freqLarge + 200f);
        
        // Apply first warp
        Vector2 warped1 = tilePos + new Vector2(warpX1 * largeAmp, warpY1 * largeAmp);
        Vector3 warpedPeriodic = ToPeriodicCoords(warped1, mapWidth, mapHeight);
        
        // Smaller-scale detail warp
        float warpX2 = domainWarpFine.GetNoise(warpedPeriodic.x * freqSmall, warpedPeriodic.y * freqSmall, warpedPeriodic.z * freqSmall);
        float warpY2 = domainWarpFine.GetNoise(warpedPeriodic.x * freqSmall + 150f, warpedPeriodic.y * freqSmall + 150f, warpedPeriodic.z * freqSmall + 150f);
        
        // Final warped position
        return warped1 + new Vector2(warpX2 * smallAmp, warpY2 * smallAmp);
    }
    
    /// <summary>
    /// Get elevation using advanced multi-noise blending.
    /// Combines: FBm (base), Ridged (mountains), Billow (hills), Voronoi (variation).
    /// </summary>
    public float GetAdvancedElevationPeriodic(Vector2 tilePos, float mapWidth, float mapHeight,
        float broadFreq, float ridgedFreq, float billowFreq, float voronoiFreq,
        float ridgeBlend = 0.35f, float billowBlend = 0.25f, float voronoiBlend = 0.15f)
    {
        Vector3 periodic = ToPeriodicCoords(tilePos, mapWidth, mapHeight);
        
        // Base broad terrain (FBm)
        float broad = elevationBroadNoise.GetNoise(periodic.x * broadFreq, periodic.y * broadFreq, periodic.z * broadFreq);
        broad = (broad + 1f) * 0.5f;
        
        // Ridged mountains/spines
        float ridged = elevationRidgedNoise.GetNoise(periodic.x * ridgedFreq, periodic.y * ridgedFreq, periodic.z * ridgedFreq);
        ridged = Mathf.InverseLerp(-1.2f, 1.2f, ridged);
        
        // Billow for rolling hills (abs of noise creates rounded bumps)
        float billow = elevationBillowNoise.GetNoise(periodic.x * billowFreq, periodic.y * billowFreq, periodic.z * billowFreq);
        billow = Mathf.Abs(billow);  // Creates rounded "pillowy" shapes
        
        // Voronoi for natural variation/cells
        float voronoi = voronoiNoise.GetNoise(periodic.x * voronoiFreq, periodic.y * voronoiFreq, periodic.z * voronoiFreq);
        voronoi = (voronoi + 1f) * 0.5f;
        
        // Multi-blend: Start with broad, add others based on weights
        // The weights should sum to ~1.0 for the additive parts
        float baseWeight = 1f - ridgeBlend - billowBlend;
        float blended = broad * baseWeight + ridged * ridgeBlend + billow * billowBlend;
        
        // Voronoi modulates the result (adds subtle cellular variation)
        blended = Mathf.Lerp(blended, blended * (0.7f + voronoi * 0.6f), voronoiBlend);
        
        return Mathf.Clamp01(blended);
    }
    
    /// <summary>
    /// Get continent noise with domain warping for organic shapes.
    /// Much more natural-looking than raw noise.
    /// </summary>
    public float GetWarpedContinentPeriodic(Vector2 tilePos, float mapWidth, float mapHeight, 
        float freq, float warpAmplitude)
    {
        // First apply domain warping to the position
        Vector2 warpedPos = GetDomainWarpedPosition(tilePos, mapWidth, mapHeight, 
            warpAmplitude * mapWidth * 0.15f,  // Large-scale warp
            warpAmplitude * mapWidth * 0.05f); // Fine-scale warp
        
        // Now sample continent noise at warped position
        Vector3 periodic = ToPeriodicCoords(warpedPos, mapWidth, mapHeight);
        float n = continentNoise.GetNoise(periodic.x * freq, periodic.y * freq, periodic.z * freq);
        n = Mathf.Sign(n) * Mathf.Pow(Mathf.Abs(n), 0.95f);  // Sharpen edges
        return (n + 1f) * 0.5f;
    }
    
    // ============================================================================
    // UTILITY FUNCTIONS
    // ============================================================================
    
    /// <summary>
    /// Attempt smoothstep (Hermite interpolation) for smooth falloff.
    /// </summary>
    public static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * (3f - 2f * t);
    }
    
    /// <summary>
    /// Attempt smoother step for even smoother falloff.
    /// </summary>
    public static float SmootherStep(float edge0, float edge1, float x)
    {
        float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
        return t * t * t * (t * (t * 6f - 15f) + 10f);
    }
}
