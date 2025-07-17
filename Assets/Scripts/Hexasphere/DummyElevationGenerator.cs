/* Implements the minimal surface of IHexasphereGenerator needed by
   HexasphereRenderer.  All extra planet features return defaults. */
using UnityEngine;
using System.Collections.Generic;

public class DummyElevationGenerator : MonoBehaviour, IHexasphereGenerator
{
    public float[] elevations;                // injected by HexasphereTest

    // — IHexasphereGenerator —
    public float GetTileElevation(int i)       => elevations != null && i < elevations.Length ? elevations[i] : 0f;
    public HexTileData GetHexTileData(int i)   => null;       // not needed for this test
    public List<BiomeSettings> GetBiomeSettings() => new();   // empty list satisfies renderer
}
