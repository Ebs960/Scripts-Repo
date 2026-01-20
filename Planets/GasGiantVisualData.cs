using UnityEngine;

[CreateAssetMenu(menuName = "Planets/Gas Giant Visual Data")]
public class GasGiantVisualData : ScriptableObject
{
    public Texture2D baseGradient;
    public Texture2D noiseTexture;

    public Color tint = Color.white;
    public float bandSharpness = 1f;
    public float stormStrength = 0.5f;
    public float rotationSpeed = 1f;
}
