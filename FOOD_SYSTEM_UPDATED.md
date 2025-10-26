# Food System - Granular Consumption Update

## ✅ **Changes Made**

The food consumption system has been updated to be **per-unit granular** and include **city population consumption**.

---

## 🍖 **How Food Consumption Works Now**

### **1. Each Unit Type Has Its Own Consumption**

**Combat Units** (`CombatUnitData.cs`):
- New field: `foodConsumptionPerTurn` (default: 2)
- Configure per unit type in Unity Inspector
- Examples:
  - Light infantry: 1 food/turn
  - Heavy infantry: 2 food/turn
  - Cavalry: 3 food/turn
  - Siege weapons: 1 food/turn
  - Spaceships: 5 food/turn

**Worker Units** (`WorkerUnitData.cs`):
- New field: `foodConsumptionPerTurn` (default: 1)
- Configure per worker type in Unity Inspector
- Examples:
  - Pioneer: 1 food/turn
  - Builder: 1 food/turn
  - Advanced engineer: 2 food/turn

---

### **2. Cities Consume Food Based on Population**

**City Population** (`City.cs`):
- New field: `foodConsumptionPerPopulation` (default: 1)
- **Consumption = City Level × foodConsumptionPerPopulation**

**Examples:**
```
City Level 1:  1 × 1 = 1 food/turn
City Level 5:  5 × 1 = 5 food/turn
City Level 10: 10 × 1 = 10 food/turn
City Level 20: 20 × 1 = 20 food/turn
City Level 40: 40 × 1 = 40 food/turn (max)
```

**This creates interesting decisions:**
- Large cities need LOTS of food!
- Expanding too fast can cause famine
- Must balance city growth with food production

---

### **3. Total Consumption Calculation**

**Each Turn:**
```csharp
Total Consumption = 
    Σ(each combat unit's foodConsumptionPerTurn) +
    Σ(each worker unit's foodConsumptionPerTurn) +
    Σ(each city's level × foodConsumptionPerPopulation)
```

**Example Game State:**
```
Units:
├── 3 × Spearmen (2 food each)      = 6 food
├── 2 × Archers (1 food each)       = 2 food
├── 1 × Cavalry (3 food)            = 3 food
└── 2 × Pioneers (1 food each)      = 2 food
    Unit Consumption:                 13 food/turn

Cities:
├── Capital (level 8)                = 8 food
├── Second City (level 4)            = 4 food
└── Third City (level 2)             = 2 food
    City Consumption:                 14 food/turn

TOTAL CONSUMPTION:                    27 food/turn
```

---

## 📊 **Food Economy Examples**

### **Early Game (1 city, 2 units)**
```
Production:
└── City level 2 producing 15 food/turn

Consumption:
├── 1 Pioneer: 1 food
├── 1 Spearman: 2 food
└── City pop (level 2): 2 food
    Total: 5 food/turn

Net Food: +10 food/turn ✅ Surplus!
```

### **Mid Game (3 cities, 12 units)**
```
Production:
├── Capital (level 10): 25 food
├── Second city (level 6): 18 food
└── Third city (level 4): 12 food
    Total: 55 food/turn

Consumption:
├── 8 combat units (avg 2): 16 food
├── 4 workers (avg 1): 4 food
├── City pops (20 total): 20 food
    Total: 40 food/turn

Net Food: +15 food/turn ✅ Good balance!
```

### **Late Game Large Army (5 cities, 40 units)**
```
Production:
└── 5 cities (avg level 12): 80 food/turn

Consumption:
├── 35 combat units (avg 2.5): 87 food
├── 5 workers (avg 1.5): 8 food
├── City pops (60 total): 60 food
    Total: 155 food/turn

Net Food: -75 food/turn ⚠️ DEFICIT!
Stockpile: 200 food
Time until famine: ~2.6 turns
```

### **Crisis Scenario (Cities Under Siege)**
```
Production:
└── Cities damaged, only 30 food/turn

Consumption:
└── Still need 155 food/turn

Net Food: -125 food/turn 💀
Stockpile: 50 food
Time until famine: <1 turn!

ACTION NEEDED: 
- Disband some units
- Build more farms
- Make peace!
```

---

## 🎮 **Strategic Implications**

### **Unit Recruitment:**
- Can't just spam units anymore - each one increases food drain
- Heavy units (cavalry, tanks) cost more food
- Must balance army size with food production

### **City Growth:**
- Growing cities consume more food
- Level 40 city = 40 food/turn just for population!
- Must invest in farms and food buildings

### **War Economy:**
- Large armies are expensive to maintain
- Long wars can lead to famine if cities are damaged
- Peace becomes more attractive when food runs low

### **Empire Expansion:**
- Founding new cities increases consumption
- New cities start at level 1 but still need food
- Too many small cities = food crisis

---

## 🛠️ **Configuration Guide**

### **Unit Food Consumption (in Unity Inspector)**

**Light Units (1 food/turn):**
- Scouts, archers, militia
- Workers, pioneers
- Small boats

**Standard Units (2 food/turn):**
- Infantry, swordsmen, spearmen
- Basic ships
- Early artillery

**Heavy Units (3-4 food/turn):**
- Cavalry, heavy cavalry
- Tanks, mechs
- Battleships

**Elite/Advanced Units (5+ food/turn):**
- Spaceships
- Giant robots
- Super-heavy units

### **City Food Consumption**

**Adjust in City prefab:**
- `foodConsumptionPerPopulation` (default: 1)
- Higher values = cities need more food per pop
- Lower values = cities are more efficient

**Examples:**
- Nomadic era: 0.5 (efficient small groups)
- Agricultural era: 1.0 (normal)
- Industrial era: 1.5 (urban populations)
- Space age: 2.0 (high-tech cities)

---

## 📈 **UI Display Recommendations**

### **Show in PlayerUI:**
```
Food: 127 (+15/turn)
  Production: +55
  Consumption: -40
    ├─ Units: -16
    └─ Cities: -24
  Net: +15
```

### **Tooltip Breakdown:**
Use `GetFoodConsumptionBreakdown()` method:
```csharp
var (units, cities, total) = playerCiv.GetFoodConsumptionBreakdown();

tooltip = $"Food Consumption: {total}/turn\n" +
          $"  Units: {units}\n" +
          $"  Cities: {cities}";
```

### **Warning Indicators:**
```csharp
int netFood = playerCiv.GetNetFoodPerTurn();
if (netFood < 0)
    ShowRedWarning("FOOD DEFICIT!");
else if (netFood < 5)
    ShowYellowWarning("Low Food Surplus");
```

---

## 🔧 **Balance Tuning**

### **If Food is Too Scarce:**
1. Reduce unit consumption (1-2 instead of 2-3)
2. Reduce city consumption (0.5 per pop instead of 1)
3. Increase farm yields in buildings
4. Give early techs food bonuses

### **If Food is Too Abundant:**
1. Increase unit consumption (3-4 for heavy units)
2. Increase city consumption (1.5-2 per pop)
3. Add food-draining events (droughts, plagues)
4. Make farms more expensive to build

### **Good Starting Values (Recommended):**
```
Unit Consumption:
├── Light infantry: 1 food/turn
├── Standard infantry: 2 food/turn
├── Heavy units: 3 food/turn
├── Workers: 1 food/turn
└── Advanced workers: 2 food/turn

City Consumption:
└── 1 food per population level

With these settings:
- Early game (1 city lvl 2, 2 units): 2 + 3 = 5 food needed
- Mid game (3 cities lvl 6-10, 12 units): ~20 + 24 = 44 food needed
- Late game (5 cities lvl 15+, 30 units): ~75 + 60 = 135 food needed
```

---

## 🎯 **Testing Checklist**

- [ ] Each unit type consumes its configured amount
- [ ] Cities consume food based on population (level × 1)
- [ ] Large cities (level 20+) consume significant food
- [ ] Building a big army causes food deficit
- [ ] Stockpile drains when consumption > production
- [ ] Famine triggers when food reaches 0
- [ ] Units take damage during famine
- [ ] Disbanding units reduces consumption
- [ ] Growing cities increases consumption

---

## 📝 **Summary**

**Before:**
- All combat units consumed 2 food (flat rate)
- All workers consumed 1 food (flat rate)
- Cities consumed NOTHING

**After:**
- Each unit type has configurable consumption
- Cities consume food based on population size
- Strategic decisions around army size and city growth
- Proper food economy simulation!

**Result:** Much more realistic and strategic food management system! 🌾

