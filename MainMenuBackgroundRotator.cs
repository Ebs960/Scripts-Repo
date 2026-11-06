using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Main Menu consumer of the shared historical background selection. Picks one background per menu
/// instance from <see cref="HistoricalBackgroundSelector"/> and keeps it covering its viewport.
/// </summary>
[DisallowMultipleComponent]
public class MainMenuBackgroundRotator : MonoBehaviour
{
    [Tooltip("Shared artwork catalog. Assign the same asset to the loading screen so both share one selection history.")]
    [SerializeField] private HistoricalBackgroundCatalog catalog;
    [Tooltip("Legacy per-component sprites. Still honoured (appended after the catalog); prefer moving them into the catalog asset.")]
    [SerializeField] private List<Sprite> backgroundSprites = new List<Sprite>();
    [SerializeField] private Image backgroundImage;
    [Tooltip("Legacy fallback. The catalog's fallback takes precedence when assigned.")]
    [SerializeField] private Sprite fallbackSprite;
    [SerializeField] private string persistenceKey = HistoricalBackgroundSelector.DefaultPersistenceKey;

    private bool initialized;
    private bool fitting;

    public Sprite CurrentSprite => backgroundImage != null ? backgroundImage.sprite : null;

    private void Start()
    {
        Initialize(null);
    }

    public void Initialize(Image target)
    {
        if (backgroundImage == null) backgroundImage = target;
        if (initialized || backgroundImage == null) return;
        initialized = true;

        var sprites = HistoricalBackgroundSelector.BuildCatalog(catalog, backgroundSprites);
        WarnIfLegacySpritesDiverge(sprites);
        Sprite fallback = HistoricalBackgroundSelector.ResolveFallback(catalog, fallbackSprite);
        Sprite selected = HistoricalBackgroundSelector.Draw(sprites, fallback, persistenceKey);

        HistoricalBackgroundSelector.ApplyBackground(backgroundImage, selected);
        UpdateCover();
    }

    // Shared history only stays continuous when both screens see the identical catalog.
    private void WarnIfLegacySpritesDiverge(List<Sprite> effective)
    {
        if (catalog == null)
        {
            if (effective.Count > 0)
                Debug.LogWarning("[MainMenuBackgroundRotator] No HistoricalBackgroundCatalog assigned. The loading screen cannot share this background history until the same catalog asset is assigned to both.", this);
            return;
        }
        int catalogCount = HistoricalBackgroundSelector.BuildCatalog(catalog, null).Count;
        if (effective.Count != catalogCount)
            Debug.LogWarning("[MainMenuBackgroundRotator] Legacy backgroundSprites contain artwork missing from the shared catalog; move them into the catalog asset so the Main Menu and loading screen share one shuffle history.", this);
    }

    private void OnEnable()
    {
        UpdateCover();
    }

    private void OnRectTransformDimensionsChange()
    {
        UpdateCover();
    }

    private void UpdateCover()
    {
        if (fitting || backgroundImage == null || backgroundImage.sprite == null) return;
        fitting = true;
        HistoricalBackgroundSelector.FitCover(backgroundImage, backgroundImage.rectTransform.parent as RectTransform);
        fitting = false;
    }

    public static Vector2 CalculateCoverSize(Vector2 viewport, Vector2 artwork)
    {
        return HistoricalBackgroundSelector.CalculateCoverSize(viewport, artwork);
    }
}
