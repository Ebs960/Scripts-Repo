using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared, ordered collection of historical background artwork used by the Main Menu and the regular
/// loading screen. Both reference the same asset so the artwork list is maintained in one place and the
/// shuffle-bag history (keyed by the catalog signature) stays continuous between the two screens.
/// </summary>
[CreateAssetMenu(fileName = "Historical Background Catalog", menuName = "Caveman to Cosmos/UI/Historical Background Catalog")]
public sealed class HistoricalBackgroundCatalog : ScriptableObject
{
    [Tooltip("Ordered historical artwork. Import textures as Sprite (2D and UI). Null and duplicate entries are ignored at runtime.")]
    [SerializeField] private List<Sprite> backgrounds = new List<Sprite>();

    [Tooltip("Optional sprite shown when the catalog has no valid backgrounds. Leave empty to show the solid navy base.")]
    [SerializeField] private Sprite fallbackSprite;

    public IReadOnlyList<Sprite> Backgrounds => backgrounds;
    public Sprite FallbackSprite => fallbackSprite;
}
