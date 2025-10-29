# Battle Controls Guide

## Updated BattleTestSimple.cs Features

### **Controls:**
- **Left-click on a unit**: Select it (shows yellow circle underneath)
- **Right-click on another unit**: Move selected unit to that position
- **Units automatically move toward each other** when no specific target is set

### **Visual Indicators:**
- **Red Unit**: Attacker
- **Blue Unit**: Defender  
- **Yellow Circle**: Selection indicator (shows which unit is selected)

### **How to Test:**

1. **Create a new scene**
2. **Add Empty GameObject**
3. **Add BattleTestSimple component**
4. **Hit Play**
5. **Click "Start Test" button**

### **What Should Happen:**
1. **Two colored capsules appear** (red and blue)
2. **They start moving toward each other automatically**
3. **Left-click one** - it gets a yellow selection circle
4. **Right-click the other** - selected unit moves to that position
5. **Console shows debug messages** about movement

### **Debug Messages to Look For:**
- `[BattleTestSimple] Test button clicked!`
- `[BattleTestSimple] Creating simple test...`
- `[SimpleMover-RedUnit] SimpleMover started`
- `[SimpleMover-RedUnit] Found target: BlueUnit`
- `[SimpleMover-RedUnit] Moving to target. Distance: X.XX`
- `[SimpleMover-RedUnit] Selected!` (when you click)
- `[SimpleMover-RedUnit] New move target set: (X, Y, Z)` (when you right-click)

### **Troubleshooting:**

#### **Units don't move at all:**
- Check Console for `[SimpleMover-UnitName] SimpleMover started`
- Check Console for `[SimpleMover-UnitName] Found target: [OtherUnit]`
- Make sure both units are created successfully

#### **Units don't respond to clicks:**
- Make sure the units have colliders (they should automatically)
- Check Console for `[SimpleMover-UnitName] Selected!` when you click
- Try clicking directly on the center of the capsule

#### **Right-click doesn't work:**
- Make sure you have a unit selected first (yellow circle)
- Right-click on the OTHER unit, not the selected one
- Check Console for movement messages

### **Expected Behavior:**
1. **Automatic movement**: Units move toward each other initially
2. **Selection**: Left-click shows yellow selection indicator
3. **Manual movement**: Right-click on other unit moves selected unit there
4. **Debug feedback**: Console shows what's happening

The system now has both automatic AI movement and manual player control! 🎮
