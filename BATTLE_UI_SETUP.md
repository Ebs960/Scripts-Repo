# Battle UI Setup Guide

This guide shows you exactly how to set up the simplified battle UI in Unity.

## 🎯 **Required UI Elements**

Create a Canvas in your BattleScene with these elements:

### **Main UI Panel**
```
Canvas
└── BattleUIPanel (Image with semi-transparent background)
    ├── SelectedUnitsText (TextMeshPro) - "Selected Units: 0"
    ├── FormationText (TextMeshPro) - "Formation: Line"
    ├── InstructionsText (TextMeshPro) - "Left Click: Select Units\nRight Click: Move/Attack\nEscape: Pause/Resume"
    ├── BattleStatusText (TextMeshPro) - "BATTLE IN PROGRESS"
    ├── FormationDropdown (Dropdown)
    ├── ChangeFormationButton (Button) - "Change Formation"
    └── UnitListPanel
        ├── UnitListScrollRect (ScrollRect)
        │   └── Content (Empty GameObject)
        └── UnitListItemPrefab (Button with children)
            ├── UnitName (TextMeshPro)
            ├── Health (TextMeshPro)
            └── HealthBar (Slider)
```

## 🔧 **Step-by-Step Setup**

### **1. Create the Main Canvas**
1. Right-click in Hierarchy → UI → Canvas
2. Name it "BattleCanvas"
3. Set Canvas Scaler to "Scale With Screen Size"
4. Reference Resolution: 1920x1080

### **2. Create the Main Panel**
1. Right-click on Canvas → UI → Panel
2. Name it "BattleUIPanel"
3. Set Anchor to "Stretch" and stretch to fill screen
4. Set Color to semi-transparent black (0, 0, 0, 0.5)

### **3. Add Text Elements**
1. Right-click on BattleUIPanel → UI → Text - TextMeshPro
2. Name it "SelectedUnitsText"
3. Position: Top-left corner
4. Text: "Selected Units: 0"
5. Repeat for:
   - "FormationText" - "Formation: Line"
   - "InstructionsText" - "Left Click: Select Units\nRight Click: Move/Attack\nEscape: Pause/Resume"
   - "BattleStatusText" - "BATTLE IN PROGRESS"

### **4. Add Formation Controls**
1. Right-click on BattleUIPanel → UI → Dropdown - TextMeshPro
2. Name it "FormationDropdown"
3. Position: Top-center
4. Add options: "Line", "Square", "Wedge", "Column", "Skirmish"

1. Right-click on BattleUIPanel → UI → Button - TextMeshPro
2. Name it "ChangeFormationButton"
3. Position: Next to dropdown
4. Text: "Change Formation"

### **5. Create Unit List Panel**
1. Right-click on BattleUIPanel → UI → Panel
2. Name it "UnitListPanel"
3. Position: Right side of screen
4. Size: 300x400

### **6. Create Unit List ScrollRect**
1. Right-click on UnitListPanel → UI → ScrollRect
2. Name it "UnitListScrollRect"
3. Stretch to fill UnitListPanel
4. Remove the default Text child

### **7. Create Content Area**
1. Right-click on UnitListScrollRect → UI → Panel
2. Name it "Content"
3. Add Vertical Layout Group component
4. Set Child Controls Size: Height
5. Set Child Force Expand: Height = false

### **8. Create Unit List Item Prefab**
1. Right-click in Project → Create → UI → Button - TextMeshPro
2. Name it "UnitListItemPrefab"
3. Remove the default Text child
4. Add these children:

**UnitName (TextMeshPro)**
- Position: Top-left
- Text: "Unit Name"

**Health (TextMeshPro)**
- Position: Top-right
- Text: "100/100"

**HealthBar (Slider)**
- Position: Bottom
- Size: Full width, height 20
- Remove Handle
- Set Fill to green

### **9. Create Selection Indicator Prefab**
1. Right-click in Project → Create → 3D Object → Cylinder
2. Name it "SelectionIndicator"
3. Scale: (0.1, 0.05, 0.1)
4. Position: (0, 0.1, 0)
5. Color: Yellow
6. Remove Collider
7. Add to Prefabs folder

## 🎮 **BattleUI Script Setup**

### **1. Create BattleUI GameObject**
1. Create Empty GameObject in BattleScene
2. Name it "BattleUI"
3. Add BattleUI script

### **2. Assign References**
In the BattleUI inspector, assign:
- **Selected Units Text**: SelectedUnitsText
- **Formation Text**: FormationText
- **Unit List Scroll Rect**: UnitListScrollRect
- **Unit List Item Prefab**: UnitListItemPrefab
- **Battle Status Text**: BattleStatusText
- **Instructions Text**: InstructionsText
- **Formation Dropdown**: FormationDropdown
- **Change Formation Button**: ChangeFormationButton

## 🎯 **BattleManager Setup**

### **1. Create BattleManager GameObject**
1. Create Empty GameObject in BattleScene
2. Name it "BattleManager"
3. Add BattleManager script

### **2. Assign References**
In the BattleManager inspector, assign:
- **Battle UI Prefab**: BattleUI (or create prefab)
- **Selection Indicator Prefab**: SelectionIndicator

## 🗺️ **BattleMapGenerator Setup**

### **1. Create BattleMapGenerator GameObject**
1. Create Empty GameObject in BattleScene
2. Name it "BattleMapGenerator"
3. Add BattleMapGenerator script

### **2. Create Terrain Prefabs**
1. Create simple plane prefabs for different terrain types
2. Assign to BattleMapGenerator's Terrain Prefabs array

## 🎮 **Controls Summary**

- **Left Click**: Select units
- **Right Click on Enemy**: Attack
- **Right Click on Ground/Friendly**: Move
- **Escape**: Pause/Resume battle

## 🐛 **Testing**

1. Load the BattleScene
2. Press Play
3. The UI should appear with instructions
4. You can test unit selection and movement

## 📝 **Notes**

- The UI is designed to be simple and intuitive
- All combat is controlled through right-clicking
- Formation changes affect selected units
- The system automatically detects enemies vs allies
- Escape key pauses the entire battle

This setup gives you a clean, Total War-style battle interface that's easy to use and understand!
