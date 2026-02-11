using UnityEngine;

public class NoiseSampler
{
    FastNoiseLite elevationNoise;       // Single FBm noise for all elevation
    FastNoiseLite moistNoise;
    FastNoiseLite temperatureNoise;

    public NoiseSampler(int seed)
    {
        // ---------- Elevation (single FBm - handles all terrain) ----------
        elevationNoise = new FastNoiseLite(seed + 2);
        elevationNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        elevationNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        elevationNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        elevationNoise.SetFractalOctaves(5);
        elevationNoise.SetFractalLacunarity(2.0f);
        elevationNoise.SetFractalGain(0.5f);
        elevationNoise.SetFrequency(1.0f);

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
    /// Get elevation with seamless horizontal wrap using single FBm noise.
    /// Returns value in 0..1 range.
    /// </summary>
    public float GetElevationPeriodic(Vector2 tilePos, float mapWidth, float mapHeight, float freq)
    {
        Vector3 periodic = ToPeriodicCoords(tilePos, mapWidth, mapHeight);
        float n = elevationNoise.GetNoise(periodic.x * freq, periodic.y * freq, periodic.z * freq);
        return Mathf.Clamp01((n + 1f) * 0.5f); // Map -1..1 to 0..1
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
