# Space Map System Documentation

## Overview
The Space Map System allows players to travel between multiple planets in a solar system. Each planet is generated once and persists in its own scene, allowing players to return to previously explored worlds without losing progress.

## Key Components

### 1. SolarSystemManager
- **Purpose**: Manages multiple planet scenes and solar system data
- **Location**: Auto-created as singleton, persists across scenes
- **Key Methods**:
  - `SwitchToPlanet(int planetIndex)` - Travel to a planet
  - `GetPlanetData(int planetIndex)` - Get planet information
  - `OpenSpaceMap()` - Show the space map UI

### 2. SpaceMapUI
- **Purpose**: Custom UI showing planets and civilizations
- **Features**:
  - Visual planet layout
  - Civilization information per planet
  - Travel confirmation
  - Planet status indicators (home world, visited, current)

### 3. PlanetTransitionLoader
- **Purpose**: Loading screen for planet transitions
- **Features**:
  - Travel-themed loading messages
  - Progress bar
  - Visual effects (optional)
  - Sound effects (optional)

### 4. SpaceMapButton
- **Purpose**: Integration component for existing UI
- **Usage**: Attach to any button to add space map functionality
- **Hotkey**: Press M to open space map (configurable)

## Setup Instructions

### Quick Setup (Recommended)
1. Add `SpaceMapIntegrationExample` to any GameObject in your game scene
2. Enable both `addSpaceMapButton` and `createStandaloneSystem`
3. Run the game - the system will auto-configure

### Manual Setup
1. Create a GameObject with `SolarSystemManager` component
2. Create a GameObject with `PlanetTransitionLoader` component  
3. Add `SpaceMapButton` to your existing UI buttons
4. Optionally create a GameObject with `SpaceMapUI` for custom UI

### Integration with Existing Game
1. Find your main game UI canvas
2. Add a new button for the space map
3. Attach `SpaceMapButton` component to the button
4. The system will handle the rest automatically

## Planet Types
The system supports various planet types with different characteristics:
- **Terran**: Earth-like, balanced
- **Desert**: Hot and dry  
- **Ocean**: Mostly water/islands
- **Ice**: Cold and frozen
- **Volcanic**: Hot with mountains
- **Jungle**: Hot and humid
- **Rocky**: Barren and mountainous
- **Gas**: Gas giant (no surface)

## How It Works

### First Visit to a Planet
1. Player clicks on unvisited planet in space map
2. System shows "Explore Planet?" confirmation
3. New scene is created for the planet
4. GameManager generates the world based on planet type
5. Civilizations are spawned and scanned
6. Planet is marked as generated and visited

### Returning to a Planet
1. Player clicks on visited planet in space map
2. System shows "Travel to Planet?" confirmation
3. Existing scene is loaded/activated
4. All previous progress is preserved (cities, units, etc.)

### Planet Persistence
- Each planet gets its own scene that persists
- GameManager instance exists per planet
- Civilization progress is maintained
- Map terrain and improvements are saved

## Controls
- **M Key**: Open space map (configurable)
- **Mouse**: Click planets to select them
- **Space Map UI**: 
  - Yellow planets = Home world
  - Cyan planets = Current planet
  - Green planets = Visited planets
  - Gray planets = Unvisited planets

## Customization

### Adding Custom Planet Types
1. Add new values to `PlanetType` enum
2. Update `ConfigureGameSetupForPlanet()` in SolarSystemManager
3. Add planet names to `GeneratePlanetName()` array

### Customizing Planet Generation
Edit the `ConfigureGameSetupForPlanet()` method to change:
- Map types per planet
- Number of continents/islands
- Climate settings (temperature, moisture)
- Civilization counts

### UI Customization
- Modify `SpaceMapUI` for different layouts
- Change colors in the inspector
- Add custom planet icons
- Implement custom transition effects

## Contextual Selection Panels

`SpaceMapWorldController` is the only owner of space map selection and input. It selects a
planet, a fleet, a single spacecraft, or a space sector, and tells `SpaceMapUI` what to show.
`SpaceMapUI` only displays: it shows exactly one right-side panel at a time, all saved in
`UI/Space Map UI.prefab` and all inactive until something is selected.

| Selection | Panel | Artwork (`UI/Space UI`) |
| --- | --- | --- |
| Planet marker | Planet Information Panel | Planet Information Sidebar |
| Fleet marker | Fleet Overview Panel | Fleet Overview Shell, Fleet Roster Row |
| Ship, or a roster row | Individual Spacecraft Panel | Individual Spacecraft Card |
| Empty sector | Space Infrastructure Panel | Space Infrastructure Shell, Space Structure Card |

- Every fleet gets its own map marker (`SpaceFleetView`), so packed fleets stay selectable while
  their ships are inactive. An unpacked fleet's marker sits beside its first surviving ship.
- Roster rows resolve members from `SpaceFleet.memberUnitIds` through `UnitRegistry` on every
  refresh. A missing or destroyed member shows as such and cannot be opened.
- Opening a ship from the roster is inspection only: it stays in its fleet and cannot be given a
  movement order. Movement orders (pick a sector, then Confirm or click it again) apply only to a
  was selected directly on the map. Spacecraft use their remaining movement points; queued paths
  resume when their civilization starts its next turn. Packed members cannot receive ship orders.
- Back to Fleet appears only when the ship's fleet also lists it as a member, and returns to that
  fleet with the roster scroll position kept.
- Unexplored planets show "Unknown" for atmosphere, temperature, colonization, and
  civilizations. Features marked hidden-until-detected, hidden resources, and undetected hazards
  are not listed. A foreign ship's route is not shown.
- Pack Fleet and Deploy Fleet call the existing `FleetDeploymentManager` and are offered only for
  the player's own fleets. The panels add no other actions.
- Panels re-read live data four times a second and clear themselves when their fleet, ship, or
  sector no longer exists. Closing the map clears the selection.

All display text is produced by `SpaceMapSelectionInfo`, which reads live objects and stores nothing.

## Map Markers, Navigation, Missions, and Combat

Artwork for everything below lives in `UI/Space UI`. All of it is decorative; text, buttons, and
highlights are real Unity UI or world objects.

| Feature | Where it lives | Artwork |
| --- | --- | --- |
| Ship, fleet, and structure markers | Built by `SpaceMapWorldController` | Ship Marker Icon, Fleet Marker Icon, Infrastructure Marker Icon |
| Navigation bar | `Navigation Bar` in `Space Map UI.prefab` | Route Confirmation Row |
| Map legend | `Legend Toggle Button` and `Map Legend Panel` | Space Map Legend |
| Combat forecast | `Combat Forecast` (`SpaceCombatPreviewPanel`) | Combat Preview Shell |
| Mission strip | `Space Mission Bar.prefab` (`SpaceMissionBarView`) | Mission Bar |
| Interplanetary travel | `Managers/Space Embark Canvas.prefab` (`SpaceEmbarkUI`) | Embarkation Panel Shell |

- **Markers** are world-space sprites facing the map camera, each with a collider the size of its
  visible artwork and the same `SpaceShipView`, `SpaceFleetView`, or `SpaceImprovementView` as
  before. A selected marker gets a ring; ships of a selected fleet and other civilizations' markers
  are tinted, never recoloured flat. A structure's own `SpaceImprovementData.icon` is drawn in the
  infrastructure marker's empty recess, with a small generic symbol when it has none. Markers are
  rebuilt when a ship or fleet moves, packs, deploys, or dies, and grow slightly when zoomed out.
- **Navigation bar**: choosing a destination previews the route and shows the bar with the route
  length, movement cost, and available movement from `SpaceShipMovementController`. Confirm and a
  second click on the destination both call `ConfirmPendingRoute`; Cancel only drops the preview.
- **Legend**: collapsed by default. Its swatches are coloured at runtime from the controller's own
  colours.
- **Combat forecast**: with one of your ships selected, clicking a hostile ship that
  `SpaceCombatManager.CanDirectAttack` accepts opens the forecast from `BuildPreview`. Confirm calls
  `ResolveDirectAttack` once; Cancel closes it. While it is open the map ignores clicks.
- **Missions**: `SpaceMissionManager` resolves missions on the tiles of the planet a spacecraft is
  positioned over, so targets are planetary tile indices, never space sectors. The strip therefore
  appears in two places. On the space map it sits under the spacecraft card and shows real
  availability; a ship in open space has no planetary tile, so its commands are disabled with the
  reason. On the planet view it sits above the unit panel for a spacecraft at that planet: choosing a
  mission starts `SpaceMissionTargetingMode`, a right-click on a tile is checked with
  `CanLaunchMission`, and `LaunchMission` runs only after the confirmation dialog.
- **Escape** backs out one step at a time: the combat forecast, then a pending route, then the map.
  The pause menu ignores Escape while the map or mission targeting is handling it.

## Events
The system provides events for integration:
- `OnPlanetSwitched` - Fired when switching planets
- `OnPlanetGenerated` - Fired when a new planet is created
- `OnSolarSystemInitialized` - Fired when system is ready

## Testing
Use `SpaceMapTester` component for development:
- F1: Generate test planet
- F2: Switch to next planet  
- F3: Show space map
- On-screen debug info

## Performance Notes
- Only current planet scene is active (others are unloaded but persistent)
- Planet generation happens once per planet
- Memory usage scales with number of visited planets
- Consider scene unloading for very large solar systems

## Integration Examples

### Add to Existing Main Menu
```csharp
public class MainMenuExtension : MonoBehaviour {
    void Start() {
        // Add space map button to existing menu
        GameObject spaceMapButton = Instantiate(spaceMapButtonPrefab, menuPanel);
        spaceMapButton.AddComponent<SpaceMapButton>();
    }
}
```

### Custom Travel Confirmation
```csharp
public class CustomSpaceMapUI : SpaceMapUI {
    protected override void ConfirmTravel(PlanetSceneData planet) {
        // Show custom confirmation dialog
        MyConfirmationDialog.Show($"Travel to {planet.planetName}?", 
            () => TravelToPlanet(planet));
    }
}
```

## Troubleshooting

### Space Map Not Showing
- Ensure `SolarSystemManager.Instance` exists
- Check that `SpaceMapUI` is properly initialized
- Verify button is calling `OpenSpaceMap()`

### Planets Not Generating
- Check `planetGeneratorPrefab` is assigned in SolarSystemManager
- Verify `GameSetupData` is properly configured
- Look for errors in planet generation coroutines

### Performance Issues
- Consider reducing `maxPlanets` in SolarSystemManager
- Implement scene unloading for distant planets
- Optimize planet generation settings

## Future Enhancements
- Save/load system for planet data
- Multiplayer synchronization
- Faction wars across planets
- Trade routes between worlds
- Stellar phenomenon and events
- Galaxy map with multiple solar systems
