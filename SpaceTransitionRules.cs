using UnityEngine;

/// <summary>
/// Central transition gateway for spacecraft location changes.
/// Rules:
/// surface -> orbit: unit must be allowed to enter orbit; cargo remains attached to carrier.
/// orbit -> surface: destination planet tile must be valid and available; invalid destinations fail without side effects.
/// orbit -> solar-system grid: unit is placed on the chosen space hex and planetary occupancy is cleared.
/// solar-system grid -> orbit: unit leaves its space hex and enters the target planet's single orbit layer.
/// destruction during transition: callers should destroy the unit before invoking a transition; failed transitions never teleport.
/// occupied destinations: SpaceShipMovementController validates tile blocking/stacking before changing authoritative location.
/// carriers/fleets: fleet transitions must call the same methods for each member and roll back at caller level if any member fails.
/// </summary>
public static class SpaceTransitionRules
{
    public static bool EnterPlanetOrbit(BaseUnit unit, int planetIndex) => SpaceShipMovementController.Instance != null && SpaceShipMovementController.Instance.EnterPlanetOrbit(unit, planetIndex);
    public static bool LeavePlanetOrbitForSpace(BaseUnit unit, int spaceTileIndex) => SpaceShipMovementController.Instance != null && SpaceShipMovementController.Instance.LeavePlanetOrbitForSpace(unit, spaceTileIndex);
    public static bool EnterPlanetOrbitFromSpace(BaseUnit unit, int planetIndex) => SpaceShipMovementController.Instance != null && SpaceShipMovementController.Instance.EnterPlanetOrbitFromSpace(unit, planetIndex);
    public static bool LandOnPlanet(BaseUnit unit, int planetIndex, int planetaryTileIndex) => SpaceShipMovementController.Instance != null && SpaceShipMovementController.Instance.LandOnPlanet(unit, planetIndex, planetaryTileIndex);
    public static bool PlaceOnSpaceTile(BaseUnit unit, int spaceTileIndex) => SpaceShipMovementController.Instance != null && SpaceShipMovementController.Instance.PlaceOnSpaceTile(unit, spaceTileIndex);
}

public class SpaceMapSelectionController : MonoBehaviour
{
    [SerializeField] private SpaceMapWorldController worldController;
    public void SelectShip(BaseUnit unit) { if (worldController == null) worldController = FindAnyObjectByType<SpaceMapWorldController>(FindObjectsInactive.Include); worldController?.SelectShip(unit); }
    public void SelectPlanet(SpaceMapPlanetMarker marker) { if (worldController == null) worldController = FindAnyObjectByType<SpaceMapWorldController>(FindObjectsInactive.Include); worldController?.SelectPlanetMarker(marker); }
}
