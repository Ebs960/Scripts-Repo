using UnityEngine;

[System.Serializable]
public class TradeRoute
{
    public City sourceCity;
    public City destinationCity;
    public int goldPerTurn;
    public int foodPerTurn;
    public int productionPerTurn;
    
    // Constants for trade route configuration
    private const int BASE_GOLD_PER_TURN = 2;
    private const float DISTANCE_GOLD_MULTIPLIER = 0.5f; // More gold for longer routes
    
    public TradeRoute(City source, City destination)
    {
        sourceCity = source;
        destinationCity = destination;
        
        // Calculate initial yields
        CalculateYields();
    }
    
    public void CalculateYields()
    {
        // Base yields
        goldPerTurn = BASE_GOLD_PER_TURN;
        foodPerTurn = 1;
        productionPerTurn = 1;
        
        if (sourceCity != null && destinationCity != null)
        {
            // Bonus yields based on buildings
            foreach (var (building, _) in destinationCity.builtBuildings)
            {
                if (building.isMarket || building.isBank)
                    goldPerTurn += 1;
                if (building.isMill || building.isFactory)
                    productionPerTurn += 1;
                if (building.isGranary || building.isFarm)
                    foodPerTurn += 1;
            }
        }
    }
    
    /// <summary>
    /// Calculate estimated trade route benefits between two cities
    /// </summary>
    public static (int goldPerTurn, int foodPerTurn, int productionPerTurn) CalculateTradeRouteBenefits(City source, City destination)
    {
        TradeRoute simulatedRoute = new TradeRoute(source, destination);
        return (simulatedRoute.goldPerTurn, simulatedRoute.foodPerTurn, simulatedRoute.productionPerTurn);
    }
}
