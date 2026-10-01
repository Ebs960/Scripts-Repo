using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameCombat;

/// <summary>Empire-wide, prefab-driven archetype loadout and stockpile manager.</summary>
public class EquipmentManagerPanel : MonoBehaviour
{
    public enum ArchetypeFilter { All, Military, Workers }

    [Header("Archetype List")]
    [SerializeField] private RectTransform archetypeListContent;
    [SerializeField] private GameObject archetypeRowPrefab;
    [SerializeField] private ArchetypeFilter archetypeFilter;
    [Header("Loadout")]
    [SerializeField] private TMP_Text selectedArchetypeName;
    [SerializeField] private Image selectedArchetypeIcon;
    [SerializeField] private EquipmentSlotUI weaponSlot, shieldSlot, bodySlot, headSlot, toolSlot, miscSlot, projectileSlot;
    [SerializeField] private TMP_Text statPreviewText;
    [Header("Equipment List")]
    [SerializeField] private RectTransform equipmentListContent;
    [SerializeField] private GameObject equipmentRowPrefab;
    [Header("Summary")]
    [SerializeField] private TMP_Text requiredText, availableText, shortageText;
    [Header("Actions")]
    [SerializeField] private Button applyToAllButton, equipAvailableButton, closeButton;

    [Header("Legacy migration only (not required by the new UI)")]
    [SerializeField] private TMP_Dropdown unitTypeDropdown, weaponDropdown, shieldDropdown, armorDropdown, miscDropdown, projectileDropdown;

    private Civilization currentCiv;
    private ScriptableObject selectedArchetype;
    private EquipmentType selectedSlot = EquipmentType.Weapon;
    private UnitLoadoutTemplate loadout;
    private readonly List<GameObject> rows = new List<GameObject>();
    private readonly List<GameObject> equipmentRows = new List<GameObject>();

    private void Awake()
    {
        foreach (var slot in Slots()) if (slot != null) slot.Clicked += SelectSlot;
        if (applyToAllButton != null) applyToAllButton.onClick.AddListener(ApplyToAll);
        if (equipAvailableButton != null) equipAvailableButton.onClick.AddListener(EquipAvailable);
        if (closeButton != null) closeButton.onClick.AddListener(Hide);
    }
    private void OnDestroy()
    {
        foreach (var slot in Slots()) if (slot != null) slot.Clicked -= SelectSlot;
        if (applyToAllButton != null) applyToAllButton.onClick.RemoveListener(ApplyToAll);
        if (equipAvailableButton != null) equipAvailableButton.onClick.RemoveListener(EquipAvailable);
        if (closeButton != null) closeButton.onClick.RemoveListener(Hide);
        Unsubscribe();
    }
    public void Show(Civilization civ)
    {
        Unsubscribe(); currentCiv = civ;
        if (currentCiv == null) { gameObject.SetActive(false); return; }
        currentCiv.OnEquipmentChanged += InventoryChanged;
        currentCiv.OnProjectileChanged += ProjectileChanged;
        currentCiv.OnUnlocksChanged += UnlocksChanged;
        gameObject.SetActive(true); RefreshArchetypes(true);
    }
    public void ShowDefault() { gameObject.SetActive(true); SetActions(false); }
    public void Hide() { Unsubscribe(); currentCiv = null; gameObject.SetActive(false); }
    public void SetFilter(int value) { archetypeFilter = (ArchetypeFilter)value; RefreshArchetypes(false); }
    private void Unsubscribe()
    {
        if (currentCiv == null) return;
        currentCiv.OnEquipmentChanged -= InventoryChanged; currentCiv.OnProjectileChanged -= ProjectileChanged; currentCiv.OnUnlocksChanged -= UnlocksChanged;
    }
    private void InventoryChanged(EquipmentData _, int __) { RefreshEquipmentList(); RefreshSummary(); }
    private void ProjectileChanged(ProjectileData _, int __) { RefreshProjectileChoices(); }
    private void UnlocksChanged() { RefreshArchetypes(false); }

    private IEnumerable<EquipmentSlotUI> Slots()
    { yield return weaponSlot; yield return shieldSlot; yield return bodySlot; yield return headSlot; yield return toolSlot; yield return miscSlot; yield return projectileSlot; }
    private static string Id(ScriptableObject data)
    {
        if (data is CombatUnitData c && !string.IsNullOrWhiteSpace(c.stableId)) return c.stableId;
        if (data is WorkerUnitData w && !string.IsNullOrWhiteSpace(w.stableId)) return w.stableId;
        return data != null ? data.name : string.Empty; // compatibility fallback for pre-ID assets
    }
    private void RefreshArchetypes(bool selectFirst)
    {
        Clear(rows); if (currentCiv == null || archetypeListContent == null || archetypeRowPrefab == null) { SetActions(false); return; }
        var all = new List<ScriptableObject>();
        if (archetypeFilter != ArchetypeFilter.Workers && currentCiv.unlockedCombatUnits != null) all.AddRange(currentCiv.unlockedCombatUnits.Where(x => x != null));
        if (archetypeFilter != ArchetypeFilter.Military && currentCiv.unlockedWorkerUnits != null) all.AddRange(currentCiv.unlockedWorkerUnits.Where(x => x != null));
        if (!selectFirst && selectedArchetype != null && !all.Contains(selectedArchetype)) selectedArchetype = null;
        if ((selectFirst || selectedArchetype == null) && all.Count > 0) selectedArchetype = all[0];
        foreach (var data in all)
        {
            var go = Instantiate(archetypeRowPrefab, archetypeListContent); rows.Add(go); var row = go.GetComponent<UnitArchetypeRowUI>(); if (row == null) continue;
            bool worker = data is WorkerUnitData; int count = worker ? currentCiv.workerUnits.Count(x => x != null && x.data == data) : currentCiv.combatUnits.Count(x => x != null && x.data == data);
            string label = worker ? ((WorkerUnitData)data).unitName : ((CombatUnitData)data).unitName;
            Sprite icon = worker ? ((WorkerUnitData)data).icon : ((CombatUnitData)data).icon;
            row.Bind(data, icon, label, worker ? "Worker" : ((CombatUnitData)data).unitType.ToString(), count, data == selectedArchetype);
            row.Clicked += SelectArchetype;
        }
        BindSelection();
    }
    private void SelectArchetype(ScriptableObject data) { selectedArchetype = data; RefreshArchetypes(false); }
    private void BindSelection()
    {
        if (selectedArchetype == null) { loadout = null; SetActions(false); return; }
        bool worker = selectedArchetype is WorkerUnitData; loadout = currentCiv.GetOrCreateStandardLoadout(Id(selectedArchetype), worker);
        if (selectedArchetypeName != null) selectedArchetypeName.text = worker ? ((WorkerUnitData)selectedArchetype).unitName : ((CombatUnitData)selectedArchetype).unitName;
        if (selectedArchetypeIcon != null) selectedArchetypeIcon.sprite = worker ? ((WorkerUnitData)selectedArchetype).icon : ((CombatUnitData)selectedArchetype).icon;
        RefreshSlots(); RefreshEquipmentList(); RefreshSummary(); RefreshStats();
    }
    private void SelectSlot(EquipmentSlotUI slot)
    {
        if (slot == null) return;
        if (slot.IsProjectileSlot) { RefreshProjectileChoices(); return; }
        selectedSlot = slot.SlotType; RefreshSlots(); RefreshEquipmentList(); RefreshSummary();
    }
    private void RefreshSlots()
    {
        if (loadout == null) return;
        Bind(weaponSlot, EquipmentType.Weapon); Bind(shieldSlot, EquipmentType.Shield); Bind(bodySlot, EquipmentType.Body);
        Bind(headSlot, EquipmentType.Head); Bind(toolSlot, EquipmentType.Tool); Bind(miscSlot, EquipmentType.Miscellaneous);
        bool ammo = loadout.weapon != null && loadout.weapon.usesProjectiles;
        if (projectileSlot != null) projectileSlot.gameObject.SetActive(ammo);
        if (!ammo) loadout.projectile = null; else RefreshProjectileChoices();
    }
    private void Bind(EquipmentSlotUI ui, EquipmentType slot) { if (ui != null) ui.Bind(loadout.Get(slot), loadout.IsConfigured(slot), selectedSlot == slot); }
    private void RefreshEquipmentList()
    {
        Clear(equipmentRows); if (loadout == null || equipmentListContent == null || equipmentRowPrefab == null) return;
        AddEquipmentRow(null);
        foreach (var item in currentCiv.GetAvailableEquipment().Where(IsCompatible).OrderBy(x => x.equipmentName)) AddEquipmentRow(item);
    }
    private bool IsCompatible(EquipmentData item)
    {
        if (item == null || item.equipmentType != selectedSlot) return false;
        if (selectedArchetype is CombatUnitData c) return (item.targetUnit == EquipmentTarget.CombatUnit || item.targetUnit == EquipmentTarget.Both) && currentCiv.CanEquipEquipmentForUnitTypeCached(c.unitType, item);
        if (item.targetUnit != EquipmentTarget.WorkerUnit && item.targetUnit != EquipmentTarget.Both) return false;
        if (item.allowedUnitTypes != null && item.allowedUnitTypes.Length > 0) return false;
        if (item.minimumLevel > 1) return false;
        return RequirementsMet(item);
    }
    private bool RequirementsMet(EquipmentData item) =>
        (item.requiredTechs == null || item.requiredTechs.All(x => x == null || (currentCiv.researchedTechs != null && currentCiv.researchedTechs.Contains(x)))) &&
        (item.requiredCultures == null || item.requiredCultures.All(x => x == null || (currentCiv.researchedCultures != null && currentCiv.researchedCultures.Contains(x))));
    private void AddEquipmentRow(EquipmentData item)
    {
        var go = Instantiate(equipmentRowPrefab, equipmentListContent); equipmentRows.Add(go); var row = go.GetComponent<EquipmentInventoryRowUI>(); if (row == null) return;
        row.Bind(item, item != null ? currentCiv.GetEquipmentCount(item) : 0, loadout.IsConfigured(selectedSlot) && loadout.Get(selectedSlot) == item, Modifier(item)); row.Clicked += ChooseEquipment;
    }
    private static string Modifier(EquipmentData x)
    {
        if (x == null) return "Explicitly clear this slot"; var parts = new List<string>();
        if (x.attackBonus != 0) parts.Add($"Attack {x.attackBonus:+0.##;-0.##}"); if (x.defenseBonus != 0) parts.Add($"Defense {x.defenseBonus:+0.##;-0.##}");
        if (x.healthBonus != 0) parts.Add($"Health {x.healthBonus:+0.##;-0.##}"); if (x.movementBonus != 0) parts.Add($"Move {x.movementBonus:+0.##;-0.##}"); if (x.workPointsBonus != 0) parts.Add($"Work {x.workPointsBonus:+0.##;-0.##}"); return string.Join(", ", parts);
    }
    private void ChooseEquipment(EquipmentData item) { loadout.Set(selectedSlot, item); RefreshSlots(); RefreshEquipmentList(); RefreshSummary(); RefreshStats(); }
    private List<BaseUnit> MatchingUnits()
    {
        if (selectedArchetype is CombatUnitData c) return currentCiv.combatUnits.Where(x => x != null && x.data == c).Cast<BaseUnit>().ToList();
        if (selectedArchetype is WorkerUnitData w) return currentCiv.workerUnits.Where(x => x != null && x.data == w).Cast<BaseUnit>().ToList();
        return new List<BaseUnit>();
    }
    private List<LoadoutRequirement> Requirements()
    {
        var result = new List<LoadoutRequirement>(); var units = MatchingUnits(); if (loadout == null) return result;
        foreach (var slot in UnitLoadoutTemplate.Slots)
        {
            if (!loadout.IsConfigured(slot)) continue; var desired = loadout.Get(slot); if (desired == null) continue;
            int same = units.Count(x => x.GetEquippedItem(slot) == desired); int returned = units.Count(x => x.GetEquippedItem(slot) != null && x.GetEquippedItem(slot) != desired);
            int required = units.Count - same; int available = currentCiv.GetEquipmentCount(desired);
            result.Add(new LoadoutRequirement { equipment = desired, matchingUnits = units.Count, alreadyEquipped = same, required = required, available = available, returned = returned, shortage = Mathf.Max(0, required - available) });
        }
        return result;
    }
    private void RefreshSummary()
    {
        var req = Requirements(); int required = req.Sum(x => x.required), available = req.Sum(x => Mathf.Min(x.required, x.available)), shortage = req.Sum(x => x.shortage);
        if (requiredText != null) requiredText.text = $"Required: {required}"; if (availableText != null) availableText.text = $"Available: {available}"; if (shortageText != null) shortageText.text = $"Shortage: {shortage}";
        SetActions(loadout != null); if (applyToAllButton != null) applyToAllButton.interactable = loadout != null && shortage == 0;
    }
    private void RefreshStats()
    {
        if (statPreviewText == null || loadout == null) return; UnitStatPreview before, after;
        var empty = new UnitLoadoutTemplate();
        if (selectedArchetype is CombatUnitData c) { before = EquipmentStatPreviewService.Calculate(c, empty); after = EquipmentStatPreviewService.Calculate(c, loadout); }
        else { var w = (WorkerUnitData)selectedArchetype; before = EquipmentStatPreviewService.Calculate(w, empty); after = EquipmentStatPreviewService.Calculate(w, loadout); }
        statPreviewText.text = $"Attack {before.attack:0.#} → {after.attack:0.#}\nDefense {before.defense:0.#} → {after.defense:0.#}\nHealth {before.health:0.#} → {after.health:0.#}\nMovement {before.movement:0.#} → {after.movement:0.#}\nRange {before.range:0.#} → {after.range:0.#}" + (selectedArchetype is WorkerUnitData ? $"\nWork {before.workPoints:0.#} → {after.workPoints:0.#}" : "");
    }
    private void ApplyToAll()
    {
        if (!currentCiv.TryApplyLoadout(MatchingUnits(), loadout, out var reason)) Notify(reason); else Notify("Standard loadout applied to all matching units."); RefreshSummary();
    }
    private void EquipAvailable()
    {
        int changed = 0; foreach (var unit in MatchingUnits()) if (currentCiv.TryApplyLoadout(new[] { unit }, loadout, out _)) changed++; Notify($"Applied the standard loadout to {changed} available unit(s)."); RefreshSummary();
    }
    private void RefreshProjectileChoices()
    {
        if (projectileDropdown == null || loadout == null) return; projectileDropdown.onValueChanged.RemoveAllListeners(); projectileDropdown.ClearOptions();
        var weapon = loadout.weapon; bool active = weapon != null && weapon.usesProjectiles; projectileDropdown.gameObject.SetActive(active); if (!active) return;
        var choices = currentCiv.GetAvailableProjectiles(weapon.projectileCategory); var options = new List<string> { "Use Weapon Default" }; options.AddRange(choices.Select(x => $"{x.projectileName} x{currentCiv.GetProjectileCount(x)}")); projectileDropdown.AddOptions(options);
        int index = loadout.projectile == null ? 0 : choices.IndexOf(loadout.projectile) + 1; projectileDropdown.value = Mathf.Max(0, index); projectileDropdown.onValueChanged.AddListener(i => loadout.projectile = i > 0 && i <= choices.Count ? choices[i - 1] : null);
    }
    private void SetActions(bool active) { if (applyToAllButton != null) applyToAllButton.interactable = active; if (equipAvailableButton != null) equipAvailableButton.interactable = active; }
    private static void Clear(List<GameObject> list) { foreach (var go in list) if (go != null) Destroy(go); list.Clear(); }
    private static void Notify(string message) { if (UIManager.Instance != null) UIManager.Instance.ShowNotification(message); }
}
