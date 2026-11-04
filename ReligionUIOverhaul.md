# Religion UI Functional Overhaul

## Changed Files

Modified scripts:

- [ReligionUI.cs](ReligionUI.cs): pending belief editing, safe Apply/Cancel, lifecycle subscriptions, availability-driven founding views, icons, close control, and scrollable details.
- [Civilization.cs](Civilization.cs): atomic custom-belief application; shared pantheon eligibility; Faith notifications after founding and upgrading.
- [ReligionManager.cs](ReligionManager.cs): claimed-pantheon lookup and owned Holy Site validation.
- [BeliefSlotUI.cs](BeliefSlotUI.cs): reject category-mismatched drops with feedback.
- [BeliefButtonUI.cs](BeliefButtonUI.cs): restore parent, sibling, position, opacity, and raycasts after completion or interrupted dragging.

Created script:

- [ReligionPresentation.cs](ReligionPresentation.cs): `ReligionFoundingInfo`, `CityReligionInfo`, `ReligionPresentation`, and shared `ReligionEffectDescriptions`. Includes a new Unity metadata file.

Other assets:

- [UI/Religion UI.prefab](UI/Religion%20UI.prefab): correct `religionFoundingPanel` to the actual Religion Founding Panel, rather than Religion Info. Existing object IDs, slots, sprites, and borders are retained.
- This guide and its metadata. No gameplay data assets or save schemas were changed.

The Game and SampleScene scenes instantiate this prefab without overriding its founding-panel binding. Neither scene needed editing. Existing UIManager show/hide methods, the religion database, religion/pantheon/belief assets, and ReligionPoliticsService remain intact.

## Behavior

`Civilization.TryApplyCustomBeliefs` accepts a complete proposed custom loadout. Missing categories mean removal; null entries also mean removal. It snapshots and validates every entry before spending Faith or changing assignments. Validation includes known categories, matching belief categories, duplicates, pantheon restrictions, and affordability of the entire selection. The total uses a wide accumulator to avoid overflow.

Unchanged beliefs and removals are free. Replacements retain the existing full new-belief cost. Successful changes replace only `customAssignedBeliefs`, deduct Faith once, refresh modifiers/content/availability, revalidate policies, refresh sight, and then notify belief and Faith listeners. There is no per-belief purchase loop. Existing single-belief APIs delegate to this authority. Automatically granted beliefs, owned pantheons, and founded religions are not rewritten.

Opening or closing either panel never purchases beliefs. Pending edits survive closing/reopening for the same civilization and incoming gameplay events. Apply commits the complete selection or leaves it untouched with a reason. Cancel reloads the latest civilization assignments without spending Faith. Switching civilizations discards the old civilization's pending editor state without committing it.

Pantheon founding remains visible at any religion stage. The count/cap, owned pantheons, current founding cost, and existing prerequisites are shown; the action is disabled at capacity or when unavailable. Claimed assets are checked again by gameplay before purchase. Spirit upgrades show the God result, cost, requirements, and existing bonus descriptions. Upgrading still replaces the existing list entry rather than consuming another slot.

The religion list includes every configured religion, including locked and already-founded entries. Options carry existing icons and status. Selected details show the Faith cost, pantheon requirement, cultures, age, Holy Site requirement, founded status, and authoritative failure reason. `ReligionManager.CanFoundReligion` controls availability and rechecks the selected owned Holy Site city on click. Only `Civilization.FoundReligion` registers the new religion.

Faith, beliefs, turns, and state-religion events mark the view dirty. A dirty view refreshes once in LateUpdate; idle frames do not rebuild it. Immediate successful UI actions refresh their final state directly. Subscriptions are idempotent and detached on Hide, disable, destruction, and civilization switch.

Founded and state religions are displayed independently. No state-adoption action or bypass of political approval was added. Existing founding-time state sponsorship through ReligionPoliticsService is unchanged.

## Inspector And Artwork Handoff

Existing serialized fields and public entry points are preserved. No new required component needs to be attached for the current prefab to function. The five named slot references set their matching categories during initialization, correcting legacy prefabs in which every slot serialized as Survival.

New optional fields on ReligionUI:

| Field | Binding / fallback |
| --- | --- |
| `closeButton` | Main shell Close button. If absent, clone the existing belief Close control with its current styling. |
| `actionFeedbackText` | Inline action feedback. Otherwise use the existing UIManager notification. |
| `beliefCostText` | Total pending adoption cost. Otherwise display it on the Apply label. |
| `pantheonCountText` | Dedicated owned/cap count. The existing cost text also includes the count. |
| `foundedPantheonsText` | Dedicated list of all owned pantheons and tiers. Existing description also lists them. |
| `foundedReligionText` | Dedicated founded-religion value. Existing name and description provide fallback. |
| `stateReligionText` | Dedicated state-religion value, independent of founding. |
| `cityReligionOverviewText` | Optional compact read-only city overview. Without it, consume the `CityReligions` model in a future row view. |
| `selectedReligionIcon` | Optional dedicated preview image. Existing dropdown options also carry icons. |
| `religionRequirementsText` | Full selected religion details; otherwise included in the description region. |
| `upgradeDetailsText` | Full selected Spirit-to-God details; otherwise included in the description region. |

Keep these bindings on distinct text objects; do not bind a dedicated details field to the shared description object. Place dedicated long-form text and city lists in appropriately sized or scrollable regions during the artwork pass.

Missing dropdown caption/item images are created using existing asset sprites; configured images are left alone. Legacy cost/name labels use bounded autosizing and ellipsis, while full details remain available separately. If the description has no ancestor ScrollRect, initialization wraps the existing text in a transparent, masked, vertical ScrollRect within its original bounds and adds a ContentSizeFitter. An existing ScrollRect is respected. The generated main Close control is hidden while the belief panel's own Close control is active.

These are functional presentation components, not new artwork. The current font assets, shell, timeless belief borders, and all existing sprites remain in use. The main shell, existing pantheon/religion regions, five slots, drag layer, and Apply/Cancel controls can be skinned independently. No parchment, wood, or replacement border style was introduced.

`ReligionOptions` exposes read-only founding presentation rows, including asset/name/icon, cost, requirements, founded status, and authoritative availability. `CityReligions` exposes city, name, actual majority religion, Holy Site status, and a state-religion match flag. Effect formatting is shared outside the controller; existing public ReligionUI formatter methods forward to it for compatibility.

## Validation Results

- Full game source compilation passed with Unity 6000.4.9f1's compiler and current Bee response file, writing assemblies only to the temporary directory.
- The cached response file did not yet include the new presentation script, so the validation command explicitly included it. Unity must import the new source normally; use Assets > Refresh before play-mode verification if it has not appeared automatically.
- VS Code reported no errors in the six changed/created scripts.
- A temporary Roslyn harness executed actual source method bodies with Unity objects, scene infrastructure, and modifier calculation infrastructure stubbed: **79 assertions passed**. It covered both successful and rejected transactions/actions, reentrant callbacks clearing the source selection, costs, removals, overflow, categories, pending edits, subscriptions, turns, dragging, founding requirements, capacity, registration, overview data, and separate founded/state text.
- The extracted effect formatters matched the original git source for the tested compact/detailed, empty/long/null descriptions, improvements, herd effects, and attrition effects.
- The harness emitted two CS1701 assembly-unification warnings from its own Roslyn/.NET references; it executed successfully. These were not game compilation warnings.
- Prefab binding and new source/metadata final-newline checks passed; `git diff --check` passed.

Temporary validation sources/runners are under `%TEMP%` and are not gameplay assets or an installed Unity test suite. No Unity play-mode execution, rendered-layout verification, or save round-trip was performed. The source-body assertions are not substitutes for those integration tests.

### Requested Scenario Matrix

Every row still needs the listed Unity integration confirmation. "Source checked" means tested using the explicitly stubbed source-body harness, not a Unity pass.

| # | Scenario | Evidence / remaining Unity confirmation |
| --- | --- | --- |
| 1 | Open and close | Source show/hide checked. Verify generated main Close, overlay Close, initial scene activation, and scrolling in play mode. |
| 2 | Found pantheon | Source success/prerequisite failure checked. Verify live assets and gameplay prerequisites. |
| 3 | Additional pantheons | Source multiple/cap/post-religion founding checked. Verify count/list/buttons in play mode. |
| 4 | Upgrade pantheon | Source cost/replacement/unavailable checks passed. Verify live bonuses and UI detail selection. |
| 5 | Found religion | Source Holy Site/ownership/culture/age/pantheon checks passed. Verify live Holy Site districts and tile data. |
| 6 | Religion claimed by another civ | Source duplicate/global-limit checks passed. Verify live multiplayer/AI turn state. |
| 7 | Locked religion reasons | Source requirements/status model checked. Verify long names, icons, dropdown templates, and text clipping visually. |
| 8 | Matching categories only | Source UI/drop/gameplay rejection and slot-default repair checked. Verify real pointer drag/drop in Unity. |
| 9 | Multiple belief apply | Reentrant source-selection clearing checked without exception. Verify actual gameplay listeners. |
| 10 | Total belief cost | Source multi-purchase, unchanged, removal, and replacement costs checked. Verify Faith HUD and panel together. |
| 11 | Insufficient Faith | Source atomic rejection checked; no partial mutation. Verify disabled controls and displayed feedback. |
| 12 | Cancel | Source live-selection reload and no spending checked. Verify button wiring and panel transitions. |
| 13 | Civilization bonuses | Refresh/notification count and ordering checked with modifier infrastructure stubbed. Real city/unit/policy/sight bonus values require Unity. |
| 14 | No duplicate listeners | Source repeated Show, Hide, disable/reenable, and switch counts checked. Verify actual Unity lifecycle order. |
| 15 | Faith refresh | Source Faith/turn dirty refresh and idle behavior checked. Verify live gains, founding/upgrades, and HUD. |
| 16 | Civilization switch | Source subscription/state cleanup checked. Verify full live visual rebinding. |
| 17 | City majority | Actual majority algorithm and overview model checked against stubbed tile pressures. Verify live TileSystem pressure data. |
| 18 | Founded versus state | Source distinct values checked. Verify political state changes in a live game. |
| 19 | Save/reference compatibility | Existing fields/assets/GUIDs and save schema preserved; prefab binding inspected. Old-save load/save and scene round-trip not run. |
| 20 | Compilation | Full current source compile passed, including the new helper explicitly. Unity editor import/player build not run. |

## Remaining Limits

- City distribution is read-only. No follower counts, percentages, conversion actions, missionaries, or spread changes were added. Numerical pressure totals are intentionally not exposed by this initial binding model.
- Availability presents the manager's first authoritative failure plus the full configured requirement list, not a new progression system.
- Existing Holy Site behavior remains authoritative: the selector/overview uses City.HasHolySite, while founding checks the selected city's center-tile Holy Site flag. Inconsistent district/tile data can therefore leave a listed city unavailable; this overhaul does not migrate that gameplay data.
- Existing code that changes the public Faith field directly without raising an event is reflected on the next turn or explicit view refresh. The founding/upgrade actions touched here now publish Faith changes.
- No balancing values, religion content, cap-increase mechanics, political approval rules, save format, or underlying modifier formulas were changed.
