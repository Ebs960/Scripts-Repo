# Battle Movement & Combat System Explanation

## How Movement Works

### Formation Movement Flow

1. **Movement Command** (`MoveToPosition()` - line 3194)
   - Sets `targetPosition` and `isMoving = true`
   - Plays walking animations
   - Formation starts moving toward target

2. **Movement Update** (`MoveFormation()` - line 3202)
   - Called every frame while `isMoving == true`
   - Calculates direction: `(targetPosition - formationCenter).normalized`
   - Calculates distance to target
   - If distance > 0.5f, continues moving
   - Rotates formation to face movement direction
   - Moves formation center: `formationCenter += direction * speed * Time.deltaTime`
   - Clamps to battlefield bounds
   - Grounds the formation center
   - **Calls `UpdateSoldierPositions()`** - THIS IS WHERE TELEPORTING HAPPENS

3. **Soldier Position Update** (`UpdateSoldierPositions()` - line 3288)
   - **CRITICAL**: This directly sets soldier positions every frame
   - Calculates formation offset for each soldier (square grid pattern)
   - Sets: `soldiers[i].transform.position = Ground(desired)`
   - This is INSTANT position setting, not smooth movement
   - If `formationCenter` moves, soldiers instantly snap to new positions

### The Teleporting Problem

**Root Cause**: `UpdateSoldierPositions()` directly sets `transform.position` every frame based on `formationCenter + offset`. 

**When it happens**:
- When `formationCenter` changes (formation moves)
- When formation rotation changes (soldiers snap to new grid positions)
- When combat starts and formations try to maintain formation while fighting
- When `soldierOffsetOverrides` are applied (knockback, compression, etc.)

**Why it looks like teleporting**:
- Soldiers don't smoothly move to their formation positions
- They instantly snap to `formationCenter + GetFormationOffset(i)`
- If `formationCenter` jumps (due to grounding, bounds clamping, or combat adjustments), soldiers teleport

## How Combat Works

### Combat Detection Flow

1. **Enemy Detection** (`CheckForEnemies()` - line 3362)
   - Called every frame during movement (line 3246)
   - Checks cached enemy formations
   - Calculates distance: `(formationCenter - enemyFormation.formationCenter).sqrMagnitude`
   - If distance < `(formationRadius * 2)²`, combat starts
   - Calls `StartCombatWithFormation(enemyFormation)`

2. **Combat Start** (`StartCombatWithFormation()` - line 3417)
   - Sets `currentEnemyTarget = enemyFormation`
   - Updates charge state (if moving toward enemy)
   - Plays fighting animations for both formations
   - **Starts `CombatDamageCoroutine()`** - this is where damage happens

3. **Combat Damage Loop** (`CombatDamageCoroutine()` - line 3546)
   - Runs every 0.6 seconds (tick interval)
   - Checks if formations are still in range: `distance < (formationRadius * 2)²`
   - Finds soldiers in melee contact (within 1.5 units)
   - Pairs soldiers for combat
   - Calls `StaggeredAttack()` for each pair
   - **Also calls `UpdateSoldierPositions()`** for both formations (line 3666-3667)

### Why Combat Might Not Work

**Problem 1: Combat Range Check**
- Combat only happens if formations are within `(formationRadius * 2)²` distance
- If formations are too far apart, combat coroutine exits immediately
- Check: Are formations actually getting close enough? (formationRadius might be too small)

**Problem 2: Melee Contact Detection**
- Combat damage only applies to soldiers within 1.5 units of each other
- If soldiers aren't close enough, no damage happens
- Soldiers might be in formation positions that don't overlap with enemy formation

**Problem 3: Formation Movement During Combat**
- `UpdateSoldierPositions()` is called during combat (line 3666-3667)
- This keeps soldiers in formation grid, which might pull them away from enemies
- Soldiers teleport to formation positions, breaking melee contact

**Problem 4: Movement Not Stopping**
- When combat starts, `StopMoving()` is called (line 3248)
- But `MoveFormation()` might still be running if `isMoving` wasn't properly cleared
- Formation continues moving, pulling soldiers away from combat

## The Teleporting Issue - Detailed Analysis

### Where Teleporting Happens

1. **`UpdateSoldierPositions()` (line 3301)**
   ```csharp
   soldiers[i].transform.position = Ground(desired);
   ```
   - Direct position assignment - no interpolation
   - Called every frame during movement
   - Called during combat (line 3666-3667)

2. **Formation Compression (line 4206)**
   ```csharp
   soldier.transform.position = Vector3.Lerp(soldier.transform.position, formationCenter, compressionFactor * 0.1f);
   ```
   - Uses Lerp but still directly sets position
   - Can cause sideways movement if formation center shifts

3. **Knockback (line 4225)**
   ```csharp
   unit.transform.position = Ground(knockbackPosition);
   ```
   - Direct position setting for knockback
   - Can cause teleporting if knockback distance is large

4. **Ground() Function**
   - If `Ground()` raycast finds different Y positions, soldiers snap up/down
   - If terrain is uneven, soldiers teleport to ground level

### Why "Sideways Off Map" Happens

**Scenario 1: Formation Center Calculation**
- `formationCenter` is calculated from soldier positions
- If soldiers are teleporting, `formationCenter` might jump
- This causes more teleporting in a feedback loop

**Scenario 2: Bounds Clamping**
- `ClampFormationToBattlefieldBounds()` (line 3232) might clamp formation center
- If center is clamped, soldiers teleport to new positions
- If bounds are wrong, formations can be pushed off map

**Scenario 3: Combat Position Updates**
- During combat, both formations call `UpdateSoldierPositions()`
- If enemy formation moves, your soldiers teleport to maintain formation
- This can pull soldiers away from combat area

**Scenario 4: Formation Rotation**
- When formation rotates (line 3216), soldier offsets rotate too
- Soldiers instantly snap to new rotated positions
- If rotation is sudden, looks like teleporting

## Potential Issues to Check

### 1. Formation Radius Too Small
- If `formationRadius` is too small, formations might not detect enemies
- Check: `formationRadius` value in FormationUnit
- Combat range = `(formationRadius * 2)²`

### 2. Soldier Spacing Too Large
- If `soldierSpacing` is too large, soldiers are spread out
- Melee range is only 1.5 units
- Soldiers might not be close enough to enemies to fight

### 3. Movement Not Stopping
- Check if `isMoving` is properly set to false when combat starts
- `StopMoving()` should set `isMoving = false` and `targetPosition = formationCenter`

### 4. Formation Center Calculation
- `formationCenter` should be average of soldier positions
- If soldiers are teleporting, center calculation might be wrong
- Check how `formationCenter` is initialized and updated

### 5. Ground() Function Issues
- If `Ground()` raycast fails or returns wrong position, soldiers teleport
- Check if battlefield has proper colliders
- Check if raycast layers are correct

### 6. Combat Coroutine Not Starting
- `StartCombatWithFormation()` checks `if (activeCombatCoroutine != null) return;`
- If coroutine doesn't properly clean up, new combat can't start
- Check if coroutine is properly stopped when combat ends

## Debugging Steps

1. **Check Formation Distances**
   - Log `formationCenter` positions when combat should start
   - Log distance between formations
   - Verify formations are within combat range

2. **Check Soldier Positions**
   - Log soldier positions when combat starts
   - Check if soldiers are within 1.5 units of enemies
   - Verify melee contact detection is working

3. **Check Movement State**
   - Log `isMoving` when combat starts
   - Verify `StopMoving()` is being called
   - Check if `targetPosition` is being cleared

4. **Check Formation Center**
   - Log `formationCenter` every frame during movement
   - Check if center is jumping unexpectedly
   - Verify bounds clamping isn't pushing formations off map

5. **Check Combat Coroutine**
   - Log when `CombatDamageCoroutine()` starts
   - Log when it exits (and why)
   - Check if coroutine is running but not finding soldiers in contact

## Key Code Locations

- **Movement**: `MoveFormation()` - line 3202
- **Position Updates**: `UpdateSoldierPositions()` - line 3288
- **Combat Detection**: `CheckForEnemies()` - line 3362
- **Combat Start**: `StartCombatWithFormation()` - line 3417
- **Combat Damage**: `CombatDamageCoroutine()` - line 3546
- **Melee Contact**: Lines 3566-3604
- **Soldier Pairing**: Lines 3606-3636

## Summary

**Movement**: Formations move by updating `formationCenter`, then instantly snapping all soldiers to `formationCenter + offset` positions. This causes teleporting.

**Combat**: Combat starts when formations are within range, but damage only applies to soldiers within 1.5 units. If `UpdateSoldierPositions()` keeps soldiers in formation grid, they might not be close enough to enemies to fight.

**Teleporting**: Direct `transform.position` assignment in `UpdateSoldierPositions()` causes instant position changes. Combined with formation movement, rotation, and combat position updates, this creates the teleporting effect.

**Sideways Off Map**: Likely caused by formation center calculation issues, bounds clamping, or formation rotation pulling soldiers to unexpected positions.

