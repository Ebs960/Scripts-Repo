using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "PlanetConfig", menuName = "Planets/Planet Config")]
public class PlanetConfig : ScriptableObject
{
    [Tooltip("Display name used to match this config to generated planets (exact match)")]
    public string planetName;

    [Header("Authoritative Gameplay Layers")]
    [Tooltip("List of layers this planet supports. This is authoritative and controls which systems run.")]
    public List<GameManager.PlanetLayerType> supportedLayers = new List<GameManager.PlanetLayerType>();

    [Header("Optional Visual Data")]
    [Tooltip("If set, used to configure atmosphere-only (gas giant) visuals")]
    public GasGiantVisualData gasGiantVisualData;
}
