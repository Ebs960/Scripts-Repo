# Projectile Production & Management System

## Overview
Projectiles are now **producible items** that civilizations can manufacture and assign to units. This allows for strategic progression (e.g., upgrading from stone arrows to copper arrows) without the micromanagement of ammo consumption.

---

## How It Works

### 1. **Projectiles as Producible Items**
- **ProjectileData** now has production fields: `productionCost`, `goldCost`, `requiredResources`, `requiredTechs`, `requiredCultures`
- Cities can produce projectiles just like equipment
- Civilizations track projectile inventory: `Dictionary<ProjectileData, int> projectileInventory`

### 2. **Projectile Categories**
Weapons specify which projectile category they accept:
- **Arrow** - Used by bows
- **Bolt** - Used by crossbows
- **Bullet** - Used by guns
- **Shell** - Used by artillery
- **Rocket** - Used by launchers
- **Javelin** - Used by spear throwers
- **Stone** - Used by slings
- **Laser** - Used by energy weapons
- **Plasma** - Used by plasma weapons
- **Magic** - Used by magical weapons

### 3. **Unit Active Projectile**
- Each unit (CombatUnit & WorkerUnit) has an `ActiveProjectile` field
- This is **separate from the weapon** - one bow can use different arrow types
- Set in the Equipment Management UI (player choice)
- When firing, the game uses this priority:
  1. **Unit's ActiveProjectile** (if it matches weapon's category)
  2. **Weapon's default ProjectileData** (fallback)

---

## Setting Up Projectiles

### For Projectile Assets (ProjectileData)
1. **Identity**: Name, icon, category (e.g., Arrow, Bolt, Bullet)
2. **Production**: `productionCost`, `goldCost`, `requiredResources`
3. **Requirements**: `requiredTechs`, `requiredCultures`
4. **Visuals**: Prefab, scale, trail, impact effects
5. **Damage**: Base damage, area of effect, status effects

**Example: Copper Arrows**
```
projectileName: "Copper Arrows"
category: Arrow
productionCost: 10
goldCost: 50
requiredTechs: [Bronze Working]
requiredResources: [Copper]
damage: 15
```

### For Weapon Assets (EquipmentData)
1. **Projectile Setup**:
   - `usesProjectiles` = **true** (enables projectile system)
   - `projectileCategory` = **Arrow** (what ammo it accepts)
   - `projectileData` = **Default Arrow** (fallback if no active projectile)

**Example: Bow**
```
equipmentName: "Bow"
usesProjectiles: true
projectileCategory: Arrow
projectileData: Stone Arrow (default)
```

---

## City Production

### Queueing Projectile Production
Cities can produce projectiles through their production queue:
```csharp
// In City UI
city.QueueProduction(copperArrowProjectileData);
```

### Production Completion
When production finishes, projectiles are added to civilization inventory:
```csharp
// Automatic on completion
owner.AddProjectile(projectile, 1); // Adds 1 unit to inventory
```

---

## Civilization Inventory Management

### Adding Projectiles
```csharp
civilization.AddProjectile(projectileData, count);
```

### Checking Availability
```csharp
int copperArrowCount = civilization.GetProjectileCount(copperArrowData);
bool hasArrows = civilization.HasProjectile(copperArrowData, 10);
```

### Getting Available Projectiles by Category
```csharp
// Get all arrow types the civ has produced
List<ProjectileData> availableArrows = civilization.GetAvailableProjectiles(ProjectileCategory.Arrow);
```

---

## Unit Projectile Assignment

### Setting Active Projectile
```csharp
// In Equipment Management UI
unit.ActiveProjectile = copperArrowData;
```

### How Firing Works
When a unit fires a ranged weapon:
1. Check if `unit.ActiveProjectile` exists and matches `weapon.projectileCategory`
2. If yes → **Use unit's active projectile**
3. If no → **Use weapon's default projectile**

```csharp
// Automatic in SpawnProjectileFromEquipment()
GameCombat.ProjectileData projectileToUse = null;

if (unit.ActiveProjectile != null && weapon.usesProjectiles && 
    unit.ActiveProjectile.category == weapon.projectileCategory)
{
    projectileToUse = unit.ActiveProjectile; // USE ACTIVE
}
else if (weapon.projectileData != null)
{
    projectileToUse = weapon.projectileData; // FALLBACK TO DEFAULT
}
```

---

## UI Integration (To Be Implemented)

### Equipment Management Panel
**New Section: "Active Projectile"**
- Show dropdown for projectile category (e.g., "Arrows")
- List all available projectiles from `civilization.GetAvailableProjectiles(category)`
- Allow player to select which projectile this unit uses
- Update `unit.ActiveProjectile` on selection

### City Production Panel
**Add "Projectiles" Tab**
- Show all projectiles available to produce
- Check `projectile.CanBeProducedBy(civilization)`
- Queue production like equipment

---

## Example Progression Flow

### 1. Early Game (Stone Age)
- Player starts with **Stone Arrows** (default on bows)
- No production needed - weapons use default projectile

### 2. Tech Research
- Player researches **"Bronze Working"**
- Unlocks **Copper Arrow** projectile

### 3. Production
- Player queues **Copper Arrows** in city
- City produces them over several turns
- Copper arrows added to civilization inventory

### 4. Equipment Assignment
- Player opens Equipment Management UI
- Selects archer unit
- Changes "Active Projectile" from **Stone Arrow** → **Copper Arrow**
- Unit now fires copper arrows (more damage!)

### 5. Strategic Choice
- Player can mix unit types:
  - Some archers use stone arrows (free fallback)
  - Elite archers use copper arrows (better damage)
- No ammo consumption - it's a loadout choice, not resource drain

---

## Key Benefits

✅ **Strategic Depth** - Choice matters (better projectiles = better units)  
✅ **No Micromanagement** - No ammo consumption per shot  
✅ **Tech Progression** - Unlocking better projectiles feels meaningful  
✅ **Flexible System** - One weapon type works with multiple ammo types  
✅ **Simple UI** - Just a dropdown in equipment panel  

---

## Future Expansion Ideas

### Optional: Ammo Consumption
If you want ammo depletion later:
```csharp
// In SpawnProjectileFromEquipment()
if (owner != null)
{
    owner.ConsumeProjectile(projectileToUse, 1); // Consume 1 ammo per shot
}
```

### Optional: Auto-Upgrade All Units
Add a civilization-wide "default projectile" setting:
```csharp
public Dictionary<ProjectileCategory, ProjectileData> defaultProjectiles;

// When tech unlocks new projectile, auto-assign to all units
```

---

## Files Modified

1. **ProjectileData.cs** - Added production fields & `CanBeProducedBy()`
2. **EquipmentData.cs** - Added `projectileCategory` & `usesProjectiles`
3. **Civilization.cs** - Added projectile inventory & management methods
4. **City.cs** - Added projectile production to queue
5. **CombatUnit.cs** - Added `ActiveProjectile` field & updated firing logic
6. **WorkerUnit.cs** - Added `ActiveProjectile` field & updated firing logic

---

## Testing Checklist

- [ ] Create Stone Arrow ProjectileData (category: Arrow)
- [ ] Create Copper Arrow ProjectileData (category: Arrow, requires Bronze Working)
- [ ] Create Bow EquipmentData (usesProjectiles: true, category: Arrow, default: Stone Arrow)
- [ ] Research Bronze Working
- [ ] Queue Copper Arrows in city
- [ ] Verify copper arrows added to civilization inventory
- [ ] Equip bow to unit
- [ ] Verify unit fires stone arrows (default)
- [ ] Open equipment UI and set ActiveProjectile to Copper Arrows
- [ ] Verify unit now fires copper arrows

---

**System Status: ✅ FULLY IMPLEMENTED**  
Ready for UI integration!

