# Unit and equipment balance policy

This document defines the targets to use before making broad changes to unit and
equipment assets. Balance passes should compare like-for-like units within a
category; an age-wide multiplier must not be used to hide a category regression.

## Equipment's role

Equipment remains a meaningful part of combat power throughout the game. A
complete, ordinary, same-age loadout should provide **20–30% of the relevant
baseline combat stat** of a standard unit in its category. Individual pieces do
not each need to meet that target. Tactical, rare, and culture-specific items may
trade raw stats for abilities, targeting modifiers, mobility, or narrower unit
eligibility.

Later-age equipment must continue alongside the unit progression. When raw
bonuses would make combat too volatile, prefer percentage-based or conditional
bonuses over allowing equipment to become numerically irrelevant.

## Category curves

Review and tune each of these curves independently:

| Curve | Primary stats | Cost considerations |
| --- | --- | --- |
| Infantry | attack, defense, health | baseline production and gold |
| Cavalry | attack, health, movement | mobility and mounted premiums |
| Artillery | ranged/city attack, range | setup and vulnerability tradeoffs |
| Surface ships | attack, defense, health | harbor requirement and naval utility |
| Submarines | underwater attack, stealth utility | specialized targeting |
| Aircraft | mission attack, range, interception | airport and mission constraints |
| Fighting vehicles | ground attack, defense, health | resource and upkeep requirements |
| Spacecraft | domain attacks, defense, travel | spaceport and travel capability |
| Workers, animals, and other units | role-specific output or utility | availability rather than combat parity |

For every age transition, report the median standard-unit stat and cost within
each category. A decrease is a review flag, not an automatic error: light units,
specialists, and units with unusually strong abilities may legitimately sit below
the category curve when that tradeoff is documented in their description.

## Costs

Production and gold should normally rise within a category as a unit's total
power rises. Review Classical-to-Dark, Feudal-to-Castle, and
Modern-to-Information transitions explicitly. Free equipment must state a real
non-production acquisition path; otherwise it needs production and gold costs.

The War Elephant is priced as a premium Iron Age heavy-cavalry unit: 180
production and 420 gold. Its exceptional attack and health should not be
available for free.

## Ordinary weapon differentiation

Two-handed ordinary weapons exchange shield access and additional cost for a
larger melee bonus. One-handed swords retain the lower cost and ability to pair
with a shield. This rule differentiates the Bronze Sword/Bronze Two-Handed Axe,
Iron Sword/Iron Two-Handed Axe, and Steel Sword/Great Sword pairs without adding
special abilities to otherwise ordinary equipment.

## Upgrade and obsolescence rules

Only genuine replacements should use `upgradesTo`; parallel sidegrades and
culture-specific alternatives should remain separate. Each proposed chain must:

1. keep the same battlefield role and compatible combat category;
2. move forward in age and avoid cycles;
3. preserve a deliberate specialist branch instead of collapsing it into a
   generic unit;
4. identify what happens to equipped items that the replacement cannot use; and
5. be validated as a complete chain from the obsolete asset to a currently
   obtainable replacement.

Upgrade chains should be added category by category after the corresponding stat
and cost curve is reviewed. This prevents automation from directing players into
a nominally newer but weaker or cheaper regression.

## Review order

1. Fix zero-cost assets without a documented alternate acquisition path.
2. Establish category medians and investigate mid-game regressions.
3. Extend equipment coverage so same-age loadouts meet the 20–30% target.
4. Add and validate genuine replacement chains one reviewed category at a time.
5. Revisit armor-defense dips in the Dark and Colonial Ages and clearly describe
   light-versus-heavy tradeoffs that are intentional.

## Audit notes

The equipment asset scan must distinguish `EquipmentData` from `ProjectileData`;
both are stored under `Units/Equipment`, but projectile damage and on-hit effects
are not equipment stat bonuses. Projectile variants that share costs are not
substitutes when their damage, status effects, application chance, or launch
behavior differs.

Although a zero `goldCost` does not block normal production, every producible
equipment item now supports instant purchase for consistency. When no bespoke
price has been designed, its initial gold price is 175% of production cost, rounded
up to a whole number. Designers may depart from that fallback to price scarcity,
flexibility, or tactical impact. The Iron Leather Helmet uses that same ratio as
the Iron Leather Armor and Leather Shield, scaled down for its smaller slot
contribution.

Legacy assets also need explicit age metadata so age-based comparisons do not
silently place them in the enum default. The Iron Leather Helmet and Paleolithic
Stone Throwing Spear now declare their intended ages. Future audits should flag
missing `equipmentAge` fields independently from stat and cost outliers.
