using TMPro;
using UnityEngine;

/// <summary>Compact world marker showing mobility, starvation, and garrison count.</summary>
public sealed class BandWorldUI : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField] private Color packedColor = Color.white, encampedColor = new Color(.8f, .65f, .4f), starvingColor = Color.red;
    private Band band;
    public void Initialize(Band value) { band = value; Refresh(); }
    public void Refresh()
    {
        if (band == null || label == null) return;
        label.text = $"{(band.State == BandState.Packed ? "PACKED" : "CAMP")}  •  {band.Garrison.Count}";
        label.color = band.IsStarving ? starvingColor : (band.State == BandState.Packed ? packedColor : encampedColor);
    }
}
