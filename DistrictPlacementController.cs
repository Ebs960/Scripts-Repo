using System.Collections.Generic;
using UnityEngine;


public class DistrictPlacementController : MonoBehaviour
{
    public static DistrictPlacementController Instance { get; private set; }
    
    [Header("References")]
    [SerializeField] private Material validTileMaterial;
    [SerializeField] private GameObject highlightPrefab;
    
    // State tracking
    private bool isPlacingDistrict = false;
    private City sourceCity;
    private DistrictData districtData;
    private List<int> validTileIndices = new List<int>();
    private Dictionary<int, GameObject> tileHighlights = new();
    private int currentHoveredTileIndex = -1;

    // Components references
    private SphericalHexGrid grid;
    private PlanetGenerator planet;
    
    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        
        planet = FindAnyObjectByType<PlanetGenerator>();
        grid = planet != null ? planet.Grid : null;
    }
    
    void Update()
    {
        if (!isPlacingDistrict) return;
        
        // Handle hovering over tiles
        int hoverTileIndex = GetHoveredTileIndex();
        if (hoverTileIndex != currentHoveredTileIndex)
        {
            // Reset previous hover effect
            if (currentHoveredTileIndex >= 0 && validTileIndices.Contains(currentHoveredTileIndex))
            {
                ResetTileHighlight(currentHoveredTileIndex);
            }
            
            // Apply new hover effect if it's a valid tile
            currentHoveredTileIndex = hoverTileIndex;
            if (currentHoveredTileIndex >= 0 && validTileIndices.Contains(currentHoveredTileIndex))
            {
                HighlightHoveredTile(currentHoveredTileIndex);
            }
        }
        
        // Handle clicking on a valid tile
        if (Input.GetMouseButtonDown(0) && currentHoveredTileIndex >= 0 && validTileIndices.Contains(currentHoveredTileIndex))
        {
            PlaceDistrictOnTile(currentHoveredTileIndex);
        }
        
        // Cancel district placement with right-click
        if (Input.GetMouseButtonDown(1))
        {
            CancelDistrictPlacement();
        }
    }
    
    /// <summary>
    /// Begin district placement mode for a given city and district type
    /// </summary>
    public void BeginDistrictPlacement(City city, DistrictData district)
    {
        sourceCity = city;
        districtData = district;
        isPlacingDistrict = true;
        
        // Determine valid tiles for this district
        FindValidTiles();
        
        // Highlight all valid tiles
        HighlightValidTiles();
        
        // Notify player
        if (UIManager.Instance != null)
            UIManager.Instance.ShowNotification("Select a tile to place your " + district.districtName);
    }
    
    /// <summary>
    /// Find all valid tiles where this district can be placed
    /// </summary>
    private void FindValidTiles()
    {
        validTileIndices.Clear();
        
        // Get tiles within city territory radius
        int centerTileIndex = sourceCity.centerTileIndex;
        int radius = sourceCity.TerritoryRadius;
        
        var tilesInRange = TileDataHelper.Instance.GetTilesWithinSteps(centerTileIndex, radius);
        if (tilesInRange == null) return;
        
        foreach (int tileIndex in tilesInRange)
        {
            if (IsValidTileForDistrict(tileIndex, districtData))
            {
                validTileIndices.Add(tileIndex);
            }
        }
    }
    
    /// <summary>
    /// Check if a tile is valid for this district type
    /// </summary>
    private bool IsValidTileForDistrict(int tileIndex, DistrictData district)
    {
        // Check if tile is owned by the city's civilization
        var (tileData, _) = TileDataHelper.Instance.GetTileData(tileIndex);
        if (tileData == null) return false;
        
        // Check if tile is already occupied by a district, unit, or improvement
        if (tileData.district != null || tileData.occupantId != 0 || tileData.improvement != null)
            return false;
        
        // Check biome requirements
        bool biomeValid = false;
        if (district.allowedBiomes == null || district.allowedBiomes.Length == 0)
        {
            biomeValid = true;
        }
        else
        {
            foreach (var allowedBiome in district.allowedBiomes)
            {
                if ((int)tileData.biome == (int)allowedBiome)
                {
                    biomeValid = true;
                    break;
                }
            }
        }
        
        if (!biomeValid) return false;
        
        // Check special requirements
        if (district.requiresRiver)
        {
            bool adjacentToRiver = false;
            foreach (int neighborIndex in TileDataHelper.Instance.GetTileNeighbors(tileIndex))
            {
                var (neighborData, _) = TileDataHelper.Instance.GetTileData(neighborIndex);
                if (neighborData != null && neighborData.biome == Biome.River)
                {
                    adjacentToRiver = true;
                    break;
                }
            }
            if (!adjacentToRiver) return false;
        }
        
        if (district.requiresCoastal)
        {
            bool adjacentToWater = false;
            foreach (int neighborIndex in TileDataHelper.Instance.GetTileNeighbors(tileIndex))
            {
                var (neighborData, _) = TileDataHelper.Instance.GetTileData(neighborIndex);
                if (neighborData != null && 
                    (neighborData.biome == Biome.Ocean || 
                     neighborData.biome == Biome.Seas || 
                     neighborData.biome == Biome.Coast))
                {
                    adjacentToWater = true;
                    break;
                }
            }
            if (!adjacentToWater) return false;
        }
        
        if (district.requiresMountainAdjacent)
        {
            bool adjacentToMountain = false;
            foreach (int neighborIndex in TileDataHelper.Instance.GetTileNeighbors(tileIndex))
            {
                var (neighborData, _) = TileDataHelper.Instance.GetTileData(neighborIndex);
                if (neighborData != null && neighborData.biome == Biome.Mountain)
                {
                    adjacentToMountain = true;
                    break;
                }
            }
            if (!adjacentToMountain) return false;
        }
        
        return true;
    }
    
    /// <summary>
    /// Get the tile index under the mouse cursor
    /// </summary>
    private int GetHoveredTileIndex()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit))
        {
            if (grid == null) return -1;
            Vector3 localDir = (hit.point - planet.transform.position).normalized;
            return grid.GetTileAtPosition(localDir);
        }
        return -1;
    }
    
    /// <summary>
    /// Highlight all valid tiles where the district can be placed
    /// </summary>
    private void HighlightValidTiles()
    {
        ClearAllHighlights();
        Color color = validTileMaterial != null ? validTileMaterial.color : new Color(0, 1, 0, 0.3f);
        foreach (int tileIndex in validTileIndices)
        {
            HighlightTile(tileIndex, color);
        }
    }
    
    /// <summary>
    /// Special highlight effect for the currently hovered tile
    /// </summary>
    private void HighlightHoveredTile(int tileIndex)
    {
        if (tileHighlights.TryGetValue(tileIndex, out var obj))
        {
            var mr = obj.GetComponent<MeshRenderer>();
            if (mr != null) mr.material.color = Color.yellow;
        }
    }
    
    /// <summary>
    /// Reset the highlight effect on a previously hovered tile
    /// </summary>
    private void ResetTileHighlight(int tileIndex)
    {
        if (tileHighlights.TryGetValue(tileIndex, out var obj))
        {
            var mr = obj.GetComponent<MeshRenderer>();
            if (mr != null)
                mr.material.color = validTileMaterial != null ? validTileMaterial.color : new Color(0,1,0,0.3f);
        }
    }

    private void HighlightTile(int tileIndex, Color color)
    {
        if (grid == null || planet == null) return;

        if (!tileHighlights.ContainsKey(tileIndex))
        {
            GameObject highlightObj = highlightPrefab != null ?
                Instantiate(highlightPrefab) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            highlightObj.name = $"TileHighlight_{tileIndex}";

            Vector3 worldPos = planet.transform.TransformPoint(grid.tileCenters[tileIndex]);
            highlightObj.transform.position = worldPos + Vector3.up * 0.05f;
            float tileSize = 0.2f;
            highlightObj.transform.localScale = new Vector3(tileSize, tileSize, tileSize);

            var mat = new Material(Shader.Find("Standard")) { color = color };
            var rend = highlightObj.GetComponent<MeshRenderer>();
            rend.material = mat;

            tileHighlights[tileIndex] = highlightObj;
        }
        else
        {
            var rend = tileHighlights[tileIndex].GetComponent<MeshRenderer>();
            rend.material.color = color;
        }
    }

    private void ClearAllHighlights()
    {
        foreach (var obj in tileHighlights.Values)
            Destroy(obj);
        tileHighlights.Clear();
    }
    
    /// <summary>
    /// Place the district on the selected tile
    /// </summary>
    private void PlaceDistrictOnTile(int tileIndex)
    {
        // Add district to production queue with the selected tile
        sourceCity.AddDistrictToQueue(districtData, tileIndex);
        
        // End placement mode
        EndDistrictPlacement();
        
        // Notify player
        if (UIManager.Instance != null)
            UIManager.Instance.ShowNotification(districtData.districtName + " added to production queue");
    }
    
    /// <summary>
    /// Cancel district placement mode
    /// </summary>
    public void CancelDistrictPlacement()
    {
        // Notify player
        if (UIManager.Instance != null)
            UIManager.Instance.ShowNotification("District placement canceled");
        
        // End placement mode
        EndDistrictPlacement();
    }
    
    /// <summary>
    /// End district placement mode and clean up
    /// </summary>
    private void EndDistrictPlacement()
    {
        ClearAllHighlights();

        // Reset state
        isPlacingDistrict = false;
        sourceCity = null;
        districtData = null;
        validTileIndices.Clear();
        currentHoveredTileIndex = -1;
    }
}