using UnityEngine;

[System.Serializable]
public class TradeRoute
{
    [Header("City Trade")]
    public City sourceCity;
    public City destinationCity;
    
    [Header("Interplanetary Trade")]
    public bool isInterplanetaryRoute = false;
    public int originPlanetIndex = -1;
    public int destinationPlanetIndex = -1;
    public Civilization tradingCivilization; // For planet-to-planet trade
    
    [Header("Yields")]
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
        isInterplanetaryRoute = false;
        
        // Calculate initial yields
        CalculateYields();
    }
    
    // New constructor for interplanetary trade
    public TradeRoute(Civilization civ, int originPlanet, int destPlanet)
    {
        tradingCivilization = civ;
        originPlanetIndex = originPlanet;
        destinationPlanetIndex = destPlanet;
        isInterplanetaryRoute = true;
        
        // Calculate initial yields for interplanetary trade
        CalculateYields();
    }
    
    public void CalculateYields()
    {
        // Base yields
        goldPerTurn = BASE_GOLD_PER_TURN;
        foodPerTurn = 1;
        productionPerTurn = 1;
        
        if (isInterplanetaryRoute)
        {
            // Interplanetary trade calculation - much more profitable but longer distance
            int planetDistance = Mathf.Abs(destinationPlanetIndex - originPlanetIndex);
            
            // Base interplanetary profit is higher than city trade
            int baseInterplanetaryGold = 8; // Higher base than cities
            
            // Distance bonus: farther planets = much more profit (like luxury goods)
            int distanceBonus = Mathf.RoundToInt(planetDistance * DISTANCE_GOLD_MULTIPLIER * 3);
            
            goldPerTurn = baseInterplanetaryGold + distanceBonus;
            
            // No food/production for interplanetary - just pure gold profit
            foodPerTurn = 0; 
            productionPerTurn = 0;
            
            Debug.Log($"[TradeRoute] Interplanetary route: Planet {originPlanetIndex} → {destinationPlanetIndex} (distance: {planetDistance}) = {goldPerTurn} gold/turn");
        }
        else if (sourceCity != null && destinationCity != null)
        {
            // Original city trade calculation
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
    
    /// <summary>
    /// Calculate estimated interplanetary trade route benefits
    /// </summary>
    public static (int goldPerTurn, int foodPerTurn, int productionPerTurn) CalculateInterplanetaryTradeBenefits(Civilization civ, int originPlanet, int destPlanet)
    {
        TradeRoute simulatedRoute = new TradeRoute(civ, originPlanet, destPlanet);
        return (simulatedRoute.goldPerTurn, simulatedRoute.foodPerTurn, simulatedRoute.productionPerTurn);
    }
}
