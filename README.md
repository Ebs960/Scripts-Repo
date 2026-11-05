# historygame

## Faction-selection illustrations

On each playable `CivData`, assign the optional `factionSelectionArtwork` Sprite.
The slot supports cinematic illustrations for every civilization without a
hard-coded faction count. Import text-free 16:9 landscape masters as Sprite
(2D and UI). Keep the original `icon` assigned: it is still the civilization
emblem, not the hero illustration. No artwork is generated or assigned to assets
automatically.

`MainMenuManager` builds a dedicated masked hero Image when
`selectedCivArtwork` is unassigned. Artwork is centered and uniformly scaled to
cover a 16:9 viewport; non-16:9 sources are cropped, never stretched. The shell,
labels, emblems, and navigation remain separate. The roster and details scroll;
narrow layouts stack the roster above the details.

Artwork comes only from the civilization's `factionSelectionArtwork` Sprite,
or `fallbackCivSelectionArtwork` when unassigned. Without either, a navy
placeholder appears. Illustrations are rendered only in the hero Image,
never over the shell or emblem.

The roster comes exclusively from the catalog supplied through
`ResourceCache` (`GameManager.civilizationCatalog` / base-game content database).
Nulls, duplicate civilization references, tribes, and city-states are excluded.
Buttons are created from
`civButtonPrefab` (or a plain UI button if no prefab is assigned). Reopening
selection reuses generated buttons, clears the previous choice/artwork, and
disables Next until another civilization is chosen. Back from Leader Selection
retains the selected civilization and refreshes its artwork. Leader artwork and
gameplay data are unchanged.

Run **Tools > Main Menu > Validate Faction Selection** in Unity to exercise 21
synthetic playable factions, excluded/duplicate entries, several artwork/fallback choices,
original emblems/bonuses, repeated entry, Back/Next, and masked aspect-ratio
layout at 1920x1080, 2560x1440, 3440x1440, 1024x768, and 800x1280. The validator
uses temporary objects in a preview scene and restores the civilization cache;
it does not save scenes, data assets, or artwork.

## Expanded civilization roster

The configured catalogs now include 20 playable civilizations, six tribes, and
three city-states (29 entries total). Goths are a non-playable tribe; their
existing leaders and other data are preserved. The five additions use existing modifier systems,
with 15% total base bonuses and 20% total leader bonuses as an initial balance
budget. These are starting values, not a claim of playtested balance.

| Civilization | Base bonuses | Leader 1 | Leader 2 |
|---|---|---|---|
| America | +10% Production, +5% Culture | George Washington: +10% Production, +10% military attack | Theodore Roosevelt: +10% Science, +10% Culture |
| Incan Empire | +10% Food, +5% Production | Pachacuti: +10% Production, +10% Culture | Huayna Capac: +10% Food, +10% Gold |
| India | +10% Faith, +5% Food | Ashoka: +10% Faith, +10% Culture | Chandragupta Maurya: +10% Gold, +10% military attack |
| Greece | +5% Science, +5% Culture, +5% Gold | Pericles: +15% Culture, +5% Science | Alexander the Great: +15% military attack, +5% Science |
| Spain | +5% Gold, +5% Faith, +5% Production | Isabella I: +10% Faith, +10% Gold | Philip II: +10% Production, +10% Gold |

Each has twelve city names, four herd names, a description, and two leaders
with biographies, named abilities, agendas, diplomatic traits, victory
preferences, and AI priorities. Abilities describe the actual flat percentage
modifiers; no extra scripted mechanics or unique units/buildings are implied.
The base-game content database, Game Manager catalog, and standalone CivManager
pool all register the additions for player and AI selection. Player builds
refresh the build-safe content database through the existing build processor.

All five start with the shared Band and two starting combat units, plus shared
herd prefabs and music. America/Spain reuse Western settlement models; Greece
uses Greco-Roman models in the Ancient/Classical periods. India uses the existing
South Asian models. The Incan Empire uses Mesoamerican models as explicit visual
placeholders, not as its culture identity: `CultureGroup.Andean = 14` and its
governor-name pool are separate. Existing culture enum values are unchanged.
For these new civilizations, metropolis slots reuse city models, and
Modern/Information/Futuristic periods reuse Enlightenment models until dedicated
assets are authored.

Civilization emblems, faction illustrations, and leader artwork intentionally
remain unassigned. The faction screen uses its normal missing-art fallback.
No existing leader assets are modified.

Run **Tools > Main Menu > Validate Civilization Catalog** to check the actual
20-faction playable catalog, Goths' tribe classification, all five new identities, both leaders per addition, bonus
budgets/application, AI settings, UI selection, and progression to Game Setup.