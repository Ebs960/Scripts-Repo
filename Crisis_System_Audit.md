# Crisis System Audit

## Executive Summary

This static, repository-wide audit covers **18 crises, 53 missions, 53 legacies, and 50 objective types**. It distinguishes serialized content from runtime wiring. The Word-compatible audit document is the authoritative row-level audit; this report summarizes the most consequential findings.

Across crisis, mission, objective, and legacy audit rows there are **97 PASS**, **42 WARNING**, **35 BROKEN**, and **0 UNVERIFIED** statuses. The issue register contains **10 P0**, **11 P1**, **2 P2**, and **0 P3** items.

## Critical Technical Problems

- **10 used objective types have no progress producer.** Their missions cannot normally complete.
- Several declared mechanics are narrative/targeting shells: activation only explicitly implements disease, locusts, genetic mutation, aliens, and asteroid behavior; Drought and PredatorSurge primarily rely on world overrides.
- `CanAutoTrigger` rejects anything in `crisisHistory`, bypassing repeat-mode/cooldown occurrence logic used elsewhere.
- Mission progress is sequential: only `CurrentObjective` accepts events, so valid actions performed before a later objective activates are discarded.

## Objective Tracking Problems

The P0 set is: **BuildImprovementsInUnaffectedArea, DefendCityBattles, DestroyCrisisSpawners, NegotiateAutonomy, RaidSettlements, ReclaimCity, ReintegrateCrisisUnits, RepairImprovements, RestoreDisabledBuildings, RestoreSubjectControl**. `RepairImprovements` also exposes a manager method but no gameplay caller was found, so it is treated as broken rather than implemented. `AchieveIndependence` is defined but unused and unwired. Generic polling is concentrated in `PollTurnObjectives`; event-driven paths depend on concrete subscriptions/callers rather than enum presence.

## AI Problems

`SelectAiMissions` chooses the highest strategy-tag score among legal missions. `ExecuteAiCrisisAction` only deliberately performs alien negotiation and mutant integration. Asteroid interception separately queues production for idle AI cities. Other economic, repair, combat, political, survival, and diplomacy goals have no mission-specific planner: they are incidental, impossible when the objective is unwired, or selected despite infeasibility.

## Balance Problems

- Fixed survival and maintenance targets can be trivial or impossible depending on crisis duration and selection timing.
- Per-city/per-population targets use clamps, but fixed political and combat targets do not consistently scale with affected scope.
- `PercentOfBaseline` target resolution uses the serialized target directly while polling computes a percentage; this works for 100-style targets but is easy to mis-author.
- Consecutive-turn checks add a separate streak requirement after comparison; deadlines must exceed both setup time and streak length.
- Crisis-end objectives can appear satisfied early but cannot complete until resolution, and an intercepted asteroid intentionally makes infrastructure preservation 100%.

## Legacy Problems

All serialized legacy values and reward GUIDs are listed in the complete Word audit document. Effects are not inferred from descriptions: the audit reports non-zero fields and checks the central `LegacyManager`/targeted consumer architecture. Unawarded assets and description-only assets are flagged. Legacy effects apply while promoted, while ownership remains permanent; promotion-slot limits reduce stacking but repeat reward substitution should still be play-tested.

## Crisis-by-Crisis Findings

### Alien Invasion

**PASS** — Mechanic `AlienLanding`, scope `Global`, 3 missions/3 legacies. No blocking wiring defect found in static trace. Balance: Fixed windows and targets can vary sharply by empire size.

### Asteroid Strike

**PASS** — Mechanic `AsteroidCountdown`, scope `Continent`, 2 missions/2 legacies. No blocking wiring defect found in static trace. Balance: Fixed windows and targets can vary sharply by empire size.

### Barbarian Invasion

**BROKEN** — Mechanic `HostileRaiders`, scope `Global`, 3 missions/3 legacies. HostileRaiders is selected/targeted but ApplyCrisisMechanicOnActivation has no branch for it; 3 mission(s) use objective types with no progress producer Balance: Fixed windows and targets can vary sharply by empire size.

### Cannibal Raids

**BROKEN** — Mechanic `HostileRaiders`, scope `Global`, 3 missions/3 legacies. HostileRaiders is selected/targeted but ApplyCrisisMechanicOnActivation has no branch for it; 1 mission(s) use objective types with no progress producer Balance: Fixed windows and targets can vary sharply by empire size.

### Constitutional Crisis

**BROKEN** — Mechanic `ConstitutionalConflict`, scope `Civilization`, 3 missions/3 legacies. ConstitutionalConflict is selected/targeted but ApplyCrisisMechanicOnActivation has no branch for it Balance: Fixed windows and targets can vary sharply by empire size.

### Coup Attempt

**BROKEN** — Mechanic `PoliticalCoup`, scope `Civilization`, 3 missions/3 legacies. PoliticalCoup is selected/targeted but ApplyCrisisMechanicOnActivation has no branch for it Balance: Fixed windows and targets can vary sharply by empire size.

### Domestic Terrorism

**BROKEN** — Mechanic `TerroristCells`, scope `Civilization`, 3 missions/3 legacies. TerroristCells is selected/targeted but ApplyCrisisMechanicOnActivation has no branch for it Balance: Fixed windows and targets can vary sharply by empire size.

### Drought

**BROKEN** — Mechanic `Drought`, scope `Global`, 3 missions/3 legacies. 1 mission(s) use objective types with no progress producer Balance: Fixed windows and targets can vary sharply by empire size.

### Financial Crisis

**BROKEN** — Mechanic `FinancialShock`, scope `Global`, 3 missions/3 legacies. FinancialShock is selected/targeted but ApplyCrisisMechanicOnActivation has no branch for it Balance: Fixed windows and targets can vary sharply by empire size.

### Genetic Disaster

**PASS** — Mechanic `GeneticMutation`, scope `Civilization`, 3 missions/3 legacies. No blocking wiring defect found in static trace. Balance: Fixed windows and targets can vary sharply by empire size.

### Great Plague

**PASS** — Mechanic `DiseaseOutbreak`, scope `Global`, 3 missions/3 legacies. No blocking wiring defect found in static trace. Balance: Fixed windows and targets can vary sharply by empire size.

### Industrial Disaster

**BROKEN** — Mechanic `IndustrialDamage`, scope `Global`, 3 missions/3 legacies. IndustrialDamage is selected/targeted but ApplyCrisisMechanicOnActivation has no branch for it; 1 mission(s) use objective types with no progress producer Balance: Fixed windows and targets can vary sharply by empire size.

### Locust Infestation

**BROKEN** — Mechanic `LocustInfestation`, scope `Global`, 3 missions/3 legacies. 2 mission(s) use objective types with no progress producer Balance: Fixed windows and targets can vary sharply by empire size.

### Popular Revolution

**BROKEN** — Mechanic `PopularRevolution`, scope `Civilization`, 3 missions/3 legacies. PopularRevolution is selected/targeted but ApplyCrisisMechanicOnActivation has no branch for it Balance: Fixed windows and targets can vary sharply by empire size.

### Predator Hunting Season

**WARNING** — Mechanic `PredatorSurge`, scope `Global`, 3 missions/3 legacies. No blocking wiring defect found in static trace. Balance: Fixed windows and targets can vary sharply by empire size.

### Robot Uprising

**BROKEN** — Mechanic `RobotDefection`, scope `Civilization`, 3 missions/3 legacies. RobotDefection is selected/targeted but ApplyCrisisMechanicOnActivation has no branch for it; 2 mission(s) use objective types with no progress producer Balance: Fixed windows and targets can vary sharply by empire size.

### The Long Cold

**WARNING** — Mechanic `None`, scope `Global`, 4 missions/4 legacies. No blocking wiring defect found in static trace. Balance: Fixed windows and targets can vary sharply by empire size.

### War of Independence

**BROKEN** — Mechanic `IndependenceWar`, scope `SubjectRelationship`, 2 missions/2 legacies. IndependenceWar is selected/targeted but ApplyCrisisMechanicOnActivation has no branch for it; 2 mission(s) use objective types with no progress producer Balance: Fixed windows and targets can vary sharply by empire size.

## Recommended Fix Order

1. **P0:** Wire every used broken objective to the actual successful gameplay operation, including filters and attribution; add end-to-end play-mode tests.
2. **P1:** Implement or explicitly remove signature mechanics that currently have no activation/tick/cleanup path, then verify save/load.
3. **P1:** Prevent AI from selecting objectives it cannot deliberately execute, then add objective-specific action plans.
4. **P2:** Route auto-trigger history checks through repeat/cooldown occurrence logic.
5. **P2:** Revisit fixed targets, deadlines, sequential counter loss, and baseline-percent authoring after mechanics are functional.
6. **P3:** Reconcile reward prose, serialized effects, and UI terminology; add validation tooling for broken GUIDs and unreachable legacies.
