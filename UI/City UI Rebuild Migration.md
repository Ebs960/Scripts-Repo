# City UI Rebuild: continuous-sheet prefab migration

The current `City UI Rebuild.prefab` is still the legacy full-screen hierarchy and contains several independent nested scroll views. Its YAML does not encode the requested five-section sheet, so it should be migrated in the Unity Editor rather than by assigning invented file IDs in text.

## Required hierarchy

Under the existing `City UI Rebuild` root, create a right-anchored `CityPanelRoot` sized as a vertical drawer. Keep these as direct, fixed descendants so they never move with scrolling:

- `Background` — an `Image` using the authored tall panel sprite.
- `FixedCityHeader` — reparent the existing city name, population/owner text, capital controls, and X button here.
- `VerticalNavigation` — a vertical layout containing `CitizensButton`, `ProductionButton`, `BuildingsButton`, `CrimeDiseaseButton`, and `UnitStorageButton`.
- `ScrollView` — the only moving view. Configure its `ScrollRect` with Vertical enabled, Horizontal disabled, Movement Type **Clamped**, and Inertia enabled.

Create `ScrollView/Viewport/Content`. Give `Viewport` a `RectMask2D`. Give `Content` a `VerticalLayoutGroup` and a `ContentSizeFitter` with Horizontal Fit **Unconstrained** and Vertical Fit **Preferred Size**. Under `Content`, in this exact order, create:

1. `CitizensAndTilesSection`
2. `ProductionSection`
3. `BuildingsAndSpecialistsSection`
4. `CrimeAndDiseaseSection`
5. `UnitStorageSection`

All five section GameObjects must remain active. Give each section a `LayoutElement` or `ContentSizeFitter` appropriate to its children; do not use nested category tabs or fixed heights that clip generated rows.

## Reparent existing controls

- **CitizensAndTilesSection:** food/growth, yields, citizen job summary, unemployment/order summary, citizen assignment button, governor controls, and capital controls if they are not kept in the fixed header.
- **ProductionSection:** current production, production queue, Buildings title/container, Units title/container (combat and worker rows share it), Equipment title/container, and Missiles title/container. Delete or deactivate the legacy `Projectiles Title` and `Projectiles Container` objects.
- **BuildingsAndSpecialistsSection:** move the existing `Buildings & Specialists Panel` contents here.
- **CrimeAndDiseaseSection:** move the existing `Crime & Disease Panel` contents and disease list here.
- **UnitStorageSection:** move the existing `Unit Storage Panel` contents here.

Remove the old category `ScrollRect` components after their contents are reparented; generated option containers must grow inside the one outer sheet. Remove `CityUITabController` from this migrated prefab (the script remains in the project for old prefabs). Do not connect either controller to panel `SetActive` calls.

## Inspector assignments

Add `CityUIScrollNavigator` to `CityPanelRoot` and assign:

- **Scroll Rect:** `ScrollView`
- **Content Root:** `ScrollView/Viewport/Content`
- **Citizens And Tiles Section:** `CitizensAndTilesSection`
- **Production Section:** `ProductionSection`
- **Buildings And Specialists Section:** `BuildingsAndSpecialistsSection`
- **Crime And Disease Section:** `CrimeAndDiseaseSection`
- **Unit Storage Section:** `UnitStorageSection`
- The five optional button fields to their matching fixed navigation buttons.

On the existing `CityUI` component, assign **Continuous City Sheet / Scroll Navigator** to that navigator. Keep **City Feature Tabs / Tab Controller** empty on this prefab; it remains only as a legacy-prefab fallback. Preserve every existing gameplay reference while reparenting. The serialized `projectilesContainer` reference may remain assigned for compatibility—the runtime hides it and generates no projectile rows.
