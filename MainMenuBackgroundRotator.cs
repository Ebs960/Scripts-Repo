using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MainMenuBackgroundRotator : MonoBehaviour
{
    [SerializeField] private List<Sprite> backgroundSprites = new List<Sprite>();
    [SerializeField] private Image backgroundImage;
    [SerializeField] private Sprite fallbackSprite;
    [SerializeField] private string persistenceKey = "MainMenu.HistoricalBackgrounds.v1";

    private static readonly System.Random SelectionRandom = new System.Random();
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

        var catalog = new List<Sprite>();
        if (backgroundSprites != null)
            foreach (var sprite in backgroundSprites)
                if (sprite != null && !catalog.Contains(sprite)) catalog.Add(sprite);

        Sprite selected = fallbackSprite;
        if (catalog.Count > 0)
        {
            string signature = CatalogSignature(catalog);
            var bag = LoadBag();
            if (bag == null || !bag.IsValid(catalog.Count, signature))
                bag = new MainMenuBackgroundShuffleBag { catalogSignature = signature };
            selected = catalog[bag.Draw(catalog.Count, SelectionRandom)];
            PlayerPrefs.SetString(PreferenceKey, JsonUtility.ToJson(bag));
            PlayerPrefs.Save();
        }

        backgroundImage.sprite = selected;
        backgroundImage.overrideSprite = null;
        backgroundImage.type = Image.Type.Simple;
        backgroundImage.preserveAspect = true;
        backgroundImage.color = Color.white;
        backgroundImage.raycastTarget = false;
        backgroundImage.enabled = selected != null;
        UpdateCover();
    }

    private string PreferenceKey => string.IsNullOrWhiteSpace(persistenceKey) ? "MainMenu.HistoricalBackgrounds.v1" : persistenceKey;

    private MainMenuBackgroundShuffleBag LoadBag()
    {
        string saved = PlayerPrefs.GetString(PreferenceKey, string.Empty);
        if (string.IsNullOrEmpty(saved)) return null;
        try { return JsonUtility.FromJson<MainMenuBackgroundShuffleBag>(saved); }
        catch (ArgumentException) { return null; }
    }

    private static string CatalogSignature(List<Sprite> sprites)
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
        var rect = backgroundImage.rectTransform;
        var viewport = rect.parent as RectTransform;
        if (viewport == null) return;
        fitting = true;
        rect.anchorMin = new Vector2(.5f, .5f);
        rect.anchorMax = rect.anchorMin;
        rect.pivot = rect.anchorMin;
        rect.anchoredPosition = Vector2.zero;
        rect.localScale = Vector3.one;
        rect.localRotation = Quaternion.identity;
        rect.sizeDelta = CalculateCoverSize(viewport.rect.size, backgroundImage.sprite.rect.size);
        fitting = false;
    }

    public static Vector2 CalculateCoverSize(Vector2 viewport, Vector2 artwork)
    {
        if (viewport.x <= 0f || viewport.y <= 0f || artwork.x <= 0f || artwork.y <= 0f) return Vector2.zero;
        float scale = Mathf.Max(viewport.x / artwork.x, viewport.y / artwork.y);
        return artwork * scale;
    }
}

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
