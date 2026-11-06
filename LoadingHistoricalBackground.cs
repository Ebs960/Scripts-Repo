using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Companion to <see cref="LoadingPanelController"/> for the regular loading screen. Draws one historical
/// background per loading session from the shared <see cref="HistoricalBackgroundSelector"/> (same
/// PlayerPrefs history as the Main Menu), keeps it covering the right-hand viewport without stretching,
/// and keeps the left information panel at the Main Menu's 35% / minimum-width split.
/// </summary>
[DisallowMultipleComponent]
public class LoadingHistoricalBackground : MonoBehaviour
{
    [Tooltip("Shared artwork catalog. Must be the same asset the Main Menu rotator uses so both share one shuffle history.")]
    [SerializeField] private HistoricalBackgroundCatalog catalog;
    [Tooltip("Image inside the clipped right-hand viewport that displays the artwork.")]
    [SerializeField] private Image artworkImage;
    [Tooltip("Clipped viewport (RectMask2D) on the right side of the loading panel.")]
    [SerializeField] private RectTransform artworkViewport;
    [Tooltip("Navy information panel on the left (title, status, progress).")]
    [SerializeField] private RectTransform infoPanel;
    [SerializeField, Range(.2f, .6f)] private float infoPanelWidthFraction = .35f;
    [Tooltip("Minimum information panel width in canvas units so text stays readable on narrow screens.")]
    [SerializeField] private float infoPanelMinWidth = 320f;
    [SerializeField] private string persistenceKey = HistoricalBackgroundSelector.DefaultPersistenceKey;

    private bool sessionActive;
    private bool fitting;
    private bool warnedMissingCatalog;

    public bool SessionActive => sessionActive;
    public Sprite CurrentSprite => artworkImage != null ? artworkImage.sprite : null;

    /// <summary>
    /// Starts a loading session: draws exactly one background and keeps it until <see cref="EndSession"/>.
    /// Repeated calls during the same session do not consume further shuffle-bag entries.
    /// </summary>
    public void BeginSession()
    {
        if (sessionActive) return;
        sessionActive = true;

        if (artworkImage == null)
        {
            Debug.LogWarning("[LoadingHistoricalBackground] No artwork Image assigned; the solid navy base will be shown.", this);
            return;
        }
        if (catalog == null && !warnedMissingCatalog)
        {
            warnedMissingCatalog = true;
            Debug.LogWarning("[LoadingHistoricalBackground] No HistoricalBackgroundCatalog assigned. Assign the same catalog asset used by the Main Menu to show shared historical artwork.", this);
        }

        var sprites = HistoricalBackgroundSelector.BuildCatalog(catalog, null);
        Sprite fallback = HistoricalBackgroundSelector.ResolveFallback(catalog, null);
        Sprite selected = HistoricalBackgroundSelector.Draw(sprites, fallback, persistenceKey);
        HistoricalBackgroundSelector.ApplyBackground(artworkImage, selected);
        ApplyLayout();
    }

    public void EndSession()
    {
        sessionActive = false;
    }

    private void OnEnable()
    {
        ApplyLayout();
    }

    private void OnRectTransformDimensionsChange()
    {
        ApplyLayout();
    }

    private void ApplyLayout()
    {
        if (fitting) return;
        fitting = true;
        ApplyResponsiveSplit();
        if (artworkImage != null && artworkImage.sprite != null)
            HistoricalBackgroundSelector.FitCover(artworkImage, artworkViewport != null ? artworkViewport : artworkImage.rectTransform.parent as RectTransform);
        fitting = false;
    }

    // Mirrors MainMenuPresentation: 35% shell width with a readable minimum on narrow screens.
    private void ApplyResponsiveSplit()
    {
        var panel = transform as RectTransform;
        if (panel == null) return;
        Vector2 size = panel.rect.size;
        if (size.x <= 0f || size.y <= 0f) return;
        float shellWidth = Mathf.Min(size.x, Mathf.Max(infoPanelMinWidth, size.x * infoPanelWidthFraction));
        float split = shellWidth / size.x;
        if (infoPanel != null)
        {
            infoPanel.anchorMin = new Vector2(0f, 0f);
            infoPanel.anchorMax = new Vector2(split, 1f);
            infoPanel.offsetMin = Vector2.zero;
            infoPanel.offsetMax = Vector2.zero;
        }
        if (artworkViewport != null)
        {
            artworkViewport.anchorMin = new Vector2(split, 0f);
            artworkViewport.anchorMax = Vector2.one;
            artworkViewport.offsetMin = Vector2.zero;
            artworkViewport.offsetMax = Vector2.zero;
        }
    }
}
