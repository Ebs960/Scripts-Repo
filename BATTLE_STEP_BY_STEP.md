# Battle System - Step by Step Setup

## Phase 1: Basic Scene Setup

### Step 1: Create Battle Scene
1. **File → New Scene**
2. **Save as**: `BattleScene.unity`
3. **Set Build Settings**: Add to Build Settings as Scene 1

### Step 2: Add Essential GameObjects
1. **Create Empty GameObject**: `BattleManager`
   - Add Component: `BattleManager`
   - Set Battle Map Size: 50
   - Set Unit Spacing: 2.0
   - Set Formation Spacing: 3.0

2. **Create Empty GameObject**: `BattleMapGenerator`
   - Add Component: `BattleMapGenerator`
   - Set Map Size: 50
   - Set Terrain Height: 1.0

3. **Create Canvas**: `BattleUI`
   - Add Component: `BattleUI`
   - Set Canvas Scaler: Scale With Screen Size

### Step 3: Add Ground Plane
1. **Create Plane**: `Ground`
   - Scale: (50, 1, 50)
   - Position: (0, 0, 0)
   - Material: Create new material with grass texture

### Step 4: Add Camera
1. **Create Camera**: `BattleCamera`
   - Position: (0, 20, -20)
   - Rotation: (30, 0, 0)
   - Add Component: `Camera Controller` (if you have one)

## Phase 2: Unit Setup

### Step 5: Create CombatUnit Prefab
1. **Create Empty GameObject**: `CombatUnit`
   - Add Component: `CombatUnit`
   - Add Component: `Rigidbody`
   - Add Component: `Capsule Collider`

2. **Add Model Child**:
   - Create Child: `Model`
   - Add Mesh Renderer
   - Add Mesh Filter
   - Add Animator

3. **Add Equipment Holders**:
   - Create Child: `WeaponHolder` (Position: 0, 1, 0.5)
   - Create Child: `ShieldHolder` (Position: 0, 1, -0.5)
   - Create Child: `ProjectileSpawnPoint` (Position: 0, 1.5, 0.5)

4. **Save as Prefab**: `CombatUnit.prefab`

### Step 6: Create Formation Prefab
1. **Create Empty GameObject**: `Formation`
   - Add Component: `FormationManager`
   - Set Formation Size: 9
   - Set Formation Shape: Square

2. **Add Formation Members**:
   - Create 9 Child GameObjects: `FormationMember1` to `FormationMember9`
   - Each with `FormationMember` component
   - Position in 3x3 grid

3. **Save as Prefab**: `Formation.prefab`

## Phase 3: UI Setup

### Step 7: Create Battle UI
1. **Create Panel**: `PausePanel`
   - Add Button: `ResumeButton`
   - Add Button: `QuitButton`
   - Set initially inactive

2. **Create Panel**: `UnitInfoPanel`
   - Add Text: `UnitName`
   - Add Slider: `HealthBar`
   - Add Slider: `MoraleBar`
   - Add Text: `EquipmentInfo`

3. **Create Panel**: `BattleInfoPanel`
   - Add Text: `TurnCounter`
   - Add Text: `UnitCount`
   - Add Text: `BattleStatus`

### Step 8: Connect UI Scripts
1. **Select BattleUI Canvas**
2. **Add Component**: `BattleUI`
3. **Drag UI elements** to the script fields
4. **Set up button events** in the inspector

## Phase 4: AI Setup

### Step 9: Add AI Components
1. **Select CombatUnit Prefab**
2. **Add Component**: `BattleAI`
3. **Add Component**: `BehaviorTree`
4. **Add Component**: `EnhancedTargetSelection`
5. **Add Component**: `TacticalScripts`

### Step 10: Configure AI Settings
1. **BattleAI**:
   - Set AI State: Idle
   - Set Decision Interval: 1.0
   - Set Target Search Range: 10.0

2. **BehaviorTree**:
   - Set Root Node: Selector
   - Add Child Nodes: Attack, Move, Defend, Retreat

3. **EnhancedTargetSelection**:
   - Set Learning Rate: 0.1
   - Set Memory Decay: 0.95

## Phase 5: Testing Setup

### Step 11: Create Test Script
1. **Create Empty GameObject**: `BattleTestSetup`
2. **Add Component**: `BattleTestSetup`
3. **Assign References**:
   - BattleManager
   - BattleMapGenerator
   - BattleUI
   - Test Unit Data

### Step 12: Add Test UI
1. **Create Button**: `StartBattleButton`
2. **Create Button**: `PauseButton`
3. **Create Text**: `StatusText`
4. **Connect to BattleTestSetup** script

### Step 13: Test the System
1. **Play the scene**
2. **Click Start Battle Button**
3. **Test controls**:
   - Right-click to move/attack
   - Escape to pause
   - Check AI behavior

## Phase 6: Polish and Optimization

### Step 14: Add Visual Effects
1. **Add Particle Systems** for projectiles
2. **Add Trail Renderers** for arrows
3. **Add Impact Effects** for hits
4. **Add Death Animations** for units

### Step 15: Add Audio
1. **Create AudioSource** for music
2. **Create AudioSource** for SFX
3. **Add Audio Clips** for:
   - Battle music
   - Weapon sounds
   - Impact sounds
   - UI sounds

### Step 16: Performance Optimization
1. **Set up Object Pooling** for projectiles
2. **Optimize AI Update Frequency**
3. **Add LOD System** for distant units
4. **Use Occlusion Culling** for large battles

## Phase 7: Integration with Main Game

### Step 17: Connect to GameManager
1. **Add Battle Test Button** to main menu
2. **Connect to GameManager.StartBattleTest()**
3. **Test scene transitions**

### Step 18: Save and Load
1. **Add Battle Save System**
2. **Add Battle Load System**
3. **Test save/load functionality**

## Troubleshooting

### Common Issues:
1. **Units not spawning**: Check CombatUnitData assignment
2. **AI not working**: Check BehaviorTree setup
3. **UI not responding**: Check button event connections
4. **Performance issues**: Check object pooling setup

### Debug Tips:
1. **Use Debug.Log** for AI decisions
2. **Use Gizmos** for formation visualization
3. **Use Profiler** for performance analysis
4. **Use Console** for error checking
