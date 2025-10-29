# Quick Battle System Setup

## 🚀 **5-Minute Setup**

### **1. Create BattleScene**
1. File → New Scene
2. Save as "BattleScene"
3. Add to Build Settings (File → Build Settings → Add Open Scenes)

### **2. Add BattleManager**
1. Create Empty GameObject → Name "BattleManager"
2. Add BattleManager script
3. Set Battle Map Size: 100
4. Set Unit Spacing: 2
5. Set Formation Spacing: 20

### **3. Add BattleMapGenerator**
1. Create Empty GameObject → Name "BattleMapGenerator"
2. Add BattleMapGenerator script
3. Set Tile Size: 2
4. Set Obstacle Density: 0.1

### **4. Create Simple UI**
1. Right-click Hierarchy → UI → Canvas
2. Name "BattleCanvas"
3. Right-click Canvas → UI → Panel
4. Name "BattleUIPanel"
5. Add these TextMeshPro elements to BattleUIPanel:
   - "SelectedUnitsText" → "Selected Units: 0"
   - "InstructionsText" → "Left Click: Select Units\nRight Click: Move/Attack\nEscape: Pause/Resume"
   - "BattleStatusText" → "BATTLE IN PROGRESS"

### **5. Create BattleUI GameObject**
1. Create Empty GameObject → Name "BattleUI"
2. Add BattleUI script
3. Assign the text elements to the script

### **6. Create Selection Indicator**
1. Create 3D Object → Cylinder
2. Scale: (0.1, 0.05, 0.1)
3. Color: Yellow
4. Remove Collider
5. Drag to Project to make prefab
6. Assign to BattleManager

## 🎮 **Controls**
- **Left Click**: Select units
- **Right Click on Enemy**: Attack
- **Right Click on Ground**: Move
- **Escape**: Pause/Resume

## 🧪 **Test the System**
1. In your main game scene, have any unit attack:
   - Another civilization's unit
   - An animal (wolf, bear, etc.)
2. The battle should automatically trigger (even with just 1 vs 1 units)
3. You'll be taken to the BattleScene with units in formation
4. Use the controls to test unit selection and movement

## 🎯 **That's It!**
The battle system is now ready to use. When units attack each other in the main game, it will automatically load the battle scene where you can control them in real-time.

## 🔧 **Troubleshooting**
- **Battle doesn't start**: Check if BattleManager.Instance exists in main scene
- **UI not working**: Make sure all text elements are assigned in BattleUI script
- **Units not spawning**: Check BattleMapGenerator configuration
- **Scene doesn't load**: Make sure "BattleScene" is in Build Settings
