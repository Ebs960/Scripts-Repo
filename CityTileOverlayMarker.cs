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

    private City city;
    private int tileIndex;
    private CityTileOverlayController controller;
    public int TileIndex => tileIndex;

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

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (cam != null) transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
    }

    public void Refresh()
    {
        if (city == null) return;
        var assignment = city.GetTileAssignment(tileIndex);
        bool worked = assignment != null;
        bool rural = assignment != null && assignment.jobType == CityCitizenJobType.RuralSpecialist;
        bool locked = assignment != null && assignment.locked;
        if (workedIcon != null) workedIcon.gameObject.SetActive(worked && !rural);
        if (ruralSpecialistIcon != null) ruralSpecialistIcon.gameObject.SetActive(rural);
        if (lockIcon != null) lockIcon.gameObject.SetActive(locked);
        if (yieldText != null) yieldText.text = BuildYieldText();
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
