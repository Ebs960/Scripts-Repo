# Projectile System - UI Setup Guide

## ✅ What's Been Implemented

### **Backend (Complete)**
- ✅ `ProjectileData` - Production fields, requirements, `CanBeProducedBy()`
- ✅ `EquipmentData` - `projectileCategory`, `usesProjectiles` fields
- ✅ `Civilization` - Projectile inventory management
- ✅ `City` - Projectile production in queue
- ✅ `CombatUnit` & `WorkerUnit` - `ActiveProjectile` field
- ✅ Firing logic - Uses active projectile or weapon default

### **UI Code (Complete)**
- ✅ `EquipmentManagerPanel.cs` - Projectile dropdown for unit equipment
- ✅ `CityUI.cs` - Projectile production in city build options

---

## 🎨 UI Setup Required in Unity Editor

### **1. Equipment Manager Panel**
You need to add ONE new dropdown to your existing Equipment Manager UI:

**Inspector Setup:**
1. Open your Equipment Manager Panel prefab/scene object
2. Find the `EquipmentManagerPanel` component
3. Add a new **TMP_Dropdown** to your UI (duplicate the weapon dropdown)
4. Rename it: **"Projectile Dropdown"**
5. Assign it to the **`projectileDropdown`** field in the Inspector

**Recommended UI Layout:**
```
Equipment Manager Panel
├── Unit Type Dropdown
├── Weapon Dropdown
├── Projectile Dropdown ← NEW! Place this right after weapon
├── Shield Dropdown
├── Armor Dropdown
├── Misc Dropdown
└── Apply to All Button
```

**UI Behavior:**
- When weapon selected: Shows compatible projectiles
- When no weapon: Disabled
- When weapon doesn't use projectiles: Disabled
- Default option: "(Use Weapon Default)"
- Shows inventory count: "Copper Arrows  x10"

---

### **2. City Production UI**
You need to add ONE new container to your City UI:

**Inspector Setup:**
1. Open your City UI prefab/scene object
2. Find the `CityUI` component
3. Add a new **Transform** container (duplicate your equipment container)
4. Rename it: **"Projectiles Container"**
5. Assign it to the **`projectilesContainer`** field in the Inspector

**Recommended UI Layout:**
```
City Production Panel
├── Buildings Tab
│   └── Buildings Container (existing)
├── Units Tab
│   └── Units Container (existing)
├── Equipment Tab
│   └── Equipment Container (existing)
└── Projectiles Tab ← NEW! Add this as a 4th tab
    └── Projectiles Container ← NEW! Add this container
```

**Alternatively (Simpler):**
If you don't want a separate tab, add projectiles to the Equipment tab:
```
Equipment Tab
├── Equipment Container (existing)
└── Projectiles Container ← NEW! Add below equipment
```

---

## 📦 Required Assets Setup

### **Create a Resources Folder for Projectiles**
ProjectileData assets must be in `Resources/Projectiles/` so the UI can find them:

```
Assets/
└── Resources/
    └── Projectiles/
        ├── StoneArrow.asset
        ├── CopperArrow.asset
        ├── IronArrow.asset
        ├── SteelArrow.asset
        └── etc...
```

**Why?** The CityUI loads projectiles dynamically:
```csharp
var allProjectiles = Resources.LoadAll<GameCombat.ProjectileData>("Projectiles");
```

---

## 🎯 Testing Checklist

### **1. Create Test Projectiles**
Create these ScriptableObject assets:

#### **Stone Arrow** (Default/Free)
```
Type: ProjectileData
Path: Resources/Projectiles/StoneArrow.asset

Settings:
- Projectile Name: "Stone Arrows"
- Icon: [arrow icon]
- Category: Arrow
- Production Cost: 5
- Gold Cost: 25
- Required Techs: (none)
- Required Resources: (none)
- Damage: 10
```

#### **Copper Arrow** (Upgrade)
```
Type: ProjectileData
Path: Resources/Projectiles/CopperArrow.asset

Settings:
- Projectile Name: "Copper Arrows"
- Icon: [copper arrow icon]
- Category: Arrow
- Production Cost: 10
- Gold Cost: 50
- Required Techs: [Bronze Working]
- Required Resources: [Copper]
- Damage: 15
```

### **2. Create Test Weapon**
Create a weapon that uses projectiles:

#### **Basic Bow**
```
Type: EquipmentData

Settings:
- Equipment Name: "Bow"
- Equipment Type: Weapon
- Uses Projectiles: TRUE ← CRITICAL!
- Projectile Category: Arrow
- Projectile Data: Stone Arrow (default fallback)
- Production Cost: 20
- Required Techs: [Archery]
```

### **3. In-Game Test Flow**
1. **Start Game**
2. **Research Bronze Working** (unlocks copper arrows)
3. **Open City UI** → Go to Projectiles tab
4. **See Copper Arrows** in production list (shows "Owned: 0")
5. **Click Copper Arrow** → Adds to production queue
6. **Wait for production** → Copper arrows added to inventory
7. **Open Equipment Manager**
8. **Select "Archer" unit type**
9. **Select "Bow" weapon** → Projectile dropdown activates
10. **See dropdown options:**
    - (Use Weapon Default)
    - Copper Arrows  x10
11. **Select Copper Arrows** → Click Apply
12. **Notification:** "Applied equipment to 3 Archer(s) (using Copper Arrows)"
13. **Units now fire copper arrows!** ✨

---

## 🔧 Troubleshooting

### **Problem: Projectile dropdown stays disabled**
**Cause:** Weapon doesn't have `usesProjectiles = true`
**Fix:** Check weapon EquipmentData, ensure `usesProjectiles` is checked

### **Problem: No projectiles show in City UI**
**Cause:** Projectiles not in `Resources/Projectiles/` folder
**Fix:** Move all ProjectileData assets to `Resources/Projectiles/`

### **Problem: Projectiles don't match weapon**
**Cause:** Projectile category doesn't match weapon's `projectileCategory`
**Fix:** Ensure weapon is `Arrow` and projectile is also `Arrow` category

### **Problem: Units still use old projectiles**
**Cause:** ActiveProjectile not set
**Fix:** Open Equipment Manager, select weapon, then select projectile, click Apply

---

## 🎨 Optional: Improve UI Appearance

### **Add Category Label**
Add a TextMeshProUGUI above projectile dropdown:
```
"Projectile Type: [Dropdown shows here]"
```

### **Add Info Tooltip**
Show projectile stats on hover:
- Damage
- Speed
- Special effects

### **Add Visual Feedback**
When projectile selected, show:
- ✅ Checkmark next to active projectile
- 🎯 Preview of projectile visuals
- 📊 Damage comparison vs default

---

## 📊 Summary

| Component | File | Status | UI Setup Required |
|-----------|------|--------|-------------------|
| ProjectileData | ✅ Done | ✅ Complete | Create assets in `Resources/Projectiles/` |
| Civilization Inventory | ✅ Done | ✅ Complete | None |
| City Production | ✅ Done | ✅ Complete | Add `projectilesContainer` in Inspector |
| Equipment Manager | ✅ Done | ✅ Complete | Add `projectileDropdown` in Inspector |
| Unit Firing Logic | ✅ Done | ✅ Complete | None |

---

## 🚀 You're Done When...

✅ You can open City UI and see projectiles in production list  
✅ You can queue projectile production  
✅ Projectiles are added to civilization inventory on completion  
✅ You can open Equipment Manager  
✅ You can select a weapon → Projectile dropdown activates  
✅ You can select a projectile → Apply to units  
✅ Units fire the selected projectile (visual & damage change)  

**That's it!** The system is fully functional! 🎉

