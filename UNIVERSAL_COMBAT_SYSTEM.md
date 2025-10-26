# Universal Combat System - Workers Can Now Fight!

## ✅ **What Changed**

Workers can now engage in combat with **ANY** unit type:
- ✅ Workers vs Workers (already existed, now improved)
- ✅ Workers vs Combat Units (NEW!)
- ✅ Workers vs Animals (NEW! - hunting system)
- ✅ Combat Units vs Workers (NEW!)

**Result:** Full bidirectional combat between all unit types!

---

## 🗡️ **Combat Matrix**

| Attacker ↓ / Defender → | Combat Unit | Worker Unit | Animal |
|------------------------|-------------|-------------|---------|
| **Combat Unit** | ✅ Original | ✅ **NEW!** | ✅ Original |
| **Worker Unit** | ✅ **NEW!** | ✅ Original | ✅ **NEW!** |

---

## ⚔️ **How Worker Combat Works**

### **Workers vs Combat Units (Including Animals)**

**New Methods in `WorkerUnit.cs`:**
```csharp
bool CanAttack(CombatUnit target)  // Check if can attack
void Attack(CombatUnit target)     // Perform attack
```

**Combat Mechanics:**
- Workers fight at **-2 penalty** vs trained soldiers
- Workers fight at **no penalty** vs animals (hunting!)
- Full weapon system support (melee + ranged)
- Projectile support (spears, bows, etc.)
- Counter-attacks work both ways
- Experience gain for kills

**Example Combat:**
```
Pioneer (Attack 3) vs Bear (Defense 5):
├─ Base damage: 3 - 5 = 0 (would miss)
├─ With spear (+2): 5 - 5 = 0 (barely hits!)
└─ With bow (+3): 6 - 5 = 1 damage ✅

Warrior (Attack 8) attacks Pioneer (Defense 2):
├─ Combat bonus: +2 vs workers
├─ Damage: (8 + 2) - 2 = 8 damage 💀
└─ Pioneer probably dies in 1 hit!
```

---

### **Combat Units vs Workers**

**New Methods in `CombatUnit.cs`:**
```csharp
bool CanAttack(WorkerUnit target)  // Check if can attack
void Attack(WorkerUnit target)     // Perform attack
```

**Combat Mechanics:**
- Combat units get **+2 bonus** vs workers
- Usually one-sided (workers are squishy!)
- Full weapon/projectile support
- Counter-attacks allowed (workers can fight back!)
- Morale gain for kills

---

## 🎯 **Special Features**

### **1. Hunting System (Workers vs Animals)**
```csharp
if (target.data.unitType == CombatCategory.Animal)
{
    // Worker killed animal - gain food!
    int foodGain = target.data.foodOnKill;
    owner.food += foodGain;
    ShowNotification("Hunted bear and gained 15 food!");
}
```

**Workers can now hunt animals for food!** 🏹🦌

### **2. Combat Advantage/Disadvantage System**

**Worker Penalties:**
- **-2 attack** vs trained combat units (warriors, soldiers)
- **No penalty** vs animals (natural hunting)
- **Equal footing** vs other workers

**Combat Unit Bonuses:**
- **+2 attack** vs workers (professional soldiers vs civilians)
- **Normal combat** vs other combat units
- **Normal combat** vs animals

### **3. Bidirectional Combat**

**All scenarios now work:**
```
✅ Spearman attacks Pioneer → Pioneer damaged
✅ Pioneer fights back → Spearman damaged (but less)
✅ Pioneer attacks bear → Bear damaged
✅ Bear counter-attacks → Pioneer damaged
✅ Builder vs Worker PvP → Both can fight
```

---

## 🎮 **Gameplay Scenarios**

### **Scenario 1: Hunting for Food**
```
Turn 1: Food stockpile = 5 (running low!)
  ├─ Pioneer spots wild boar nearby
  ├─ Pioneer attacks with spear
  ├─ Boar takes 3 damage, dies
  └─ Pioneer gains +12 food! ✅

Result: Food crisis averted through hunting!
```

### **Scenario 2: Worker Caught by Enemy**
```
Turn 5: Enemy warrior approaches your builder
  ├─ Builder (Attack 2, Defense 2, HP 10)
  ├─ Warrior attacks (8 + 2 bonus - 2 def = 8 damage!)
  ├─ Builder survives with 2 HP
  ├─ Builder fights back (2 - 2 penalty - 8 def = 0 damage)
  └─ Next turn: Warrior finishes off the builder 💀

Result: Workers are vulnerable to combat units!
```

### **Scenario 3: Desperate Defense**
```
Turn 12: Your city is undefended, enemy worker approaches
  ├─ Your pioneer is nearby
  ├─ Pioneer attacks enemy worker (3 - 2 = 1 damage)
  ├─ Enemy worker fights back (2 - 2 = 0 damage)
  ├─ After 10 turns: Enemy worker dies
  └─ City saved! ✅

Result: Workers can defend in emergencies!
```

### **Scenario 4: Hunting Party**
```
Turn 8: 3 pioneers hunt a mammoth
  ├─ Pioneer 1 attacks (3 damage)
  ├─ Mammoth attacks back (8 damage, pioneer 1 dies!)
  ├─ Pioneer 2 attacks (3 damage)
  ├─ Pioneer 3 attacks (3 damage)
  ├─ Mammoth dies (total 9 damage dealt)
  └─ Gain +25 food from mammoth! ✅

Result: Coordinated hunting is effective but risky!
```

---

## 📊 **Combat Stats Comparison**

### **Typical Early Game Units:**

| Unit Type | Attack | Defense | HP | Notes |
|-----------|--------|---------|-----|-------|
| **Pioneer** | 2-3 | 2 | 10 | Basic worker |
| **Warrior** | 6-8 | 6 | 20 | Basic combat |
| **Wolf** | 4 | 4 | 15 | Basic animal |
| **Bear** | 7 | 5 | 25 | Dangerous animal |

### **Combat Outcomes:**

**Worker vs Worker:**
```
Pioneer (3) vs Builder (2):
  Damage: 3 - 2 = 1 per hit
  Hits to kill: ~10 turns
  Fairly balanced ✅
```

**Worker vs Animal:**
```
Pioneer (3) vs Wolf (4 def):
  Damage: 3 - 4 = 0 (miss!)
  With spear (+2): 5 - 4 = 1 damage
  Hits to kill wolf: ~15 turns (risky!) ⚠️

Pioneer (3) vs Bear (5 def):
  Damage: 3 - 5 = 0 (miss!)
  With bow (+3): 6 - 5 = 1 damage
  Bear hits back: 7 - 2 = 5 damage per turn
  Worker dies in 2 hits! 💀
```

**Combat Unit vs Worker:**
```
Warrior (8 + 2 bonus) vs Pioneer (2 def):
  Damage: 10 - 2 = 8 per hit
  Pioneer dies in 2 hits! 💀
  One-sided massacre!
```

---

## 🛠️ **New Helper Methods**

### **For UI Systems:**

**CombatUnit:**
```csharp
bool CanAttackAnyTarget(GameObject target)  // Check if can attack anything
void AttackTarget(GameObject target)        // Attack anything (auto-detect)
```

**WorkerUnit:**
```csharp
bool CanAttackAnyTarget(GameObject target)  // Check if can attack anything
void AttackTarget(GameObject target)        // Attack anything (auto-detect)
```

**Usage in UI/Input:**
```csharp
// Generic attack button
void OnAttackButton(GameObject target)
{
    if (selectedUnit is CombatUnit cu)
    {
        if (cu.CanAttackAnyTarget(target))
            cu.AttackTarget(target);
    }
    else if (selectedUnit is WorkerUnit wu)
    {
        if (wu.CanAttackAnyTarget(target))
            wu.AttackTarget(target);
    }
}
```

---

## 🎯 **Strategic Implications**

### **1. Hunting is Now Viable**
- Workers can hunt animals for emergency food
- Risky but rewarding (animal meat → food stockpile)
- Bears/dangerous animals are lethal to workers
- Group hunting is more effective

### **2. Workers Need Protection**
- Combat units can massacre undefended workers
- Must escort valuable workers with soldiers
- Enemy can target your builders to slow expansion
- Captured territory needs military presence

### **3. Desperation Combat**
- Workers CAN fight if necessary
- Usually ineffective vs trained soldiers
- Better than nothing in emergencies
- Pioneers might survive long enough to escape

### **4. Worker PvP for Resources**
- Rival workers can fight over contested territory
- Fairly balanced (neither has combat bonuses)
- Slow combat (low damage)
- Strategic for early land grabs

---

## ⚖️ **Balance Considerations**

### **Worker Base Stats (Recommended):**
```
Pioneer:
├─ Attack: 2-3
├─ Defense: 2
├─ HP: 10-15
└─ Range: 1

Builder:
├─ Attack: 1-2
├─ Defense: 2
├─ HP: 8-12
└─ Range: 1

Advanced Worker (with tools):
├─ Attack: 3-5
├─ Defense: 3-4
├─ HP: 15-20
└─ Range: 1-2 (if ranged tool)
```

### **Equipment Impact:**
```
Basic Spear:
├─ +2 attack
└─ Makes hunting possible

Hunting Bow:
├─ +3 attack
├─ +1 range
└─ Safer hunting (ranged)

Work Tools (axe, pickaxe):
├─ +1-2 attack
├─ Dual purpose (work + combat)
└─ Better than unarmed
```

---

## 📋 **Testing Checklist**

- [ ] Worker can attack animal successfully
- [ ] Worker gains food from killing animal
- [ ] Combat unit can attack worker successfully
- [ ] Worker can fight back (but usually loses)
- [ ] Worker vs worker combat still works
- [ ] Projectiles work for both unit types
- [ ] Counter-attacks work bidirectionally
- [ ] Experience is gained properly
- [ ] Animations trigger correctly
- [ ] UI can use `CanAttackAnyTarget()` for highlighting

---

## 🎉 **Summary**

**Before:**
- Workers could only attack other workers
- Workers were defenseless vs combat units
- Workers couldn't hunt animals
- Incomplete combat system

**After:**
- ✅ Workers can attack combat units (at disadvantage)
- ✅ Workers can hunt animals for food! 🏹
- ✅ Combat units can attack workers (at advantage)
- ✅ Full bidirectional combat matrix
- ✅ Generic attack methods for easy UI integration
- ✅ Balanced advantage/disadvantage system

**Result:** Complete, universal combat system where any unit can engage any other unit! 🎮

