# Real-Time Battle System Setup Guide

This guide explains how to set up and use the new real-time Total War style battle system.

## 🎯 **System Overview**

The battle system automatically triggers when units engage in combat, creating a tactical real-time battle scene where players can control their units directly.

## 🚀 **Quick Setup**

### **1. Scene Setup**
1. Create a new scene called "BattleScene"
2. Add an empty GameObject and attach `BattleManager` script
3. Add an empty GameObject and attach `BattleMapGenerator` script
4. Set up a camera for the battle view

### **2. Prefab Setup**
1. Create a `BattleUI` prefab with the required UI elements
2. Create a `SelectionIndicator` prefab for unit selection
3. Create terrain prefabs for different battle maps
4. Create obstacle prefabs (trees, rocks, etc.)

### **3. BattleManager Configuration**
```csharp
// In BattleManager inspector:
Battle Map Size: 100
Unit Spacing: 2
Formation Spacing: 20
Battle UI Prefab: [Assign your UI prefab]
Selection Indicator Prefab: [Assign selection indicator]
```

### **4. BattleMapGenerator Configuration**
```csharp
// In BattleMapGenerator inspector:
Tile Size: 2
Height Variation: 1
Noise Scale: 0.1
Obstacle Density: 0.1
```

## 🎮 **How It Works**

### **Battle Trigger**
- Battles automatically trigger when units attack each other
- Works with any number of units (even 1 vs 1)
- Triggers for different civilizations AND when attacking animals
- Animals get temporary civilizations for battle purposes

### **Battle Flow**
1. **Scene Transition**: Game loads "BattleScene"
2. **Map Generation**: Creates terrain based on biome
3. **Unit Positioning**: Units spawn in formations
4. **Battle Control**: Player controls units in real-time
5. **Battle Resolution**: Returns to main game with results

### **Unit Control**
- **Left Click**: Select units
- **Right Click**: Move/Attack
- **Ctrl+Click**: Add to selection
- **Space**: Pause/Resume
- **Escape**: Clear selection

## 🎨 **UI Elements Required**

### **BattleUI Components**
- `selectedUnitsText`: Shows number of selected units
- `formationText`: Shows current formation
- `attackButton`: Attack command
- `defendButton`: Defend command
- `chargeButton`: Charge command
- `retreatButton`: Retreat command
- `pauseButton`: Pause battle
- `unitListScrollRect`: List of all units
- `unitListItemPrefab`: Individual unit display
- `battleStatusText`: Battle status display

### **Unit List Item Prefab**
- `UnitName`: TextMeshPro for unit name
- `Health`: TextMeshPro for health display
- `HealthBar`: Slider for health bar
- Button component for selection

## 🗺️ **Map Generation**

### **Terrain Types**
- **Plains**: Open field, no bonuses
- **Hills**: Defense bonus, movement penalty
- **Forest**: Cover bonus, movement penalty
- **Water**: Only certain units can cross
- **Fortified**: Major defense bonus
- **Swamp**: Movement penalty, health damage

### **Obstacles**
- Trees, rocks, buildings
- Provide cover and block movement
- Configurable density

## ⚔️ **Formation System**

### **Formation Types**
- **Line**: Standard infantry formation
- **Square**: Defensive formation against cavalry
- **Wedge**: Offensive formation for breaking lines
- **Column**: Fast movement formation
- **Skirmish**: Loose formation for ranged units

### **Formation Controls**
- Dropdown to select formation
- Button to apply formation to selected units
- Automatic unit spacing and positioning

## 🎯 **Battle Mechanics**

### **Unit States**
- **Idle**: Standing still
- **Moving**: Moving to position
- **Attacking**: Engaging enemy
- **Defending**: Holding position
- **Routing**: Fleeing from battle
- **Dead**: Unit eliminated

### **Combat Resolution**
- Real-time damage calculation
- Morale system affects unit behavior
- Units can rout when morale drops
- Experience gained from combat

## 🔧 **Customization**

### **Battle Triggers**
The system now triggers battles for any engagement between different civilizations. The `ShouldStartBattle()` method in `CombatUnit.cs` is simplified:
```csharp
// Battles trigger for any attack between different civilizations
return true; // (after checking they're different civs)
```

### **Formation Spacing**
Adjust `unitSpacing` and `formationSpacing` in `BattleManager`:
```csharp
public float unitSpacing = 2f;        // Distance between units
public float formationSpacing = 20f;  // Distance between formations
```

### **Map Size**
Modify `battleMapSize` in `BattleManager`:
```csharp
public float battleMapSize = 100f;    // Size of battle map
```

## 🐛 **Troubleshooting**

### **Common Issues**

1. **Battle doesn't trigger**
   - Check if `BattleManager.Instance` exists
   - Verify unit strength requirements
   - Ensure units are from different civilizations

2. **Units don't spawn correctly**
   - Check `BattleMapGenerator` configuration
   - Verify spawn point prefabs
   - Ensure unit prefabs have `CombatUnit` component

3. **UI not working**
   - Verify all UI references are assigned
   - Check `BattleUI` prefab setup
   - Ensure UI elements have correct components

4. **Scene transition issues**
   - Verify "BattleScene" exists in Build Settings
   - Check scene loading code
   - Ensure proper scene names

### **Debug Commands**
```csharp
// Force start a battle (for testing)
BattleManager.Instance.StartBattle(attacker, defender, attackerUnits, defenderUnits);

// Check battle status
Debug.Log($"Battle in progress: {BattleManager.Instance.IsBattleInProgress}");
```

## 📈 **Performance Tips**

1. **Limit Unit Count**: Keep battles under 50 units total
2. **Use Object Pooling**: Pool projectiles and effects
3. **LOD System**: Reduce detail for distant units
4. **Culling**: Only update visible units

## 🎮 **Future Enhancements**

### **Planned Features**
- **AI Behavior**: Smarter enemy AI
- **Weather Effects**: Rain, snow, fog
- **Siege Battles**: City assault mechanics
- **Naval Battles**: Ship-to-ship combat
- **Multiplayer**: Online battle support

### **Advanced Features**
- **Formation AI**: Automatic formation changes
- **Tactical Pause**: Pause to issue orders
- **Replay System**: Record and replay battles
- **Statistics**: Detailed battle analytics

## 📚 **API Reference**

### **BattleManager**
- `StartBattle(attacker, defender, attackerUnits, defenderUnits)`
- `SelectUnits(units)`
- `MoveSelectedUnits(position)`
- `AttackTarget(target)`
- `EndBattle(result)`

### **CombatUnit Battle Methods**
- `InitializeForBattle(isAttacker)`
- `SetBattleState(state)`
- `MoveToPosition(position)`
- `AttackTarget(target)`

### **BattleMapGenerator**
- `GenerateBattleMap(size, attackerUnits, defenderUnits)`
- `GetRandomPosition()`
- `IsPositionInBounds(position)`
- `GetTerrainTypeAt(position)`

This battle system provides a solid foundation for real-time tactical combat that can be extended and customized based on your game's needs!
