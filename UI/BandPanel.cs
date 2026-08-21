using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>Inspector-wired campaign panel. Intentionally contains no combat-stat fields.</summary>
public sealed class BandPanel : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText, populationText, foodText, starvationText, stateText, movementText, structuresText, productionText, garrisonText;
    [SerializeField] private Button packButton, encampButton, forageButton;
    [Header("Hardcoded Paleolithic Production Grid")]
    [SerializeField] private RectTransform productionButtonsRoot;
    private readonly List<GameObject> generatedButtons = new List<GameObject>();
    private static readonly string[] StructureButtonNames = { "Foraging Tent", "Story Circle", "Burial Pit", "Stone Pile", "Tool Maker", "Fishing Tent" };
    private static readonly string[] UnitButtonNames = { "Hunter", "Clubman", "Spear Thrower", "Raft" };
    private Band band;

    public void Show(Band value)
    {
        band = value;
        gameObject.SetActive(value != null);
        if (value != null) { BuildHardcodedProductionButtons(); Refresh(); }
    }

    public void Pack() { if (band != null) band.Pack(); Refresh(); }
    public void Encamp() { if (band != null) band.Encamp(); Refresh(); }
    public void Forage() { if (band != null) band.Forage(); Refresh(); }

    public void Refresh()
    {
        if (band == null) return;
        Set(titleText, band.Data != null ? band.Data.displayName : "Band");
        Set(populationText, $"Population: {band.Population}");
        Set(foodText, $"Food Reserve: {band.FoodReserve} / {band.FoodCapacity}\nFood Use: {band.FoodRequiredPerTurn} / turn");
        int collapse = band.Data != null ? band.Data.collapseAfterStarvationTurns : 8;
        Set(starvationText, band.IsStarving ? $"STARVING\n{band.ConsecutiveStarvationTurns} / {collapse} turns" : $"Starvation: 0 / {collapse}");
        Set(stateText, band.State.ToString());
        Set(movementText, $"Movement: {band.CurrentMovePoints}");
        Set(structuresText, "Structures\n" + string.Join("\n", band.BuiltStructures.Where(x => x != null).Select(x => "• " + x.structureName)));
        Set(productionText, band.QueuedStructure != null ? $"Producing: {band.QueuedStructure.structureName} ({band.ProductionProgress}/{band.QueuedStructure.productionCost})" : "Production: idle");
        Set(garrisonText, "Garrison\n" + string.Join("\n", band.Garrison.Where(x => x != null).Select(x => "• " + x.UnitName)));
        if (packButton != null) packButton.interactable = band.State == BandState.Encamped;
        if (encampButton != null) encampButton.interactable = band.State == BandState.Packed;
        if (forageButton != null) forageButton.interactable = band.CurrentMovePoints > 0;
        RefreshHardcodedProductionButtons();
    }

    private void BuildHardcodedProductionButtons()
    {
        foreach (var go in generatedButtons) if (go != null) Destroy(go);
        generatedButtons.Clear();
        if (band == null) return;
        EnsureProductionRoot();

        CreateSectionLabel("BAND IMPROVEMENTS", new Vector2(0f, -8f));
        for (int i = 0; i < StructureButtonNames.Length; i++)
        {
            string hardcodedName = StructureButtonNames[i];
            BandStructureData structure = band.Data.allowedStructures.FirstOrDefault(x => x != null && x.structureName == hardcodedName);
            CreateOptionButton(hardcodedName, structure != null ? structure.icon : null, structure != null ? structure.productionCost : 0,
                new Vector2((i % 3) * 164f, -42f - (i / 3) * 58f),
                () => { if (structure != null && band.QueueStructure(structure)) Refresh(); });
        }

        CreateSectionLabel("MILITARY UNITS", new Vector2(0f, -166f));
        for (int i = 0; i < UnitButtonNames.Length; i++)
        {
            string hardcodedName = UnitButtonNames[i];
            CombatUnitData unit = band.Data.allowedMilitaryRecruitment.FirstOrDefault(x => x != null && x.unitName == hardcodedName);
            CreateOptionButton(hardcodedName, unit != null ? unit.GetIcon(band.Owner) : null, unit != null ? unit.bandProductionCost : 0,
                new Vector2((i % 3) * 164f, -200f - (i / 3) * 58f),
                () => { if (unit != null && band.QueueMilitaryUnit(unit)) Refresh(); });
        }
    }

    private void RefreshHardcodedProductionButtons()
    {
        if (band == null) return;
        foreach (var go in generatedButtons)
        {
            if (go == null || !go.name.StartsWith("BandOption_")) continue;
            string optionName = go.name.Substring("BandOption_".Length);
            var button = go.GetComponent<Button>();
            var status = go.transform.Find("Status")?.GetComponent<TMP_Text>();
            var structure = band.Data.allowedStructures.FirstOrDefault(x => x != null && x.structureName == optionName);
            var unit = band.Data.allowedMilitaryRecruitment.FirstOrDefault(x => x != null && x.unitName == optionName);
            bool available;
            string reason;
            if (structure != null) available = band.CanQueueStructure(structure, out reason);
            else if (unit != null) available = band.CanQueueMilitaryUnit(unit, out reason);
            else { available = false; reason = "Asset not assigned"; }
            if (button != null) button.interactable = available;
            if (status != null) status.text = available ? "AVAILABLE" : reason.ToUpperInvariant();
        }
    }

    private void CreateSectionLabel(string value, Vector2 position)
    {
        var go = new GameObject(value, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(productionButtonsRoot, false); generatedButtons.Add(go);
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = position; rect.sizeDelta = new Vector2(492f, 28f);
        var text = go.GetComponent<TextMeshProUGUI>(); text.text = value; text.fontSize = 18f; text.fontStyle = FontStyles.Bold; text.color = Color.white;
        if (titleText != null) text.font = titleText.font;
    }

    private void EnsureProductionRoot()
    {
        if (productionButtonsRoot != null) return;
        var root = new GameObject("Hardcoded Paleolithic Production", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        productionButtonsRoot = (RectTransform)root.transform;
        productionButtonsRoot.anchorMin = productionButtonsRoot.anchorMax = new Vector2(0f, 1f);
        productionButtonsRoot.pivot = new Vector2(0f, 1f);
        productionButtonsRoot.anchoredPosition = new Vector2(24f, -310f);
        productionButtonsRoot.sizeDelta = new Vector2(492f, 310f);
    }

    private void CreateOptionButton(string optionName, Sprite icon, int cost, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject("BandOption_" + optionName, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(productionButtonsRoot, false); generatedButtons.Add(go);
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = position; rect.sizeDelta = new Vector2(154f, 50f);
        go.GetComponent<Image>().color = new Color(.18f, .14f, .09f, .95f);
        var button = go.GetComponent<Button>(); button.onClick.AddListener(action);
        var iconObject = new GameObject("Icon", typeof(RectTransform), typeof(Image)); iconObject.transform.SetParent(go.transform, false);
        var iconRect = (RectTransform)iconObject.transform; iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 1f); iconRect.pivot = new Vector2(0f, 1f); iconRect.anchoredPosition = new Vector2(4f, -5f); iconRect.sizeDelta = new Vector2(40f, 40f);
        var iconImage = iconObject.GetComponent<Image>(); iconImage.sprite = icon; iconImage.enabled = icon != null; iconImage.raycastTarget = false;
        CreateButtonText(go.transform, "Name", optionName, new Vector2(48f, -3f), new Vector2(102f, 22f), 13f, FontStyles.Bold);
        CreateButtonText(go.transform, "Cost", cost > 0 ? $"{cost} Production" : "UNASSIGNED", new Vector2(48f, -23f), new Vector2(102f, 14f), 9f, FontStyles.Normal);
        CreateButtonText(go.transform, "Status", string.Empty, new Vector2(48f, -36f), new Vector2(102f, 12f), 7f, FontStyles.Normal);
    }

    private void CreateButtonText(Transform parent, string objectName, string value, Vector2 position, Vector2 size, float fontSize, FontStyles style)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform; rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = position; rect.sizeDelta = size;
        var text = go.GetComponent<TextMeshProUGUI>(); text.text = value; text.fontSize = fontSize; text.fontStyle = style; text.color = Color.white; text.raycastTarget = false;
        if (titleText != null) text.font = titleText.font;
    }

    private static void Set(TMP_Text target, string value) { if (target != null) target.text = value; }
}
