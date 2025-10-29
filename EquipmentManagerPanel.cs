using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EquipmentManagerPanel : MonoBehaviour
{
    [Header("UI Refs")]
    [SerializeField] private TMP_Dropdown unitTypeDropdown;
    [SerializeField] private TMP_Dropdown weaponDropdown;
    [SerializeField] private TMP_Dropdown shieldDropdown;
    [SerializeField] private TMP_Dropdown armorDropdown;
    [SerializeField] private TMP_Dropdown miscDropdown;
    [SerializeField] private TMP_Dropdown projectileDropdown; // NEW: Active projectile selection
    [SerializeField] private Button applyToAllButton;
    [SerializeField] private Button closeButton;

    private Civilization currentCiv;
    private List<CombatUnitData> availableUnits = new List<CombatUnitData>();
    private Dictionary<int, CombatUnitData> indexToUnitData = new Dictionary<int, CombatUnitData>();

    // Working caches for dropdown -> EquipmentData
    private List<EquipmentData> weaponOptions = new List<EquipmentData>();
    private List<EquipmentData> shieldOptions = new List<EquipmentData>();
    private List<EquipmentData> armorOptions  = new List<EquipmentData>();
    private List<EquipmentData> miscOptions   = new List<EquipmentData>();
    
    // Working cache for dropdown -> ProjectileData
    private List<GameCombat.ProjectileData> projectileOptions = new List<GameCombat.ProjectileData>();

    private void Awake()
    {
        if (applyToAllButton != null) applyToAllButton.onClick.AddListener(ApplySelectionToAllUnitsOfType);
        if (closeButton != null) closeButton.onClick.AddListener(() => gameObject.SetActive(false));
        SetInteractable(false);
    }

    private void OnDestroy()
    {
        if (applyToAllButton != null) applyToAllButton.onClick.RemoveListener(ApplySelectionToAllUnitsOfType);
        if (closeButton != null) closeButton.onClick.RemoveListener(() => gameObject.SetActive(false));
        UnsubscribeCivEvents();
    }

    public void Show(Civilization civ)
    {
        if (civ == null) { gameObject.SetActive(false); return; }
        currentCiv = civ;
        SubscribeCivEvents();
        RefreshUnitTypes();
        gameObject.SetActive(true);
    }

    // For SendMessage compatibility when a civ isn't provided
    public void ShowDefault()
    {
        gameObject.SetActive(true);
        SetInteractable(false);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        UnsubscribeCivEvents();
        currentCiv = null;
    }

    private void SubscribeCivEvents()
    {
        if (currentCiv == null) return;
        currentCiv.OnUnlocksChanged += RefreshAll;
        currentCiv.OnEquipmentChanged += OnCivEquipmentChanged;
        currentCiv.OnProjectileChanged += OnCivProjectileChanged;
    }

    private void UnsubscribeCivEvents()
    {
        if (currentCiv == null) return;
        currentCiv.OnUnlocksChanged -= RefreshAll;
        currentCiv.OnEquipmentChanged -= OnCivEquipmentChanged;
        currentCiv.OnProjectileChanged -= OnCivProjectileChanged;
    }

    private void RefreshAll()
    {
        RefreshUnitTypes();
    }

    private void OnCivEquipmentChanged(EquipmentData data, int count)
    {
        // Only need to refresh the equipment dropdowns for current selection
        PopulateEquipmentDropdownsForSelectedUnit();
    }
    
    private void OnCivProjectileChanged(GameCombat.ProjectileData data, int count)
    {
        // Refresh projectile dropdown when projectile inventory changes
        PopulateProjectileDropdownForSelectedWeapon();
    }

    private void SetInteractable(bool enabled)
    {
        if (unitTypeDropdown != null) unitTypeDropdown.interactable = enabled;
        if (weaponDropdown != null) weaponDropdown.interactable = enabled;
        if (shieldDropdown != null) shieldDropdown.interactable = enabled;
        if (armorDropdown != null) armorDropdown.interactable = enabled;
        if (miscDropdown != null) miscDropdown.interactable = enabled;
        if (projectileDropdown != null) projectileDropdown.interactable = enabled;
        if (applyToAllButton != null) applyToAllButton.interactable = enabled;
    }

    private void RefreshUnitTypes()
    {
        if (currentCiv == null || unitTypeDropdown == null)
        {
            SetInteractable(false);
            return;
        }

        availableUnits = currentCiv.unlockedCombatUnits != null
            ? currentCiv.unlockedCombatUnits.Where(u => u != null).ToList()
            : new List<CombatUnitData>();

        unitTypeDropdown.ClearOptions();
        indexToUnitData.Clear();

        var options = new List<TMP_Dropdown.OptionData>();
        for (int i = 0; i < availableUnits.Count; i++)
        {
            var u = availableUnits[i];
            options.Add(new TMP_Dropdown.OptionData(u.unitName));
            indexToUnitData[i] = u;
        }

        if (options.Count == 0)
        {
            options.Add(new TMP_Dropdown.OptionData("No units unlocked"));
            SetInteractable(false);
        }
        else
        {
            SetInteractable(true);
        }

        unitTypeDropdown.AddOptions(options);
        unitTypeDropdown.onValueChanged.RemoveAllListeners();
        unitTypeDropdown.onValueChanged.AddListener(_ => PopulateEquipmentDropdownsForSelectedUnit());
        PopulateEquipmentDropdownsForSelectedUnit();
    }

    private CombatUnitData GetSelectedUnitData()
    {
        if (unitTypeDropdown == null) return null;
        if (!indexToUnitData.TryGetValue(unitTypeDropdown.value, out var data)) return null;
        return data;
    }

    private void PopulateEquipmentDropdownsForSelectedUnit()
    {
        var unitData = GetSelectedUnitData();
        if (unitData == null || currentCiv == null)
        {
            ClearEquipmentDropdowns();
            return;
        }

        var all = currentCiv.GetAvailableEquipment();
        if (all == null) all = new List<EquipmentData>();

        // Filter equipment that the civilization actually has in inventory
        var availableEquipment = all.Where(e => currentCiv.HasEquipment(e)).ToList();

        weaponOptions = Filter(availableEquipment, unitData, EquipmentType.Weapon);
        shieldOptions = Filter(availableEquipment, unitData, EquipmentType.Shield);
        armorOptions  = Filter(availableEquipment, unitData, EquipmentType.Armor);
        miscOptions   = Filter(availableEquipment, unitData, EquipmentType.Miscellaneous);

        Populate(weaponDropdown, weaponOptions, includeNone: true);
        Populate(shieldDropdown, shieldOptions, includeNone: true);
        Populate(armorDropdown,  armorOptions,  includeNone: true);
        Populate(miscDropdown,   miscOptions,   includeNone: true);
        
        // Hook up weapon dropdown listener to refresh projectiles when weapon changes
        if (weaponDropdown != null)
        {
            weaponDropdown.onValueChanged.RemoveAllListeners();
            weaponDropdown.onValueChanged.AddListener(_ => PopulateProjectileDropdownForSelectedWeapon());
        }
        
        // Initial projectile population
        PopulateProjectileDropdownForSelectedWeapon();
    }

    private List<EquipmentData> Filter(List<EquipmentData> list, CombatUnitData unit, EquipmentType type)
    {
        if (unit == null) return new List<EquipmentData>();
        
        return list.Where(e => e != null
                            && e.equipmentType == type
                            && (e.allowedUnitTypes == null || e.allowedUnitTypes.Length == 0 || e.allowedUnitTypes.Contains(unit.unitType)))
                   .ToList();
    }

    private void Populate(TMP_Dropdown dropdown, List<EquipmentData> items, bool includeNone)
    {
        if (dropdown == null)
            return;
        dropdown.ClearOptions();
        var opts = new List<TMP_Dropdown.OptionData>();
        if (includeNone) opts.Add(new TMP_Dropdown.OptionData("(None)"));
        foreach (var e in items)
        {
            string label = e.equipmentName;
            // Optionally show inventory count
            int count = currentCiv != null ? currentCiv.GetEquipmentCount(e) : 0;
            if (count > 0) label += $"  x{count}";
            opts.Add(new TMP_Dropdown.OptionData(label));
        }
        dropdown.AddOptions(opts);
        dropdown.value = 0; // default to None
    }

    private void ClearEquipmentDropdowns()
    {
        Populate(weaponDropdown, new List<EquipmentData>(), includeNone: true);
        Populate(shieldDropdown, new List<EquipmentData>(), includeNone: true);
        Populate(armorDropdown,  new List<EquipmentData>(), includeNone: true);
        Populate(miscDropdown,   new List<EquipmentData>(), includeNone: true);
        PopulateProjectileDropdown(new List<GameCombat.ProjectileData>(), includeDefault: true);
    }
    
    /// <summary>
    /// Populates the projectile dropdown based on the currently selected weapon
    /// </summary>
    private void PopulateProjectileDropdownForSelectedWeapon()
    {
        if (currentCiv == null || projectileDropdown == null)
        {
            projectileOptions.Clear();
            PopulateProjectileDropdown(projectileOptions, includeDefault: true);
            return;
        }
        
        // Get selected weapon
        var selectedWeapon = GetSelectionFrom(weaponDropdown, weaponOptions);
        
        // If no weapon or weapon doesn't use projectiles, clear dropdown
        if (selectedWeapon == null || !selectedWeapon.usesProjectiles)
        {
            projectileOptions.Clear();
            PopulateProjectileDropdown(projectileOptions, includeDefault: true);
            if (projectileDropdown != null) projectileDropdown.interactable = false;
            return;
        }
        
        // Get all projectiles matching the weapon's category
        projectileOptions = currentCiv.GetAvailableProjectiles(selectedWeapon.projectileCategory);
        
        // Populate dropdown
        PopulateProjectileDropdown(projectileOptions, includeDefault: true);
        if (projectileDropdown != null) projectileDropdown.interactable = true;
    }
    
    /// <summary>
    /// Populates a projectile dropdown with options
    /// </summary>
    private void PopulateProjectileDropdown(List<GameCombat.ProjectileData> items, bool includeDefault)
    {
        if (projectileDropdown == null)
            return;
            
        projectileDropdown.ClearOptions();
        var opts = new List<TMP_Dropdown.OptionData>();
        
        if (includeDefault) opts.Add(new TMP_Dropdown.OptionData("(Use Weapon Default)"));
        
        foreach (var p in items)
        {
            if (p == null) continue;
            string label = p.projectileName;
            // Show inventory count
            int count = currentCiv != null ? currentCiv.GetProjectileCount(p) : 0;
            if (count > 0) label += $"  x{count}";
            opts.Add(new TMP_Dropdown.OptionData(label));
        }
        
        projectileDropdown.AddOptions(opts);
        projectileDropdown.value = 0; // default to weapon default
    }

    private EquipmentData GetSelectionFrom(TMP_Dropdown dropdown, List<EquipmentData> source)
    {
        if (dropdown == null || source == null) return null;
        int idx = dropdown.value;
        if (idx <= 0) return null; // 0 is None
        int listIndex = idx - 1;
        if (listIndex < 0 || listIndex >= source.Count) return null;
        return source[listIndex];
    }
    
    /// <summary>
    /// Gets the selected projectile from the projectile dropdown
    /// </summary>
    private GameCombat.ProjectileData GetProjectileSelection()
    {
        if (projectileDropdown == null || projectileOptions == null) return null;
        int idx = projectileDropdown.value;
        if (idx <= 0) return null; // 0 is "Use Weapon Default"
        int listIndex = idx - 1;
        if (listIndex < 0 || listIndex >= projectileOptions.Count) return null;
        return projectileOptions[listIndex];
    }

    private void ApplySelectionToAllUnitsOfType()
    {
        if (currentCiv == null) return;
        var unitData = GetSelectedUnitData();
        if (unitData == null) return;

        var weapon = GetSelectionFrom(weaponDropdown, weaponOptions);
        var shield = GetSelectionFrom(shieldDropdown, shieldOptions);
        var armor  = GetSelectionFrom(armorDropdown,  armorOptions);
        var misc   = GetSelectionFrom(miscDropdown,   miscOptions);
        var projectile = GetProjectileSelection(); // NEW: Get selected projectile

        int changed = 0;
        foreach (var u in currentCiv.combatUnits)
        {
            if (u == null || u.data != unitData) continue;

            // Equip or unequip per slot. We do not consume inventory here; treat as a configuration panel.
            if (weapon != null) u.EquipItem(weapon); else u.UnequipItem(EquipmentType.Weapon);
            if (shield != null) u.EquipItem(shield); else u.UnequipItem(EquipmentType.Shield);
            if (armor  != null) u.EquipItem(armor);  else u.UnequipItem(EquipmentType.Armor);
            if (misc   != null) u.EquipItem(misc);   else u.UnequipItem(EquipmentType.Miscellaneous);
            
            // NEW: Set active projectile (null = use weapon default)
            u.ActiveProjectile = projectile;
            
            changed++;
        }

        // Feedback
        if (UIManager.Instance != null)
        {
            if (changed > 0)
            {
                string msg = $"Applied equipment to {changed} {unitData.unitName}(s)";
                if (projectile != null)
                    msg += $" (using {projectile.projectileName})";
                UIManager.Instance.ShowNotification(msg);
            }
            else
            {
                UIManager.Instance.ShowNotification($"No {unitData.unitName} units to update.");
            }
        }
    }
}
