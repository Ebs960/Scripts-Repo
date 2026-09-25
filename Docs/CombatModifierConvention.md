# Combat modifier convention

Global attack, melee, ranged, city-attack, and defense values are fractional multipliers throughout runtime and serialized data: `0.10` means +10%. `CombatUnit.ApplyOwnerAttackBonuses` applies these as `base * (1 + modifier)`.

`movementBonus` is intentionally **not** migrated: tracing `Civilization` into unit movement shows the legacy field is added as flat movement points. Percentage movement belongs in `UnitStatBonus.movePointsPct`; flat movement belongs in `movePointsAdd`. This distinction prevents existing `movementBonus: 1` civilizations from becoming either +100% movement or +0.01 points.
