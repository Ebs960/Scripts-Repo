using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PlanetConfig", menuName = "Planets/PlanetConfig")]
public class PlanetConfig : ScriptableObject
{
    [Tooltip("Display name used to match this config to generated planets (exact match)")]
    public string planetName;

    [Tooltip("Explicit per-planet supported layers (controls visuals and generation toggles)")]
    public List<GameManager.PlanetLayerConfig> supportedLayers = new List<GameManager.PlanetLayerConfig>();
}
