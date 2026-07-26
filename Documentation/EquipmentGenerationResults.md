# Equipment Generation Results

Generation date: 2026-07-26.

## Materialized content

- Equipment icons classified: **119**
- Projectile icons classified: **28**
- New `EquipmentData` assets created: **93**
- Existing icon-backed `EquipmentData` assets updated in place: **26**
- New `ProjectileData` assets created: **25**
- Existing icon-backed `ProjectileData` assets updated in place: **3**
- Legacy equipment/projectile assets retained and registered: **24**
- Shared `AbilityData` assets created: **3**
- Shared `StatusEffectData` assets created: **7**
- `EquipmentDatabase` assets created: **1**
- Unresolved icon files: **0**
- Ambiguous manifest stable IDs: **0**

All 147 manifest icons now have physical `.asset` and `.asset.meta` files. Existing icon-backed assets were updated at their original paths and retained their existing `.meta` GUIDs. Existing legacy assets were not deleted or recreated.

## Runtime effects represented

Poison, Burn, Expose, Suppression, Slow, Inspire, and Fortitude are shared status assets. Standards grant the shared Standard Bearer aura ability; firearms grant Marksman Accuracy; substantial armor grants Armor Discipline. Poison/fire ammunition and armor-disrupting weapons reference shared on-hit effects. No splash, knockback, active deployment, cooldown, or first-strike behavior was added because those trigger frameworks are not safely implemented.

## Validation status

Filesystem/reference validation passed: every manifest output exists, every output has metadata, stable IDs are unique, every icon GUID resolves, every database GUID resolves, and all 147 manifest records are cataloged. Unity batch import/validation did **not** run because this checkout lacks the surrounding Unity project root and the available editor could not obtain a valid headless license. The generated YAML therefore still requires Unity import verification in a licensed complete project; this report does not claim otherwise.
