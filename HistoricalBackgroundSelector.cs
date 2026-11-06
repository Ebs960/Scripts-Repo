using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared historical-background selection used by the Main Menu and the regular loading screen.
/// Draws from a persistent shuffle bag stored in PlayerPrefs so every background is shown once per cycle,
/// immediate repeats are avoided, and the order survives menu/application restarts. Both screens share
/// the same key and therefore one selection history. A dedicated System.Random keeps Unity's global random
/// state (and world generation) untouched.
/// </summary>
public static class HistoricalBackgroundSelector
{
    public const string DefaultPersistenceKey = "MainMenu.HistoricalBackgrounds.v1";

    private static readonly System.Random SelectionRandom = new System.Random();

    /// <summary>
    /// Builds the effective catalog: the shared asset first, then any legacy per-component sprites.
    /// Null entries and duplicates are dropped while preserving order.
    /// </summary>
    public static List<Sprite> BuildCatalog(HistoricalBackgroundCatalog catalog, IEnumerable<Sprite> legacySprites)
    {
        var result = new List<Sprite>();
        if (catalog != null && catalog.Backgrounds != null)
            foreach (var sprite in catalog.Backgrounds)
                if (sprite != null && !result.Contains(sprite)) result.Add(sprite);
        if (legacySprites != null)
            foreach (var sprite in legacySprites)
                if (sprite != null && !result.Contains(sprite)) result.Add(sprite);
        return result;
    }

    public static Sprite ResolveFallback(HistoricalBackgroundCatalog catalog, Sprite legacyFallback)
    {
        if (catalog != null && catalog.FallbackSprite != null) return catalog.FallbackSprite;
        return legacyFallback;
    }

    /// <summary>
    /// Draws the next background from the shared shuffle bag and persists the updated bag immediately.
    /// Returns the fallback (possibly null) when the catalog is empty.
    /// </summary>
    public static Sprite Draw(List<Sprite> catalog, Sprite fallback, string persistenceKey)
    {
        if (catalog == null || catalog.Count == 0) return fallback;

        string key = NormalizeKey(persistenceKey);
        string signature = CatalogSignature(catalog);
        var bag = LoadBag(key);
        if (bag == null || !bag.IsValid(catalog.Count, signature))
            bag = new MainMenuBackgroundShuffleBag { catalogSignature = signature };

        Sprite selected = catalog[bag.Draw(catalog.Count, SelectionRandom)];
        PlayerPrefs.SetString(key, JsonUtility.ToJson(bag));
        PlayerPrefs.Save();
        return selected;
    }

    public static string NormalizeKey(string persistenceKey)
    {
        return string.IsNullOrWhiteSpace(persistenceKey) ? DefaultPersistenceKey : persistenceKey;
    }

    private static MainMenuBackgroundShuffleBag LoadBag(string key)
    {
        string saved = PlayerPrefs.GetString(key, string.Empty);
        if (string.IsNullOrEmpty(saved)) return null;
        try { return JsonUtility.FromJson<MainMenuBackgroundShuffleBag>(saved); }
        catch (ArgumentException) { return null; }
    }

    public static string CatalogSignature(List<Sprite> sprites)
    {
        var signature = new StringBuilder();
        foreach (var sprite in sprites)
        {
            string name = sprite.name;
            string texture = sprite.texture != null ? sprite.texture.name : string.Empty;
            signature.Append(name.Length).Append(':').Append(name);
            signature.Append(texture.Length).Append(':').Append(texture);
            Rect rect = sprite.rect;
            signature.Append('|').Append(rect.x.ToString("R", CultureInfo.InvariantCulture));
            signature.Append('|').Append(rect.y.ToString("R", CultureInfo.InvariantCulture));
            signature.Append('|').Append(rect.width.ToString("R", CultureInfo.InvariantCulture));
            signature.Append('|').Append(rect.height.ToString("R", CultureInfo.InvariantCulture)).Append(';');
        }
        return signature.ToString();
    }

    /// <summary>
    /// Configures an Image to display a background: Simple type, aspect preserved, no raycasts.
    /// The image is disabled when no sprite is available so the solid navy base shows through.
    /// </summary>
    public static void ApplyBackground(Image image, Sprite sprite)
    {
        if (image == null) return;
        image.sprite = sprite;
        image.overrideSprite = null;
        image.type = Image.Type.Simple;
        image.preserveAspect = true;
        image.color = Color.white;
        image.raycastTarget = false;
        image.enabled = sprite != null;
    }

    /// <summary>
    /// Sizes the image so its sprite covers the viewport (crop, never stretch), centred in the viewport.
    /// Returns false when the image has no sprite or the viewport is not laid out yet.
    /// </summary>
    public static bool FitCover(Image image, RectTransform viewport)
    {
        if (image == null || image.sprite == null) return false;
        var rect = image.rectTransform;
        if (viewport == null) viewport = rect.parent as RectTransform;
        if (viewport == null) return false;
        rect.anchorMin = new Vector2(.5f, .5f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = rect.anchorMin;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.sizeDelta = CalculateCoverSize(viewport.rect.size, image.sprite.rect.size);
        return true;
    }

    public static Vector2 CalculateCoverSize(Vector2 viewport, Vector2 artwork)
    {
        if (viewport.x <= 0f || viewport.y <= 0f || artwork.x <= 0f || artwork.y <= 0f) return Vector2.zero;
        float scale = Mathf.Max(viewport.x / artwork.x, viewport.y / artwork.y);
        return artwork * scale;
    }
}

/// <summary>
/// Persistent shuffle-bag state. The class name and field layout are kept so existing
/// "MainMenu.HistoricalBackgrounds.v1" PlayerPrefs data deserialises unchanged.
/// </summary>
[Serializable]
public sealed class MainMenuBackgroundShuffleBag
{
    public int version = 1;
    public string catalogSignature;
    public List<int> remainingOrder = new List<int>();
    public int lastIndex = -1;

    public bool IsValid(int count, string signature)
    {
        if (version != 1 || catalogSignature != signature || remainingOrder == null || remainingOrder.Count >= count ||
            lastIndex < 0 || lastIndex >= count) return false;
        var seen = new HashSet<int>();
        foreach (int index in remainingOrder)
            if (index < 0 || index >= count || index == lastIndex || !seen.Add(index)) return false;
        return true;
    }

    public int Draw(int count, System.Random random)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (remainingOrder.Count == 0)
        {
            for (int index = 0; index < count; index++) remainingOrder.Add(index);
            for (int index = count - 1; index > 0; index--)
            {
                int swapIndex = random.Next(index + 1);
                int value = remainingOrder[index];
                remainingOrder[index] = remainingOrder[swapIndex];
                remainingOrder[swapIndex] = value;
            }
            if (count > 1 && remainingOrder[count - 1] == lastIndex)
            {
                int value = remainingOrder[0];
                remainingOrder[0] = remainingOrder[count - 1];
                remainingOrder[count - 1] = value;
            }
        }
        int selected = remainingOrder[remainingOrder.Count - 1];
        remainingOrder.RemoveAt(remainingOrder.Count - 1);
        lastIndex = selected;
        return selected;
    }
}
