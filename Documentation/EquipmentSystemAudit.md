# Equipment System Audit

## Scope and inventory

Audit date: 2026-07-26. The authoritative icon search is `AssetDatabase.FindAssets("t:Sprite", equipment folders)`; the checked-in source set contains **147 actual `* Icon.png` equipment/ammunition sprites** after excluding `Textures and Junk`, preview texture `shaded.png`, and non-icon model textures. There are 53 pre-existing equipment/projectile `.asset` files in `Units/Equipment`; 13 unit assets have a non-null serialized default weapon. Existing data assets are matched and updated in place, never recreated.

The audit covered the requested runtime/data classes, equipment UI (`EquipmentButton`, `EquipmentManagerPanel`), city production paths, all equipment/projectile assets and icons, technologies, cultures, resources/buildings, and unit data across the age folders and combat categories. Asset generation deliberately resolves project objects by path/name through `AssetDatabase`, reports non-unique names, and retains existing prefab/visual references.

## Effective combat formulas

`CombatUnit.GetTargetedAttackValue` splits attack into two terms:

```
unitAttack   = (unitBase + situationalUnitAdd) * (1 + situationalUnitPct)
weaponAttack = (weaponBase + targetedWeaponAdd) * (1 + targetedWeaponPct)
attackValue  = unitAttack + weaponAttack
```

This is the required specialization behavior: a matchup percentage modifies only the active weapon contribution, never the unit's base attack. `unitBase` selects the target-domain attack when populated, otherwise the legacy melee/ranged/city value, and includes progression/equipment-independent ability attack. `weaponBase` is general `attackBonus` plus the relevant typed (`meleeAttackBonus`, `rangedAttackBonus`, `cityAttackBonus`) and domain bonus. Therefore general `attackBonus` **does stack additively** with the applicable typed bonus.

For ordinary unit combat:

```
rawDamage = max(0, attackValue - effectiveDefense - biomeDefense - (hill ? 2 : 0))
damage = round(rawDamage * abilityDamageMultipliers * chargeMultiplier)
```

Shared melee modifiers are then applied. Worker targets use the same subtraction without terrain defense and grant combat units a flat +2 attack contribution. Counterattacks use the same targeted split. City, naval-surface, underwater, air, and space attacks use the same target-domain selection; legacy typed fallback is used only when a domain base value is absent. Owner tech/culture/building/resource modifiers, fatigue (down to 70% at maximum fatigue), unmet upkeep, and gold-maintenance modifiers enter through the queried attack/defense properties.

Defense is subtractive, so its time-to-kill impact depends on the attack-defense margin rather than a universal percentage. Health is additive to the damage pool. Approximate attacks-to-kill is `ceil(effectiveHealth / max(1, damagePerAttack))`; the validator must consequently simulate representative pairs rather than compare raw bonus totals. Range controls target eligibility and retaliation opportunities. Movement controls reach, positioning, and the existing charge condition; because typical values are small, +1 movement and +1 range consume a major item budget.

## Projectiles and hit effects

A projectile weapon resolves the unit's compatible active projectile first and its weapon default second. Category equality gates compatibility. The ranged weapon/unit calculation produces `overrideDamage`; `SpawnProjectileFromEquipment` adds `round(projectile.damage)` exactly once. Damage and hit processing occur on projectile impact, not launch. The legacy `ProjectileData.statusEffect` was previously applied as a guaranteed effect after every damaging hit. It remains guaranteed for backward compatibility. New shared applications support chance, duration override, magnitude scaling, melee/ranged filtering, target category/domain filters, and self/target routing.

Equipment on-hit applications are evaluated for the active melee weapon in the contextual damage path and for the current projectile weapon at projectile impact. Descriptions generated from the manifest may only mention effects represented in these runtime fields.

## Ability ownership

`unlockedAbilities` is the progression collection. Equipment passives are rebuilt into a distinct, non-serialized runtime collection whenever a slot changes. Effective stat and aura queries enumerate both collections. Replacing or removing equipment therefore removes its passive instances without mutating level-earned abilities.

## Discovery and production

The old runtime cache loaded `Resources/Equipment`, although assets live under `Units/Equipment`; that is not build-safe. `EquipmentDatabase` is now the primary explicit catalog for equipment, projectiles, shared passives, and shared statuses, with `Resources.LoadAll` retained as fallback. The editor generator deterministically rebuilds the database under `Resources/Equipment`.

Availability remains asset-driven through `requiredTechs` and `requiredCultures`. Production also checks resource costs; equipment supports building requirements, manufacturing capability tags, substitute resource groups, gold price, and upkeep. The UI obtains fields directly from these assets and does not discover arbitrary sprites.

## Existing-data findings

- Only 53 data assets cover the 147 actual icons; the manifest is the complete source of truth for the missing set.
- Existing assets lack stable identity/presentation fields because the schema did not previously contain them.
- Existing prefab, projectile visual, icon, technology, culture, building, and resource references must be retained unless a manifest field explicitly and unambiguously replaces them.
- The legacy per-category arrays coexist with newer targeted modifiers. Generic manifest weapons contain no targeted category bonus; only semantically specialized entries do.
- Existing assets outside a Resources path were invisible to the player-build fallback. The explicit database resolves this.
- Unit base statistics are not changed in this implementation. A later unit rebalance should begin only after Unity executes the generation and balance simulation report.

## Remaining editor-side audit

Run the five `Tools > Equipment` commands in Unity. Dry run and validation intentionally stop on missing/ambiguous project references instead of guessing. Visual prefab alignment, icon import correctness, and culture-specific historical naming require human review in the Unity Inspector after generation.
