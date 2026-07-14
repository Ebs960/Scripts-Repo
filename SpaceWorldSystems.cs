using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class SpaceWorldState
{
    public int starSystemId;
    public SpaceHexGrid grid;
    public List<SpaceFeatureInstance> features = new List<SpaceFeatureInstance>();
    public List<SpaceImprovementInstance> improvements = new List<SpaceImprovementInstance>();
    public List<SpaceFleet> fleets = new List<SpaceFleet>();
    public List<SpaceConstructionOrder> constructionOrders = new List<SpaceConstructionOrder>();
    public int nextFeatureId = 1;
    public int nextImprovementId = 1;
    public int nextConstructionOrderId = 1;
}

public class SpaceWorldManager : MonoBehaviour
{
    public static SpaceWorldManager Instance { get; private set; }
    public SpaceWorldState CurrentSystem { get; private set; }
    public SpaceHexGrid Grid => CurrentSystem != null ? CurrentSystem.grid : null;
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (CurrentSystem == null) CreateSystem(0, 12, 5f);
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }
    public void CreateSystem(int starSystemId, int radius = 12, float tileSize = 5f)
    {
        CurrentSystem = new SpaceWorldState { starSystemId = starSystemId, grid = new SpaceHexGrid(radius, tileSize) };
    }
    public void LoadSystem(SpaceWorldState state) { CurrentSystem = state ?? new SpaceWorldState { grid = new SpaceHexGrid() }; }
    public SpaceWorldState SaveSystem() => CurrentSystem;
}

public class SpaceShipView : MonoBehaviour { public int entityId; }
public class SpaceFeatureView : MonoBehaviour { public int entityId; }
public class SpaceImprovementView : MonoBehaviour { public int entityId; }
public class SpacePlanetView : MonoBehaviour { public int entityId; }
