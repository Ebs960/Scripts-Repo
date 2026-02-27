using System;
using UnityEngine;

/// <summary>
/// Data-driven mapping of biomes and water types to ambient audio clips.
/// Assign in the Inspector on AmbientSoundManager. All entries are optional —
/// biomes/water types without a clip simply produce silence.
/// </summary>
[CreateAssetMenu(menuName = "Audio/Ambient Sound Database", fileName = "AmbientSoundDatabase")]
public class AmbientSoundDatabase : ScriptableObject
{
    [Serializable]
    public class BiomeAmbientEntry
    {
        public Biome biome;
        [Tooltip("Looping ambient clip for this biome.")]
        public AudioClip clip;
        [Range(0f, 1f)]
        [Tooltip("Base volume before altitude scaling.")]
        public float volume = 0.6f;
    }

    [Serializable]
    public class WaterAmbientEntry
    {
        public TileWaterType waterType;
        [Tooltip("Looping clip that layers on top of the biome ambient when near this water type.")]
        public AudioClip clip;
        [Range(0f, 1f)]
        [Tooltip("Maximum volume when the camera is directly above the water.")]
        public float maxVolume = 0.5f;
        [Tooltip("World-unit radius within which the water sound is audible (blends from max → 0).")]
        public float audibleRadius = 40f;
    }

    [Header("Biome Ambience")]
    [Tooltip("One entry per biome that should have ambient audio. Order doesn't matter.")]
    public BiomeAmbientEntry[] biomeEntries;

    [Header("Water Proximity")]
    [Tooltip("Additive water sounds that layer on top of the biome loop when the camera is near water.")]
    public WaterAmbientEntry[] waterEntries;

    [Header("Distance Scaling")]
    [Tooltip("3D distance (world units) from camera to tile below which audio plays at full volume.")]
    public float nearDistance = 30f;
    [Tooltip("3D distance (world units) from camera to tile above which audio fades to distanceMinVolume.")]
    public float farDistance = 180f;
    [Tooltip("Minimum volume multiplier when camera is at or beyond farDistance.")]
    [Range(0f, 1f)]
    public float distanceMinVolume = 0.15f;

    // ── Runtime helpers ──

    /// <summary>Return the entry for a biome, or null if none configured.</summary>
    public BiomeAmbientEntry GetBiomeEntry(Biome biome)
    {
        if (biomeEntries == null) return null;
        for (int i = 0; i < biomeEntries.Length; i++)
        {
            if (biomeEntries[i].biome == biome) return biomeEntries[i];
        }
        return null;
    }

    /// <summary>Return the entry for a water type, or null.</summary>
    public WaterAmbientEntry GetWaterEntry(TileWaterType wt)
    {
        if (waterEntries == null) return null;
        for (int i = 0; i < waterEntries.Length; i++)
        {
            if (waterEntries[i].waterType == wt) return waterEntries[i];
        }
        return null;
    }

    /// <summary>
    /// Compute a 0-1 volume multiplier based on 3D distance from camera to the tile surface.
    /// Full volume up to nearDistance, then linear fade to distanceMinVolume at farDistance.
    /// </summary>
    public float GetDistanceMultiplier(float distance)
    {
        if (distance <= nearDistance) return 1f;
        if (distance >= farDistance) return distanceMinVolume;
        float t = Mathf.InverseLerp(nearDistance, farDistance, distance);
        return Mathf.Lerp(1f, distanceMinVolume, t);
    }
}
