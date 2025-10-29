# Battle Test Setup Guide

## ✅ **New Features Added:**

### **1. UI Management:**
- **Canvas on Same GameObject**: Canvas is now attached to the BattleTestSimple GameObject
- **UI Panel**: Button and status text are in a panel that disappears when battle starts
- **Cleaner Structure**: Everything is organized under one GameObject

### **2. Camera Controls:**
- **WASD Movement**: Move camera around with W, A, S, D keys
- **Mouse Wheel Zoom**: Scroll to zoom in/out
- **Zoom Limits**: Min zoom: 2 units, Max zoom: 20 units
- **Smooth Movement**: Camera moves smoothly with configurable speed

## **Setup Instructions:**

### **Step 1: Create Scene**
1. Create new scene
2. Add **Empty GameObject**
3. Name it "BattleTestManager"

### **Step 2: Add Component**
1. Select the "BattleTestManager" GameObject
2. Add **BattleTestSimple** component
3. The component will automatically create the Canvas and UI

### **Step 3: Configure Camera**
1. Make sure you have a **Main Camera** in the scene
2. Position it above the battle area (e.g., Y=10, Z=-10)
3. Set it to **Orthographic** or **Perspective** (both work)

### **Step 4: Test**
1. Hit **Play**
2. Click **"Start Test"** button
3. UI disappears, 6 units appear with health labels
4. Use **WASD** to move camera
5. Use **Mouse Wheel** to zoom
6. **Left-click** units to select
7. **Right-click** ground to move selected units

## **Controls Summary:**

### **Camera:**
- **W, A, S, D**: Move camera around
- **Mouse Wheel**: Zoom in/out
- **Zoom Range**: 2-20 units from center

### **Units:**
- **Left-click**: Select unit (yellow circle appears)
- **Right-click ground**: Move selected unit to that position
- **Auto-combat**: Units fight automatically when close

### **Battle:**
- **Attackers**: 10 HP, 2 damage (red units)
- **Defenders**: 8 HP, 1 damage (blue units)
- **Health Labels**: Show above each unit
- **Auto-fighting**: When within 1.5 units of each other

## **What Happens:**
1. **Start**: UI disappears, 6 units spawn in formation
2. **Movement**: Units move toward each other automatically
3. **Combat**: Units fight when close, dealing damage every second
4. **Health**: Labels update in real-time as units take damage
5. **Death**: Units disappear when health reaches 0
6. **Camera**: You can move around and zoom to watch the battle

The battle system now has full camera controls and proper UI management! 🎮📷
