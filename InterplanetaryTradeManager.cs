using System.Collections.Generic;
using UnityEngine;

public class InterplanetaryTradeManager : MonoBehaviour
{
    public static InterplanetaryTradeManager Instance { get; private set; }

    private List<InterplanetaryTradeRoute> activeRoutes = new List<InterplanetaryTradeRoute>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void CreateTradeRoute(Civilization owner, int originPlanetIndex, int destinationPlanetIndex, ResourceData resource)
    {
        InterplanetaryTradeRoute newRoute = new InterplanetaryTradeRoute(owner, originPlanetIndex, destinationPlanetIndex, resource);
        activeRoutes.Add(newRoute);
        Debug.Log($"Created new interplanetary trade route from planet {originPlanetIndex} to {destinationPlanetIndex}.");
    }

    public List<InterplanetaryTradeRoute> GetRoutesFor(Civilization civ)
    {
        List<InterplanetaryTradeRoute> civRoutes = new List<InterplanetaryTradeRoute>();
        foreach (var route in activeRoutes)
        {
            if (route.owner == civ)
            {
                civRoutes.Add(route);
            }
        }
        return civRoutes;
    }

    public void ProcessTurn()
    {
        foreach (var route in activeRoutes)
        {
            route.owner.AddGold(route.goldPerTurn);
        }
    }
}
