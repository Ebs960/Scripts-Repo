using UnityEngine;

[CreateAssetMenu(fileName = "MenuPlanetVisualPreset", menuName = "Menu/Planet Visual Preset")]
public class MenuPlanetVisualPreset : ScriptableObject
{
    public string presetName = "Default";
    public Color oceanDeepColor = new Color(0.03f, 0.12f, 0.28f, 1f);
    public Color oceanShallowColor = new Color(0.10f, 0.28f, 0.45f, 1f);
    public Color equatorialColor = new Color(0.01f, 0.30f, 0.04f, 1f);
    public Color desertSand = new Color(0.96f, 0.89f, 0.65f, 1f);
    public Color subtropicalColor = new Color(0.82f, 0.70f, 0.25f, 1f);
    public Color temperateColor = new Color(0.14f, 0.68f, 0.12f, 1f);
    public Color borealColor = new Color(0.06f, 0.25f, 0.10f, 1f);
    public Color tundraColor = new Color(0.58f, 0.50f, 0.38f, 1f);
    public Color polarColor = new Color(0.93f, 0.95f, 0.97f, 1f);
    public Color mountainColor = new Color(0.72f, 0.58f, 0.38f, 1f);
    public Color atmosphereColor = new Color(0.62f, 0.78f, 0.95f, 1f);
    public Texture2D landDetailTexture;
    public Texture2D mountainDetailTexture;
    public Texture2D iceDetailTexture;
    public Texture2D oceanDetailTexture;
    public Texture2D oceanNormalTexture;
    public Texture2D cloudNoiseTexture;
    [Range(0f, 1f)] public float landDetailStrength = 0.18f;
    [Range(0f, 1f)] public float oceanNormalStrength = 0.35f;
    [Range(0f, 1f)] public float cloudDensity = 0.55f;
    [Range(0f, 3f)] public float atmosphereIntensity = 1.2f;
    [Range(0.5f, 3f)] public float brightness = 1.4f;
    [Range(0.5f, 2f)] public float colorVibrancy = 1.3f;
    [Range(0f, 1f)] public float landSmoothness = 0.28f;
    [Range(0f, 1f)] public float oceanSmoothness = 0.82f;
    [Range(0f, 2f)] public float oceanSpecularStrength = 1.0f;
}
