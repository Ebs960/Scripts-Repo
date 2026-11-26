// Assets/Scripts/UI/CityTileOverlayMarker.cs
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CityTileOverlayMarker : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TextMeshProUGUI yieldText;
    [SerializeField] private Image workedIcon;
    [SerializeField] private Image ruralSpecialistIcon;
    [SerializeField] private Image lockIcon;
    [SerializeField] private Image selectedOutline;
    [SerializeField] private Button button;

    [Header("State Art")]
    [SerializeField] private Image stateIcon;
    [SerializeField] private Sprite workedSprite;
    [SerializeField] private Sprite unworkedSprite;
    [SerializeField] private Sprite unavailableSprite;

    private City city;
    private int tileIndex;
    private CityTileOverlayController controller;
    public int TileIndex => tileIndex;

    /// <summary>False when this city cannot put a citizen on the tile (not owned, or worked by a sister city).</summary>
    public bool IsAvailable { get; private set; } = true;

    public void Initialize(City city, int tileIndex, CityTileOverlayController controller)
    {
        this.city = city; this.tileIndex = tileIndex; this.controller = controller;
        if (button != null)
        {
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() => this.controller?.SelectTile(this.tileIndex));
        }
        Refresh();
    }

    /// <summary>Scales the marker so it is the given width in world units.</summary>
    public void SetWorldSize(float width)
    {
        float authoredWidth = transform is RectTransform rect && rect.rect.width > 0f ? rect.rect.width : 1f;
        transform.localScale = Vector3.one * (width / authoredWidth);
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam != null) transform.rotation = cam.transform.rotation;
    }

    public void Refresh()
    {
        if (city == null) return;
        var assignment = city.GetTileAssignment(tileIndex);
        bool worked = assignment != null;
        bool rural = assignment != null && assignment.jobType == CityCitizenJobType.RuralSpecialist;
        bool locked = assignment != null && assignment.locked;
        IsAvailable = worked || (city.IsTileWorkableByThisCity(tileIndex) && !IsWorkedByAnotherCity());
        if (stateIcon != null)
        {
            Sprite state = worked ? workedSprite : IsAvailable ? unworkedSprite : unavailableSprite;
            if (state != null) stateIcon.sprite = state;
        }
        if (workedIcon != null) workedIcon.gameObject.SetActive(worked && !rural);
        if (ruralSpecialistIcon != null) ruralSpecialistIcon.gameObject.SetActive(rural);
        if (lockIcon != null) lockIcon.gameObject.SetActive(locked);
        if (yieldText != null) yieldText.text = BuildYieldText();
    }

    private bool IsWorkedByAnotherCity()
    {
        var cities = city.owner != null ? city.owner.cities : null;
        if (cities == null) return false;
        foreach (var other in cities)
            if (other != null && other != city && other.GetTileAssignment(tileIndex) != null) return true;
        return false;
    }

    public void SetSelected(bool selected)
    {
        if (selectedOutline != null) selectedOutline.gameObject.SetActive(selected);
    }

    private string BuildYieldText()
    {
        var ts = TileSystem.GetForPlanet(city.planetIndex) ?? TileSystem.Instance;
        var td = ts != null ? ts.GetTileData(tileIndex) : null;
        if (td == null) return "";
        int food = td.food, production = td.production, gold = td.gold;
        if (td.improvement != null)
        {
            food += td.improvement.foodPerTurn;
            production += td.improvement.productionPerTurn;
            gold += td.improvement.goldPerTurn;
        }
        return $"F{food} P{production} G{gold}";
    }
}