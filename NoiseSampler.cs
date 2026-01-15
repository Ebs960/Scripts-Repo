using UnityEngine;

public class NoiseSampler
{
    FastNoiseLite elevationNoise;
    FastNoiseLite elevationBroadNoise;  // New: broad FBm for rolling terrain
    FastNoiseLite elevationRidgedNoise; // New: ridged noise for mountains/spines
    FastNoiseLite elevationBillowNoise; // New: billow noise for rolling hills
    FastNoiseLite moistNoise;
    FastNoiseLite temperatureNoise;
    
    // Configurable parameters for campaign maps
    public float elevationBroadFrequency = 0.012f; // Broad terrain frequency
    public float elevationRidgedFrequency = 0.02f; // Mountain ridge frequency

    public NoiseSampler(int seed)
    {
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
        
        // ---------- Temperature ----------
        temperatureNoise = new FastNoiseLite(seed + 5);
        temperatureNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        temperatureNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        temperatureNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        temperatureNoise.SetFractalOctaves(3);
        temperatureNoise.SetFractalLacunarity(2.0f);
        temperatureNoise.SetFractalGain(0.5f);
        temperatureNoise.SetFrequency(1.0f);
        
        // ---------- Billow Noise (rolling hills - inverse of ridged) ----------
        elevationBillowNoise = new FastNoiseLite(seed + 21);
        elevationBillowNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        elevationBillowNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        elevationBillowNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        elevationBillowNoise.SetFractalOctaves(4);
        elevationBillowNoise.SetFractalLacunarity(2.2f);
        elevationBillowNoise.SetFractalGain(0.45f);
        elevationBillowNoise.SetFrequency(0.8f);
        
    }
    
    /// <summary>
    /// Configure noise frequencies based on map dimensions.
    /// Call this after construction to set up proper periodic sampling.
    /// </summary>
    public void ConfigureForMapSize(float mapWidth, float mapHeight)
    {
        // Elevation frequencies
        float baseFrequency = 1f / (mapWidth * 0.75f);
        elevationBroadFrequency = baseFrequency * 1.5f;
        elevationRidgedFrequency = baseFrequency * 2.5f;
    }

    // ------------ Public helpers (legacy non-periodic) -------------
    public float GetElevation(Vector3 p) =>
        Mathf.InverseLerp(-1.2f, 1.2f, elevationNoise.GetNoise(p.x, p.y, p.z));

    public float GetElevationRaw(Vector3 p) =>
        elevationNoise.GetNoise(p.x, p.y, p.z);

    public float GetMoisture(Vector3 p) =>
        Mathf.InverseLerp(-1f, 1f, moistNoise.GetNoise(p.x, p.y, p.z));

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
    /// Get elevation using advanced multi-noise blending.
    /// Combines: FBm (base), Ridged (mountains), Billow (hills).
    /// </summary>
    public float GetAdvancedElevationPeriodic(Vector2 tilePos, float mapWidth, float mapHeight,
        float broadFreq, float ridgedFreq, float billowFreq,
        float ridgeBlend = 0.35f, float billowBlend = 0.25f)
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
        
        // Multi-blend: Start with broad, add others based on weights
        // The weights should sum to ~1.0 for the additive parts
        float baseWeight = 1f - ridgeBlend - billowBlend;
        float blended = broad * baseWeight + ridged * ridgeBlend + billow * billowBlend;

        return Mathf.Clamp01(blended);
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
