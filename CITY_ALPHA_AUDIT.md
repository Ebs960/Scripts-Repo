# City and campaign presentation alpha audit

## Canonical city interface

`UI/City UI Rebuild.prefab` is the one canonical gameplay interface. `UI/City UI 2.prefab` is retained only as a serialized-content compatibility asset and must not be assigned to gameplay scenes or managers. `UI Manager.prefab` instantiates the rebuild when an existing scene instance is not assigned.

The rebuild organizes overview/yields, production, buildings and specialists, security/health, and unit storage into tabs. It exposes food, production, gold, science, culture, faith and policy; growth; citizen jobs and unemployment; production progress and queue count; governor assignment; building/district specialist capacity; order, morale, loyalty, defense, crime and disease; garrison, aircraft and missiles. Capital controls and all list rows must be authored references. Runtime manufacture of the capital button was removed.

Manual prefab work remaining is reported by **Tools > Alpha > Validate City UI and Campaign Animations**. In particular, optional references intentionally remain optional, while a missing tab controller or competing CityUI prefab is an error.

## Capture and presentation

Capture keeps gameplay authority in `City`, then invalidates production availability, refreshes an already-open city screen, assignment and political overlays, and recalculates vision for both owners. Movement remains authoritative before its visual coroutine; the coroutine follows its smoothed wrapped path, faces its tangent, and guarantees walking-state cleanup.

Ranged attacks now use `RangedAttack`; melee uses `Attack`. Missing Animator parameters produce a warning and skip presentation rather than sending an invalid trigger to Unity. The validator scans CombatUnit prefabs under `Units`, requires walking/hit/death for all, and selects melee or ranged attack requirements from the unit category. This includes infantry, archers, cavalry, artillery, animals and naval prefabs when they contain CombatUnit components.

## Validation interpretation

Animation errors identify content that must be fixed in its AnimatorController; they do not crash gameplay. Prefabs which are only visual/equipment children and contain no CombatUnit are intentionally ignored. Run the validator after importing the project so model controllers and nested prefab references are available.
