# Battle System Prefab Setup

## 1. CombatUnit Prefab Setup

### A. Basic CombatUnit Prefab
```
CombatUnit (Prefab)
├── Model (Child GameObject)
│   ├── Mesh Renderer
│   ├── Mesh Filter
│   └── Animator
├── Collider (Capsule or Box)
├── Rigidbody
├── CombatUnit Script
├── BattleAI Script (for AI units)
├── BehaviorTree Script
├── EnhancedTargetSelection Script
└── TacticalScripts Script
```

### B. Equipment Attachment Points
```
CombatUnit (Prefab)
├── WeaponHolder (Empty GameObject)
│   └── Position: (0, 1, 0.5)
├── ShieldHolder (Empty GameObject)
│   └── Position: (0, 1, -0.5)
├── ArmorHolder (Empty GameObject)
│   └── Position: (0, 0, 0)
├── ProjectileWeaponHolder (Empty GameObject)
│   └── Position: (0, 1, 0.3)
└── ProjectileSpawnPoint (Empty GameObject)
    └── Position: (0, 1.5, 0.5)
```

## 2. Formation Prefab Setup

### A. Formation Member Prefab
```
FormationMember (Prefab)
├── Model (Child GameObject)
├── Collider
├── FormationMember Script
└── Simple AI Script
```

### B. Formation Container
```
Formation (Prefab)
├── FormationManager Script
├── FormationMember (Prefab) × 9
└── Formation Shape Settings
```

## 3. Projectile Prefab Setup

### A. Basic Projectile
```
Projectile (Prefab)
├── Model (Child GameObject)
├── Collider (Trigger)
├── Rigidbody
├── Projectile Script
├── IPoolable Interface
└── Particle System (Trail)
```

### B. Projectile Types
- Arrow (straight line)
- Bolt (fast, straight)
- Bullet (very fast, straight)
- Shell (arc trajectory)
- Rocket (guided)
- Javelin (thrown arc)

## 4. Battle UI Prefab Setup

### A. BattleUI Canvas
```
BattleUI (Canvas)
├── PausePanel (Panel)
│   ├── ResumeButton
│   ├── QuitButton
│   └── SettingsButton
├── UnitInfoPanel (Panel)
│   ├── UnitName (Text)
│   ├── HealthBar (Slider)
│   ├── MoraleBar (Slider)
│   └── EquipmentInfo (Text)
├── BattleInfoPanel (Panel)
│   ├── TurnCounter (Text)
│   ├── UnitCount (Text)
│   └── BattleStatus (Text)
└── Minimap (Panel)
    ├── MinimapCamera
    └── MinimapImage
```

## 5. Terrain Prefab Setup

### A. Battle Terrain
```
BattleTerrain (Prefab)
├── Ground (Plane)
├── Terrain (Terrain)
├── Obstacles (Empty GameObject)
│   ├── Rock1 (Cube)
│   ├── Rock2 (Cube)
│   └── Tree1 (Cylinder)
└── Cover (Empty GameObject)
    ├── Wall1 (Cube)
    └── Wall2 (Cube)
```

## 6. Audio Setup

### A. Battle Audio Manager
```
BattleAudio (Empty GameObject)
├── AudioSource (Music)
├── AudioSource (SFX)
├── AudioSource (UI)
└── BattleAudioManager Script
```

## 7. Lighting Setup

### A. Battle Lighting
```
BattleLighting (Empty GameObject)
├── Directional Light (Sun)
├── Point Light (Ambient)
├── Spot Light (Dramatic)
└── Fog (Atmospheric)
```

## 8. Camera Setup

### A. Battle Camera
```
BattleCamera (Camera)
├── Camera Controller Script
├── Cinemachine Virtual Camera
└── Camera Shake Script
```

## 9. Test Setup

### A. Battle Test Scene
```
BattleTestScene (Scene)
├── BattleManager
├── BattleMapGenerator
├── BattleUI
├── BattleTestSetup
├── TestButton (UI)
└── StatusText (UI)
```

## 10. Prefab Variants

### A. Unit Variants
- Melee Unit (sword, shield)
- Ranged Unit (bow, crossbow)
- Cavalry Unit (horse, lance)
- Artillery Unit (cannon, ballista)

### B. Formation Variants
- Square Formation (3x3)
- Line Formation (1x9)
- Wedge Formation (triangular)
- Circle Formation (defensive)

### C. Terrain Variants
- Open Field (flat, no cover)
- Forest (trees, limited visibility)
- Hills (elevation changes)
- Urban (buildings, streets)
