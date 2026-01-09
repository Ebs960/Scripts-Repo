using UnityEngine;

public class NoiseSampler
{
    FastNoiseLite continentNoise;
    FastNoiseLite elevationNoise;
    FastNoiseLite moistNoise;
    FastNoiseLite coastlineNoise; // New noise generator specifically for coastlines
    FastNoiseLite temperatureNoise; // New noise generator for temperature variation

    public NoiseSampler(int seed)
    {
        // ---------- Continents ----------
        continentNoise = new FastNoiseLite(seed + 1);
        continentNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        continentNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        continentNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        continentNoise.SetFractalOctaves(7);
        continentNoise.SetFractalLacunarity(3.5f);
        continentNoise.SetFractalGain(0.4f);
        continentNoise.SetFrequency(0.15f);  

        // ---------- Elevation ----------
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
        
        // ---------- Coastline ----------
        coastlineNoise = new FastNoiseLite(seed + 4);
        coastlineNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        coastlineNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        coastlineNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        coastlineNoise.SetFractalOctaves(2);
        coastlineNoise.SetFractalLacunarity(3.0f);
        coastlineNoise.SetFractalGain(0.6f);
        coastlineNoise.SetFrequency(2.5f);  // Higher frequency for fine detail
        
        // ---------- Temperature ----------
        temperatureNoise = new FastNoiseLite(seed + 5);
        temperatureNoise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2S);
        temperatureNoise.SetRotationType3D(FastNoiseLite.RotationType3D.ImproveXYPlanes);
        temperatureNoise.SetFractalType(FastNoiseLite.FractalType.FBm);
        temperatureNoise.SetFractalOctaves(3);
        temperatureNoise.SetFractalLacunarity(2.0f);
        temperatureNoise.SetFractalGain(0.5f);
        temperatureNoise.SetFrequency(1.0f);  // Medium frequency for regional temperature variation
    }

    // ------------ Public helpers -------------
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
}
