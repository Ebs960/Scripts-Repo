# Quick Battle Debug Guide

## Step 1: Create a Test Scene

1. **File → New Scene**
2. **Save as**: `QuickBattleTest.unity`

## Step 2: Add the Debug Script

1. **Create Empty GameObject**: `BattleTest`
2. **Add Component**: `QuickBattleStartDebug`
3. **Check the settings**:
   - ✅ Enable Debug Logs: true
   - ✅ Create UI: true

## Step 3: Test the System

1. **Hit Play**
2. **Check the Console** for debug messages
3. **Look for these messages**:
   - `[QuickBattleStartDebug] QuickBattleStartDebug started`
   - `[QuickBattleStartDebug] Button connected successfully`
   - `[QuickBattleStartDebug] Ready! Click the button to start a battle.`

## Step 4: Click the Button

1. **Click the blue "Start Battle" button**
2. **Check the Console** for these messages:
   - `[QuickBattleStartDebug] StartQuickBattle called!`
   - `[QuickBattleStartDebug] Looking for GameManager...`
   - Either: `[QuickBattleStartDebug] GameManager found!` OR `[QuickBattleStartDebug] No GameManager found, trying fallback method...`

## Step 5: Check What Happens

### If GameManager is Found:
- You should see: `Battle started via GameManager!`
- The GameManager will handle the battle setup

### If No GameManager:
- You should see: `Creating simple battle...`
- Two colored capsules should appear (red and blue)
- They should start moving toward each other

## Troubleshooting

### Problem: Nothing happens when I click the button
**Check:**
1. **Console for errors** - Look for red error messages
2. **Button is connected** - Look for "Button connected successfully"
3. **UI is created** - Look for "Start button created" and "Status text created"

### Problem: No UI appears
**Check:**
1. **Canvas exists** - Look for "Canvas created" or "Found existing canvas"
2. **Script is running** - Look for "QuickBattleStartDebug started"
3. **Create UI is enabled** - Check the inspector

### Problem: Units don't move
**Check:**
1. **Units are created** - Look for "Unit Attacker created successfully"
2. **AI is working** - Look for `[SimpleBattleAI-UnitName]` messages
3. **Target is found** - Look for "Found target by tag/name"

### Problem: GameManager not found
**This is normal if you don't have GameManager in the scene!**
- The script will fall back to creating a simple battle
- You should see two colored capsules appear

## Expected Behavior

### With GameManager:
1. Button click → GameManager.StartBattleTest() → Full battle system

### Without GameManager:
1. Button click → Create ground plane → Create two units → Units move toward each other

## Debug Messages to Look For

### Success Messages:
- `QuickBattleStartDebug started`
- `Button connected successfully`
- `StartQuickBattle called!`
- `Creating simple battle...`
- `Unit [Name] created successfully`
- `Found target by tag/name`

### Error Messages:
- `ERROR: No start button found!`
- `WARNING: No renderer found on [Name]`
- `EXCEPTION: [Error details]`

## Next Steps

Once you see the basic system working:
1. **Add GameManager** to the scene for full battle functionality
2. **Customize the units** with your own models
3. **Add more complex AI** behaviors
4. **Integrate with your main game**

## Quick Fixes

### If the button doesn't work:
1. **Check the Console** for error messages
2. **Make sure the script is enabled** in the inspector
3. **Try restarting Unity** if there are caching issues

### If no units appear:
1. **Check the Console** for "Unit created successfully" messages
2. **Look for error messages** in the unit creation process
3. **Make sure the ground plane is created** first

The debug version will tell you exactly what's happening at each step!
