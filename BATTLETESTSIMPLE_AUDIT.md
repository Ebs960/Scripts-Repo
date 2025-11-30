# BattleTestSimple Inspector Fields Audit

## Current Inspector Fields (Organized by Header)

### [Header("Battle Manager Settings")]
1. **battleMapSize** (float = 100f)
   - Purpose: Size of the battle map in world units
   - Used in: Map generation, spawn point calculation
   - Consolidation: Could be moved to BattleMapGenerator or a shared BattleSettings object

2. **formationGroupSpacing** (float = 20f)
   - Purpose: Distance between attacker and defender formation groups
   - Used in: Formation positioning
   - Consolidation: Could be part of FormationSettings

3. **pauseKey** (KeyCode = Escape)
   - Purpose: Key to pause/resume battle
   - Used in: Update() method for pause handling
   - Consolidation: Could be in InputSettings or GameSettings

4. **selectionIndicatorPrefab** (GameObject)
   - Purpose: Prefab for unit selection indicator
   - Used in: Selection system
   - Consolidation: Could be in SelectionSettings

### [Header("Battle State")]
5. **battleInProgress** (bool = false)
   - Purpose: Runtime state - is battle active?
   - Used in: Various checks throughout code
   - Note: Runtime only, shouldn't be in Inspector (but useful for debugging)

6. **isPaused** (bool = false)
   - Purpose: Runtime state - is battle paused?
   - Used in: Pause/resume logic
   - Note: Runtime only, shouldn't be in Inspector (but useful for debugging)

### [Header("Battle UI Integration")]
7. **battleUI** (BattleUI)
   - Purpose: Reference to BattleUI component
   - Used in: UI management
   - Consolidation: None needed - this is a component reference

### [Header("AI System")]
8. **formationAIManager** (FormationAIManager)
   - Purpose: AI manager for formation-based AI
   - Used in: AI control
   - Consolidation: None needed - this is a component reference

### [Header("UI")]
9. **testButton** (Button)
   - Purpose: Button to start test battle
   - Used in: UI interaction
   - Consolidation: Could be in UISettings

10. **statusText** (TextMeshProUGUI)
    - Purpose: Status text display
    - Used in: Status updates
    - Consolidation: Could be in UISettings

11. **uiPanel** (GameObject)
    - Purpose: Panel to hide when battle starts
    - Used in: UI visibility
    - Consolidation: Could be in UISettings

12. **attackerUnitDropdown** (TMP_Dropdown)
    - Purpose: Dropdown for selecting attacker unit
    - Used in: Unit selection UI
    - Consolidation: Could be in UnitSelectionUISettings

13. **defenderUnitDropdown** (TMP_Dropdown)
    - Purpose: Dropdown for selecting defender unit
    - Used in: Unit selection UI
    - Consolidation: Could be in UnitSelectionUISettings

14. **attackerLabel** (TextMeshProUGUI)
    - Purpose: Label for attacker
    - Used in: UI display
    - Consolidation: Could be in UISettings

15. **defenderLabel** (TextMeshProUGUI)
    - Purpose: Label for defender
    - Used in: UI display
    - Consolidation: Could be in UISettings

### [Header("Debug")]
16. **showDebugLogs** (bool = true)
    - Purpose: Enable/disable debug logging
    - Used in: DebugLog() method
    - Consolidation: Could be in DebugSettings (shared across all scripts)

### [Header("Camera Controls")]
17. **cameraMoveSpeed** (float = 5f)
    - Purpose: Camera movement speed
    - Used in: Camera control
    - Consolidation: Could be in CameraSettings

18. **cameraZoomSpeed** (float = 2f)
    - Purpose: Camera zoom speed
    - Used in: Camera control
    - Consolidation: Could be in CameraSettings

19. **cameraRotateSpeed** (float = 50f)
    - Purpose: Camera rotation speed
    - Used in: Camera control
    - Consolidation: Could be in CameraSettings

20. **minZoom** (float = 2f)
    - Purpose: Minimum camera zoom
    - Used in: Camera control
    - Consolidation: Could be in CameraSettings

21. **maxZoom** (float = 20f)
    - Purpose: Maximum camera zoom
    - Used in: Camera control
    - Consolidation: Could be in CameraSettings

### [Header("Civilization Selection")]
22. **attackerCivDropdown** (TMP_Dropdown)
    - Purpose: Dropdown for selecting attacker civilization
    - Used in: Civilization selection UI
    - Consolidation: Could be in CivilizationSelectionUISettings

23. **defenderCivDropdown** (TMP_Dropdown)
    - Purpose: Dropdown for selecting defender civilization
    - Used in: Civilization selection UI
    - Consolidation: Could be in CivilizationSelectionUISettings

24. **attackerCivLabel** (TextMeshProUGUI)
    - Purpose: Label for attacker civilization
    - Used in: UI display
    - Consolidation: Could be in UISettings

25. **defenderCivLabel** (TextMeshProUGUI)
    - Purpose: Label for defender civilization
    - Used in: UI display
    - Consolidation: Could be in UISettings

### [Header("Player/AI Control Selection")]
26. **attackerControlDropdown** (TMP_Dropdown)
    - Purpose: Dropdown for selecting attacker control (Player/AI)
    - Used in: Control selection UI
    - Consolidation: Could be in ControlSelectionUISettings

27. **defenderControlDropdown** (TMP_Dropdown)
    - Purpose: Dropdown for selecting defender control (Player/AI)
    - Used in: Control selection UI
    - Consolidation: Could be in ControlSelectionUISettings

28. **attackerControlLabel** (TextMeshProUGUI)
    - Purpose: Label for attacker control
    - Used in: UI display
    - Consolidation: Could be in UISettings

29. **defenderControlLabel** (TextMeshProUGUI)
    - Purpose: Label for defender control
    - Used in: UI display
    - Consolidation: Could be in UISettings

### [Header("Selection System")]
30. **selectionBoxMaterial** (Material)
    - Purpose: Material for selection box
    - Used in: Selection visualization
    - Consolidation: Could be in SelectionSettings

31. **selectionColor** (Color)
    - Purpose: Color for selection box
    - Used in: Selection visualization
    - Consolidation: Could be in SelectionSettings

32. **selectedUnitColor** (Color)
    - Purpose: Color for selected units
    - Used in: Selection visualization
    - Consolidation: Could be in SelectionSettings

### [Header("Formation Settings")]
33. **formationsPerSide** (int = 3)
    - Purpose: Number of formations per side
    - Used in: Formation creation
    - Consolidation: Could be in FormationSettings

34. **soldiersPerFormation** (int = 9)
    - Purpose: Number of soldiers per formation
    - Used in: Formation creation
    - Consolidation: Could be in FormationSettings

35. **formationSpacing** (float = 2f)
    - Purpose: Spacing between soldiers in formation
    - Used in: Formation creation
    - Consolidation: Could be in FormationSettings

### [Header("Battle Map")]
36. **mapGenerator** (BattleMapGenerator)
    - Purpose: Reference to battle map generator
    - Used in: Map generation
    - Consolidation: None needed - this is a component reference
    - **NOTE: This is where we need to add biome/battle type/hilliness controls**

37. **victoryManager** (BattleVictoryManager)
    - Purpose: Reference to victory manager
    - Used in: Victory condition handling
    - Consolidation: None needed - this is a component reference

38. **generateNewMap** (bool = true)
    - Purpose: Generate new map for each battle
    - Used in: Map generation logic
    - Consolidation: Could be in MapSettings

### [Header("Grounding")]
39. **battlefieldLayers** (LayerMask = ~0)
    - Purpose: Layers considered battlefield ground
    - Used in: Raycast grounding
    - Consolidation: **DUPLICATE** - Also exists in BattleMapGenerator! Should be shared or removed from one

## Consolidation Opportunities

### 1. **UI Settings Group**
   - All UI-related fields (testButton, statusText, uiPanel, dropdowns, labels)
   - Could create a `BattleUISettings` ScriptableObject or nested class
   - **Impact**: Reduces clutter, easier to manage UI references

### 2. **Camera Settings Group**
   - All camera-related fields (moveSpeed, zoomSpeed, rotateSpeed, minZoom, maxZoom)
   - Could create a `CameraSettings` ScriptableObject or nested class
   - **Impact**: Reduces clutter, reusable across scenes

### 3. **Formation Settings Group**
   - All formation-related fields (formationsPerSide, soldiersPerFormation, formationSpacing)
   - Could create a `FormationSettings` ScriptableObject or nested class
   - **Impact**: Reduces clutter, easier to configure different formation types

### 4. **Selection Settings Group**
   - All selection-related fields (selectionBoxMaterial, selectionColor, selectedUnitColor)
   - Could create a `SelectionSettings` ScriptableObject or nested class
   - **Impact**: Reduces clutter, easier to theme

### 5. **Duplicate battlefieldLayers**
   - Exists in both BattleTestSimple and BattleMapGenerator
   - Should be removed from one or shared via reference
   - **Impact**: Prevents confusion, ensures consistency

### 6. **Battle Map Settings**
   - battleMapSize, generateNewMap could be moved to BattleMapGenerator
   - Or create a shared BattleMapSettings object
   - **Impact**: Better organization, single source of truth

### 7. **Runtime State Fields**
   - battleInProgress, isPaused are runtime-only
   - Should be [HideInInspector] or moved to a separate runtime state class
   - **Impact**: Cleaner Inspector, less confusion

## Missing Features (To Add)

### 1. **Biome Selection**
   - Need: Dropdown or enum field to select biome for battle test
   - Should: Pass to mapGenerator.primaryBattleBiome
   - Location: Add to [Header("Battle Map")] section

### 2. **Battle Type Selection**
   - Need: Dropdown or enum field to select battle type (Land, Naval, Coastal, Siege)
   - Should: Pass to mapGenerator.battleType
   - Location: Add to [Header("Battle Map")] section

### 3. **Hilliness Control**
   - Need: Slider or toggle to control terrain hilliness
   - Should: Modify terrain generator parameters or create a modifier
   - Options:
     a) Direct control: Slider (0-1) that modifies generator's hilliness parameter
     b) Preset control: Dropdown (Flat, Gentle, Moderate, Hilly, Mountainous)
     c) Toggle: Simple boolean (Flat vs Hilly)
   - Location: Add to [Header("Battle Map")] section
   - Implementation: Need to pass to terrain generator or modify BiomeNoiseProfile

## Recommended Structure After Consolidation

```
[Header("Battle Map Settings")]
- mapGenerator (BattleMapGenerator) - Component reference
- victoryManager (BattleVictoryManager) - Component reference
- battleMapSize (float) - Map size
- generateNewMap (bool) - Generate new map each time
- **NEW: selectedBiome (Biome)** - Biome for battle test
- **NEW: selectedBattleType (BattleType)** - Battle type
- **NEW: terrainHilliness (float 0-1)** - Hilliness control
- **NEW: useCustomHilliness (bool)** - Override biome default hilliness

[Header("Formation Settings")]
- formationsPerSide (int)
- soldiersPerFormation (int)
- formationSpacing (float)
- formationGroupSpacing (float) - Move from Battle Manager Settings

[Header("Camera Settings")]
- cameraMoveSpeed (float)
- cameraZoomSpeed (float)
- cameraRotateSpeed (float)
- minZoom (float)
- maxZoom (float)

[Header("UI References")]
- All UI-related fields grouped here

[Header("Component References")]
- battleUI (BattleUI)
- formationAIManager (FormationAIManager)
- Other component references

[Header("Debug")]
- showDebugLogs (bool)
- battleInProgress (bool) [HideInInspector or RuntimeOnly]
- isPaused (bool) [HideInInspector or RuntimeOnly]
```

## Implementation Notes

1. **Biome Selection**: Use TMP_Dropdown populated with Biome enum values
2. **Battle Type Selection**: Use TMP_Dropdown populated with BattleType enum values
3. **Hilliness Control**: 
   - Option A: Slider (0-1) that directly modifies generator's hilliness
   - Option B: Preset dropdown (Flat, Gentle, Moderate, Hilly, Mountainous)
   - Recommendation: Slider for fine control, with preset buttons for quick selection
4. **Passing to MapGenerator**: In CreateSimpleTest() or StartTest(), set mapGenerator fields before calling GenerateBattleMap()

