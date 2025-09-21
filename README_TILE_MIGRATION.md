TileDataHelper has been superseded by TileSystem.

- Creation: GameManager ensures a `TileSystem` singleton exists early.
- Binding: After planet (and moon) generation, GameManager calls `TileSystem.InitializeFromPlanet(planet, moon)`.
- Switching planets: GameManager rebinds TileSystem on planet changes.
- Cleanup: GameManager calls `TileSystem.ClearAllCaches()` during memory cleanup.

If any script still references TileDataHelper, replace with the equivalent TileSystem APIs:
- UpdateReferences/RegisterPlanet/RegisterMoon -> InitializeFromPlanet(planet, moon)
- GetTileData/SetTileData -> TileSystem.GetTileData/SetTileData
- Neighbors -> TileSystem.GetNeighbors
- Positions -> TileSystem.GetTileCenter/GetTileSurfacePosition
- Distances/Range -> TileSystem.GetTileDistance/GetTilesWithinSteps
- Occupancy -> TileSystem.SetTileOccupant/ClearTileOccupant
- Religion -> TileSystem.AddReligionPressure/GetDominantReligion
