using UnityEngine;

[System.Serializable]
public class InterplanetaryTradeRoute
{
    public Civilization owner;
    public int originPlanetIndex;
    public int destinationPlanetIndex;
    public ResourceData tradedResource;
    public int goldPerTurn;

    public InterplanetaryTradeRoute(Civilization owner, int originPlanetIndex, int destinationPlanetIndex, ResourceData tradedResource)
    {
        this.owner = owner;
        this.originPlanetIndex = originPlanetIndex;
        this.destinationPlanetIndex = destinationPlanetIndex;
        this.tradedResource = tradedResource;
        CalculateYields();
    }

    private void CalculateYields()
    {
        // Placeholder yield calculation.
        // This could be expanded to factor in distance, planet types, etc.
        goldPerTurn = 10; 
    }
}
