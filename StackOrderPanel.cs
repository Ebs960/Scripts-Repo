using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Displays the members of a campaign army as a vertical list of icon rows.
/// Each row shows the unit's icon and name. Up/Down buttons reorder the stack,
/// changing who acts as the front-row unit in combat.
///
/// Setup (Inspector):
///   - Assign 'entryContainer': a GameObject with a VerticalLayoutGroup that holds the rows.
///   - Assign 'entryTemplatePrefab': a hidden GameObject (set active=false) with the following
///     named children:
///       "Background" — Image (tinted for the selected unit)
///       "Icon"       — Image (unit icon)
///       "Name"       — TextMeshProUGUI (unit name)
///       "UpButton"   — Button (move this unit one slot toward front)
///       "DownButton" — Button (move this unit one slot toward rear)
///   - Assign this component to a panel that sits beneath or beside UnitInfoPanel.
/// </summary>
public class StackOrderPanel : MonoBehaviour
{
    private void Awake()
    {
        gameObject.SetActive(false);
    }

    [Header("References")]
    [Tooltip("Vertical layout container where entry rows are placed.")]
    [SerializeField] private Transform entryContainer;

    [Tooltip("Inactive template GameObject cloned for each stack entry. " +
             "Must have children named: Background (Image), Icon (Image), Name (TextMeshProUGUI), " +
             "UpButton (Button), DownButton (Button).")]
    [SerializeField] private GameObject entryTemplatePrefab;

    [Header("Selection Tint")]
    [Tooltip("Background color for the currently selected unit's row.")]
    [SerializeField] private Color selectedTint = new Color(0.25f, 0.55f, 1f, 0.45f);
    [Tooltip("Background color for unselected rows.")]
    [SerializeField] private Color normalTint = new Color(0f, 0f, 0f, 0.25f);

    // Tracks army members in order (slot 0 is the campaign representative)
    private readonly List<BaseUnit> _orderedUnits = new List<BaseUnit>();
    private readonly List<GameObject> _rows = new List<GameObject>();

    // Unit currently shown as selected in the info panel
    private BaseUnit _selectedUnit;

    // ──────────────────────── Public API ────────────────────────

    /// <summary>
    /// Rebuild the panel to show the stack for the tile the given unit is on.
    /// Pass null to hide the panel.
    /// </summary>
    public void Refresh(BaseUnit selectedUnit)
    {
        _selectedUnit = selectedUnit;
        ClearRows();

        if (selectedUnit == null || selectedUnit.currentTileIndex < 0)
        {
            gameObject.SetActive(false);
            return;
        }

        // Collect all members of the selected unit's army.
        BuildOrderedList(selectedUnit);

        if (_orderedUnits.Count <= 1)
        {
            // Single unit — no stack panel needed
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(true);

        for (int i = 0; i < _orderedUnits.Count; i++)
            CreateRow(i);
    }

    // ──────────────────────── Private helpers ────────────────────────

    private void BuildOrderedList(BaseUnit anyUnitOnTile)
    {
        _orderedUnits.Clear();

        _orderedUnits.Add(anyUnitOnTile);
        _orderedUnits.AddRange(anyUnitOnTile.GetStackedUnits());
        _orderedUnits.Sort((left, right) => left.stackSlot.CompareTo(right.stackSlot));
    }

    private void ClearRows()
    {
        foreach (var row in _rows)
            if (row != null) Destroy(row);
        _rows.Clear();
    }

    private void CreateRow(int index)
    {
        if (entryTemplatePrefab == null || entryContainer == null) return;

        var row = Instantiate(entryTemplatePrefab, entryContainer);
        row.SetActive(true);
        _rows.Add(row);

        var unit = _orderedUnits[index];
        bool isSelected = unit == _selectedUnit;
        bool isFirst    = index == 0;
        bool isLast     = index == _orderedUnits.Count - 1;

        // Background tint
        var bg = row.transform.Find("Background")?.GetComponent<Image>();
        if (bg != null)
            bg.color = isSelected ? selectedTint : normalTint;

        // Icon
        var iconImg = row.transform.Find("Icon")?.GetComponent<Image>();
        if (iconImg != null)
        {
            Sprite sp = GetUnitIcon(unit);
            iconImg.sprite  = sp;
            iconImg.enabled = sp != null;
        }

        // Name
        var nameText = row.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
        if (nameText != null)
        {
            string label = unit.UnitName;
            if (isFirst) label += " [Representative]";
            else if (isLast) label += " [Reserve]";
            nameText.text = label;
        }

        // Click row body to select this unit
        if (row.TryGetComponent<Button>(out var rowButton))
        {
            int capturedIndex = index;
            rowButton.onClick.RemoveAllListeners();
            rowButton.onClick.AddListener(() => OnUnitClicked(capturedIndex));
        }

        // Up button
        var upBtn = row.transform.Find("UpButton")?.GetComponent<Button>();
        if (upBtn != null)
        {
            upBtn.interactable = !isFirst;
            int capturedIndex = index;
            upBtn.onClick.RemoveAllListeners();
            upBtn.onClick.AddListener(() => OnMoveUp(capturedIndex));
        }

        // Down button
        var downBtn = row.transform.Find("DownButton")?.GetComponent<Button>();
        if (downBtn != null)
        {
            downBtn.interactable = !isLast;
            int capturedIndex = index;
            downBtn.onClick.RemoveAllListeners();
            downBtn.onClick.AddListener(() => OnMoveDown(capturedIndex));
        }
    }

    // ──────────────────────── Button handlers ────────────────────────

    private void OnUnitClicked(int index)
    {
        if (index < 0 || index >= _orderedUnits.Count) return;
        var unit = _orderedUnits[index];
        if (unit == null) return;

        _selectedUnit = unit;
        if (UnitSelectionManager.Instance != null)
            UnitSelectionManager.Instance.SelectUnit(unit);
        // Panel will be refreshed by UnitInfoPanel.UpdateStackInfo callback
    }

    private void OnMoveUp(int index)
    {
        // Swap slot at 'index' with slot at 'index - 1' (closer to front)
        if (index <= 0 || index >= _orderedUnits.Count) return;
        SwapSlots(index - 1, index);
    }

    private void OnMoveDown(int index)
    {
        // Swap slot at 'index' with slot at 'index + 1' (closer to rear)
        if (index < 0 || index >= _orderedUnits.Count - 1) return;
        SwapSlots(index, index + 1);
    }

    private void SwapSlots(int indexA, int indexB)
    {
        if (indexA < 0 || indexB >= _orderedUnits.Count) return;

        var unitA = _orderedUnits[indexA];
        var unitB = _orderedUnits[indexB];
        if (unitA == null || unitB == null) return;

        int slotA = unitA.stackSlot;
        int slotB = unitB.stackSlot;

        unitA.stackSlot = slotB;
        unitB.stackSlot = slotA;

        if (slotA == 0 && unitB is CombatUnit newRepresentative)
            CampaignArmyService.SetRepresentative(newRepresentative);
        else if (slotB == 0 && unitA is CombatUnit restoredRepresentative)
            CampaignArmyService.SetRepresentative(restoredRepresentative);

        // If the front unit changed, notify selection so the info panel refreshes
        // (selecting the previously-selected unit so the panel updates cleanly)
        if (UnitSelectionManager.Instance != null && _selectedUnit != null)
            UnitSelectionManager.Instance.SelectUnit(_selectedUnit);

        // Rebuild rows
        Refresh(_selectedUnit);
    }

    // ──────────────────────── Utility ────────────────────────

    private static Sprite GetUnitIcon(BaseUnit unit)
    {
        if (unit is CombatUnit cu && cu.data != null) return cu.data.GetIcon(cu.owner);
        if (unit is WorkerUnit wu && wu.data != null) return wu.data.GetIcon(wu.owner);
        return null;
    }
}
