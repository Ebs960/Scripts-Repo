# Battle Scene Setup Guide

## 1. Create Battle Scene
1. **File → New Scene** (or duplicate an existing scene)
2. **Save as**: `BattleScene` in your Scenes folder
3. **Set as Additive**: This allows loading on top of the main game

## 2. Essential GameObjects to Add

### A. BattleManager (Required)
```
BattleManager (Empty GameObject)
├── Add Component: BattleManager
├── Set Battle Settings:
    - Battle Map Size: 50x50
    - Unit Spacing: 2.0
    - Formation Spacing: 3.0
    - Pause Key: Escape
```

### B. BattleMapGenerator (Required)
```
BattleMapGenerator (Empty GameObject)
├── Add Component: BattleMapGenerator
├── Set Map Settings:
    - Map Size: 50
    - Terrain Height: 1.0
    - Terrain Variation: 0.5
```

### C. BattleUI (Required)
```
BattleUI (Canvas)
├── Add Component: BattleUI
├── Create UI Elements:
    - Pause Panel (Panel)
    - Unit Selection UI (Panel)
    - Battle Info Panel (Panel)
```

### D. Camera Setup
```
BattleCamera (Camera)
├── Position: (0, 20, -20)
├── Rotation: (30, 0, 0)
├── Add Component: Camera Controller (if you have one)
```

## 3. Lighting Setup
- **Directional Light**: Main sun light
- **Ambient Light**: Soft fill light
- **Fog**: Optional atmospheric effect

## 4. Ground Plane
```
Ground (Plane)
├── Scale: (50, 1, 50)
├── Material: Battle terrain material
├── Position: (0, 0, 0)
```

## 5. Unit Spawn Points
```
SpawnPoints (Empty GameObject)
├── AttackerSpawn (Empty GameObject)
│   └── Position: (-20, 0, 0)
├── DefenderSpawn (Empty GameObject)
│   └── Position: (20, 0, 0)
```

## 6. Prefab Setup
Make sure you have these prefabs ready:
- CombatUnit prefabs
- Formation prefabs
- Projectile prefabs
- UI prefabs

## 7. Test Setup
Add a test button to start battles:
```
TestButton (Button)
├── Add Component: BattleTestButton
├── Connect to GameManager.StartBattleTest()
```
