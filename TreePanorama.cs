using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public class TreePanoramaSegment
{
    public Sprite sprite;
    public TechAge startAge;
    public TechAge endAge;
    [Tooltip("Unscaled display width. Zero derives width from the shared display height and sprite aspect ratio.")]
    [Min(0)] public float displayWidth;
}

public struct TreePanoramaPlacement
{
    public Sprite sprite;
    public TechAge startAge;
    public TechAge endAge;
    public Rect rect;
}

public static class TreePanorama
{
    public static List<TreePanoramaPlacement> Resolve(IEnumerable<TreePanoramaSegment> segments,
        float scale, float height, float spacing)
    {
        var result = new List<TreePanoramaPlacement>();
        float currentX = 0;
        scale = Mathf.Max(0.01f, scale);
        spacing = Mathf.Max(0, spacing);
        if (segments == null) return result;
        foreach (var segment in segments.Where(item => item != null && item.sprite != null)
                     .OrderBy(item => (int)item.startAge))
        {
            float aspect = segment.sprite.rect.width / Mathf.Max(1, segment.sprite.rect.height);
            float width = segment.displayWidth > 0 ? segment.displayWidth : Mathf.Max(1, height) * aspect;
            result.Add(new TreePanoramaPlacement
            {
                sprite = segment.sprite, startAge = segment.startAge,
                endAge = (TechAge)Mathf.Max((int)segment.startAge, (int)segment.endAge),
                rect = new Rect(currentX, 0, width * scale, width / aspect * scale)
            });
            currentX += width * scale + spacing;
        }
        return result;
    }

    public static Vector2 Size(IList<TreePanoramaPlacement> placements)
    {
        Vector2 size = Vector2.zero;
        foreach (var placement in placements)
        {
            size.x = Mathf.Max(size.x, placement.rect.xMax);
            size.y = Mathf.Max(size.y, placement.rect.height);
        }
        return size;
    }

    public static float AgeStart(IList<TreePanoramaPlacement> placements, TechAge age)
    {
        foreach (var placement in placements)
        {
            if ((int)age < (int)placement.startAge) return placement.rect.x;
            if ((int)age <= (int)placement.endAge)
                return placement.rect.x + placement.rect.width * ((int)age - (int)placement.startAge) /
                    ((int)placement.endAge - (int)placement.startAge + 1);
        }
        return Size(placements).x;
    }

    public static float AgeWidth(IList<TreePanoramaPlacement> placements, TechAge age)
    {
        foreach (var placement in placements)
            if ((int)age >= (int)placement.startAge && (int)age <= (int)placement.endAge)
                return placement.rect.width / ((int)placement.endAge - (int)placement.startAge + 1);
        return 300;
    }

    public static RectTransform Render(RectTransform content, IList<TreePanoramaPlacement> placements, bool showEraMarkers = false)
    {
        if (content == null) return null;
        var container = new GameObject("BackgroundContainer", typeof(RectTransform)).GetComponent<RectTransform>();
        container.SetParent(content, false);
        container.anchorMin = container.anchorMax = container.pivot = new Vector2(0, 1);
        container.sizeDelta = Size(placements);
        container.SetAsFirstSibling();
        foreach (var placement in placements)
        {
            var image = new GameObject("Background_" + placement.startAge + "_" + placement.endAge,
                typeof(RectTransform), typeof(Image)).GetComponent<Image>();
            var rect = image.rectTransform;
            rect.SetParent(container, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(placement.rect.x, 0);
            rect.sizeDelta = placement.rect.size;
            image.sprite = placement.sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }
        if (showEraMarkers && placements.Count > 0 && TMP_Settings.defaultFontAsset != null)
        {
            var markers = new GameObject("EraMarkers", typeof(RectTransform)).GetComponent<RectTransform>();
            markers.SetParent(content, false);
            markers.anchorMin = markers.anchorMax = markers.pivot = new Vector2(0, 1);
            markers.anchoredPosition = new Vector2(0, 40);
            markers.sizeDelta = new Vector2(Size(placements).x, 28);
            foreach (var placement in placements)
            {
                var label = new GameObject("Era_" + placement.startAge, typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
                label.rectTransform.SetParent(markers, false);
                label.rectTransform.anchorMin = label.rectTransform.anchorMax = label.rectTransform.pivot = new Vector2(0, 1);
                label.rectTransform.anchoredPosition = new Vector2(placement.rect.x, 0);
                label.rectTransform.sizeDelta = new Vector2(placement.rect.width, 28);
                label.font = TMP_Settings.defaultFontAsset;
                label.fontSize = 14;
                label.enableAutoSizing = true;
                label.fontSizeMin = 10;
                label.fontSizeMax = 14;
                label.color = new Color(0.8f, 0.74f, 0.55f);
                label.text = placement.startAge == placement.endAge ? placement.startAge.ToString() : placement.startAge + " - " + placement.endAge;
                label.raycastTarget = false;
            }
        }
        return container;
    }
}