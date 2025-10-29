# 🤖 **Battle AI System Explained**

## 🎯 **Overview**

The battle system now features **intelligent AI** that makes tactical decisions in real-time. Units will automatically:
- **Target selection** based on threat assessment
- **Formation maintenance** and coordination
- **Tactical positioning** (flanking, high ground)
- **Retreat when necessary** (health/morale thresholds)
- **Adapt to battlefield situation** (overwhelming force, balanced fight)

---

## 🧠 **AI Components**

### **1. BattleAI.cs - Individual Unit AI**
Each unit gets a `BattleAI` component that handles:

#### **Decision Making (Every 1 second)**
```csharp
- Target Selection: Choose best enemy to attack
- Tactical Assessment: Should I advance, flank, or retreat?
- Formation Maintenance: Stay with allies or regroup
```

#### **Target Scoring System**
```csharp
Score = Distance + Health + Threat + Flanking + High Ground + Formation
- Closer enemies = higher score
- Weaker enemies = higher score  
- Flanking position = +1.5x bonus
- High ground = +1.3x bonus
- Formation position = +1.2x bonus
```

#### **AI States**
- **Idle**: Not doing anything
- **Advancing**: Moving towards enemies
- **Engaging**: In combat with target
- **Flanking**: Trying to get behind enemy
- **Retreating**: Running away
- **Regrouping**: Moving back to formation
- **Defending**: Holding position

### **2. BattleAIManager.cs - Coordinated AI**
Manages all AI units and makes strategic decisions:

#### **Tactical Situation Assessment (Every 5 seconds)**
```csharp
- Balanced: Equal forces
- AttackerAdvantage: Attackers have edge
- DefenderAdvantage: Defenders have edge  
- AttackerOverwhelming: Attackers greatly outnumber
- DefenderOverwhelming: Defenders greatly outnumber
```

#### **Coordination (Every 2 seconds)**
```csharp
- Formation Centers: Calculate where units should be
- Unit Behavior: Set battle states based on situation
- Strategic Commands: Advance, defend, or retreat
```

---

## 🎮 **How It Works**

### **Battle Flow**
1. **Units spawn** in formations
2. **AI components added** to all units
3. **AI Manager starts** coordinating
4. **Units make decisions** every second
5. **Tactical situation** assessed every 5 seconds
6. **Formation coordination** every 2 seconds

### **AI Decision Process**
```
1. Update nearby units (allies/enemies)
2. Check retreat conditions (health/morale)
3. If retreating → Execute retreat
4. If regrouping → Move to formation
5. If have target → Engage or advance
6. If no target → Look for enemies
7. Execute chosen action
```

### **Target Selection Algorithm**
```
For each enemy:
  Score = 0
  Score += Distance factor (closer = better)
  Score += Health factor (weaker = better)
  Score += Threat factor (less dangerous = better)
  Score += Flanking bonus (if behind enemy)
  Score += High ground bonus (if above enemy)
  Score += Formation bonus (if near formation center)
  
Choose enemy with highest score
```

---

## ⚙️ **AI Configuration**

### **BattleAI Settings**
```csharp
[Header("AI Settings")]
public float decisionInterval = 1f;        // How often AI decides
public float targetUpdateInterval = 2f;   // How often targets update
public float retreatCheckInterval = 3f;   // How often retreat checked

[Header("Combat Behavior")]
public float preferredEngagementRange = 3f;  // Preferred fight distance
public float retreatDistance = 8f;           // How far to retreat
public float retreatHealthThreshold = 0.3f;  // Retreat at 30% health
public float retreatMoraleThreshold = 0.2f;  // Retreat at 20% morale

[Header("Tactical Behavior")]
public float flankingBonus = 1.5f;          // Flanking preference
public float formationBonus = 1.2f;         // Formation preference  
public float highGroundBonus = 1.3f;        // High ground preference
```

### **BattleAIManager Settings**
```csharp
[Header("AI Coordination")]
public float coordinationInterval = 2f;     // How often to coordinate
public float tacticalInterval = 5f;        // How often to assess situation

[Header("Formation Management")]
public float formationTightness = 2f;      // How tight formations are
public float maxFormationDistance = 8f;    // Max distance from formation
```

---

## 🎯 **AI Behaviors**

### **Balanced Fight**
- **Both sides**: Maintain formation, focus on flanking
- **Strategy**: Careful advance, coordinated attacks

### **Attacker Advantage**
- **Attackers**: More aggressive, push forward
- **Defenders**: More defensive, hold position

### **Defender Advantage**  
- **Defenders**: More aggressive, counter-attack
- **Attackers**: More defensive, regroup

### **Overwhelming Force**
- **Stronger side**: All units aggressive
- **Weaker side**: Retreat if low health, otherwise defend

---

## 🔧 **Customization**

### **Make AI More Aggressive**
```csharp
// In BattleAI.cs
public float retreatHealthThreshold = 0.1f;  // Retreat at 10% health
public float flankingBonus = 2.0f;          // More flanking preference
```

### **Make AI More Defensive**
```csharp
// In BattleAI.cs  
public float retreatHealthThreshold = 0.5f;  // Retreat at 50% health
public float formationBonus = 2.0f;         // More formation preference
```

### **Make AI Smarter**
```csharp
// In BattleAI.cs
public float decisionInterval = 0.5f;       // Decide twice as often
public float targetUpdateInterval = 1f;    // Update targets more often
```

---

## 🚀 **Advanced Features**

### **Formation Intelligence**
- Units maintain formation centers
- Regroup when too far from allies
- Coordinate based on tactical situation

### **Tactical Awareness**
- Assess battlefield situation
- Adapt behavior to force ratios
- Make strategic decisions

### **Smart Targeting**
- Multi-factor target scoring
- Consider distance, health, threat
- Prefer flanking and high ground

### **Retreat Logic**
- Health-based retreat thresholds
- Morale-based retreat conditions
- Outnumbered retreat triggers
- Surrounded retreat detection

---

## 🎮 **Player vs AI**

### **Player Control**
- **Left Click**: Select units
- **Right Click Enemy**: Attack
- **Right Click Ground**: Move
- **Escape**: Pause/Resume

### **AI Control**
- **Automatic**: All AI units act independently
- **Coordinated**: AI Manager coordinates strategy
- **Adaptive**: Behavior changes based on situation

### **Mixed Control**
- **Player units**: Manual control
- **AI units**: Automatic behavior
- **Both sides**: Can have mix of player/AI units

---

## 🔍 **Debugging**

### **AI State Display**
```csharp
// In BattleAI.cs
Debug.Log($"[BattleAI] {unit.data.unitName} state: {currentState}");
Debug.Log($"[BattleAI] {unit.data.unitName} targeting: {currentTarget.data.unitName}");
```

### **Tactical Situation**
```csharp
// In BattleAIManager.cs
Debug.Log($"[BattleAIManager] Tactical situation: {currentSituation}");
Debug.Log($"[BattleAIManager] A:{attackerCount} vs D:{defenderCount}");
```

### **Formation Centers**
```csharp
// In BattleAIManager.cs
Debug.Log($"[BattleAIManager] Attacker formation: {attackerFormationCenter}");
Debug.Log($"[BattleAIManager] Defender formation: {defenderFormationCenter}");
```

---

## 🎯 **Result**

The AI system creates **intelligent, tactical combat** where:
- **Units make smart decisions** based on battlefield conditions
- **Formations are maintained** and coordinated
- **Tactical positioning** matters (flanking, high ground)
- **Retreat logic** prevents suicidal charges
- **Strategic coordination** adapts to force ratios
- **Every battle feels different** based on unit composition and terrain

This makes the battle system **engaging and challenging** while still allowing player control when desired!
