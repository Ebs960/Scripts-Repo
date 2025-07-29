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
