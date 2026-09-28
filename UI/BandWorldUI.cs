using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Persistent, non-interactive world marker for a gameplay Band root.</summary>
public sealed class BandWorldUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text label;
    [SerializeField] private Color packedColor = Color.white;
    [SerializeField] private Color encampedColor = new Color(.8f, .65f, .4f);
    [SerializeField] private Color starvingColor = Color.red;

    private Band band;
    private Camera worldCamera;

    private void OnEnable()
    {
        if (band == null) band = GetComponentInParent<Band>();
        DisableRaycasts();
        Refresh();
    }

    public void Initialize(Band value)
    {
        band = value;
        worldCamera = Camera.main;
        DisableRaycasts();
        Refresh();
    }

    private void LateUpdate()
    {
        if (band == null) return;
        if (worldCamera == null) worldCamera = Camera.main;
        if (worldCamera != null)
            transform.rotation = Quaternion.LookRotation(transform.position - worldCamera.transform.position);
    }

    public void Refresh()
    {
        if (band == null) return;
        if (icon != null)
        {
            icon.sprite = band.Data != null ? band.Data.icon : null;
            icon.enabled = icon.sprite != null;
        }
        if (label != null)
        {
            label.text = $"{(band.State == BandState.Packed ? "PACKED" : "CAMP")}  •  {band.Garrison.Count}";
            label.color = band.IsStarving ? starvingColor :
                (band.State == BandState.Packed ? packedColor : encampedColor);
        }
        DisableRaycasts();
    }

    private void DisableRaycasts()
    {
        foreach (var graphic in GetComponentsInChildren<Graphic>(true))
            graphic.raycastTarget = false;
    }

    /// <summary>Creates the shared minimal fallback when a culture prefab has no authored marker.</summary>
    public static BandWorldUI CreateRuntime(Band owner)
    {
        var markerObject = new GameObject("Band World Marker", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(BandWorldUI));
        markerObject.transform.SetParent(owner.transform, false);
        markerObject.transform.localPosition = new Vector3(0f, 2.25f, 0f);
        markerObject.transform.localScale = Vector3.one * 0.01f;

        var canvas = markerObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.sortingOrder = 20;
        var rect = markerObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(160f, 96f);

        var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconObject.transform.SetParent(markerObject.transform, false);
        var iconRect = iconObject.GetComponent<RectTransform>();
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(.5f, 1f);
        iconRect.pivot = new Vector2(.5f, 1f);
        iconRect.anchoredPosition = Vector2.zero;
        iconRect.sizeDelta = new Vector2(64f, 64f);

        var labelObject = new GameObject("State", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        labelObject.transform.SetParent(markerObject.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 0f);
        labelRect.pivot = new Vector2(.5f, 0f);
        labelRect.anchoredPosition = Vector2.zero;
        labelRect.sizeDelta = new Vector2(0f, 28f);

        var marker = markerObject.GetComponent<BandWorldUI>();
        marker.icon = iconObject.GetComponent<Image>();
        marker.label = labelObject.GetComponent<TextMeshProUGUI>();
        marker.label.alignment = TextAlignmentOptions.Center;
        marker.label.fontSize = 18f;
        marker.Initialize(owner);
        return marker;
    }
}
