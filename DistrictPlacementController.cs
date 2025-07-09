using System.Collections.Generic;
using UnityEngine;
using HexasphereGrid;

public class DistrictPlacementController : MonoBehaviour
{
    public static DistrictPlacementController Instance { get; private set; }
    
    [Header("References")]
    [SerializeField] private Material validTileMaterial;
    [SerializeField] private Material invalidTileMaterial;
    [SerializeField] private float hoverEffectHeight = 0.02f;
    
    // State tracking
    private bool isPlacingDistrict = false;
    private City sourceCity;
    private DistrictData districtData;
    private List<int> validTileIndices = new List<int>();
    private Dictionary<int, Material> originalTileMaterials = new Dictionary<int, Material>();
    private int currentHoveredTileIndex = -1;
    
    // Components references
    private Hexasphere hex;
    private PlanetGenerator planet;
    
    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        
        hex = FindAnyObjectByType<Hexasphere>();
        planet = FindAnyObjectByType<PlanetGenerator>();
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
        {
            UIManager.Instance.ShowNotification("Select a tile to place your " + district.districtName);
        }
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
        
        var tilesInRange = hex.GetTilesWithinSteps(centerTileIndex, radius);
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
            return hex.GetTileAtPosition(hit.point);
        }
        return -1;
    }
    
    /// <summary>
    /// Highlight all valid tiles where the district can be placed
    /// </summary>
    private void HighlightValidTiles()
    {
        originalTileMaterials.Clear();
        
        // Save original materials and highlight valid tiles
        foreach (int tileIndex in validTileIndices)
        {
            // Find tile GameObject through the hex renderer
            var renderer = hex.GetComponent<Renderer>();
            if (renderer != null)
            {
                originalTileMaterials[tileIndex] = hex.GetComponent<Renderer>().sharedMaterial;
                hex.SetTileMaterial(tileIndex, validTileMaterial, true);
            }
        }
    }
    
    /// <summary>
    /// Special highlight effect for the currently hovered tile
    /// </summary>
    private void HighlightHoveredTile(int tileIndex)
    {
        // Apply an extrusion effect to show hovering
        float originalExtrusion = hex.GetTileExtrudeAmount(tileIndex);
        hex.SetTileExtrudeAmount(tileIndex, originalExtrusion + hoverEffectHeight);
    }
    
    /// <summary>
    /// Reset the highlight effect on a previously hovered tile
    /// </summary>
    private void ResetTileHighlight(int tileIndex)
    {
        // Reset extrusion to original value
        float originalExtrusion = planet.GetTileElevation(tileIndex) * planet.maxExtrusionHeight;
        hex.SetTileExtrudeAmount(tileIndex, originalExtrusion);
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
        {
            UIManager.Instance.ShowNotification(districtData.districtName + " added to production queue");
        }
    }
    
    /// <summary>
    /// Cancel district placement mode
    /// </summary>
    public void CancelDistrictPlacement()
    {
        // Notify player
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowNotification("District placement canceled");
        }
        
        // End placement mode
        EndDistrictPlacement();
    }
    
    /// <summary>
    /// End district placement mode and clean up
    /// </summary>
    private void EndDistrictPlacement()
    {
        // Reset all tile highlights
        foreach (int tileIndex in validTileIndices)
        {
            ResetTileHighlight(tileIndex);
            
            // Restore original material
            if (originalTileMaterials.ContainsKey(tileIndex))
            {
                hex.SetTileMaterial(tileIndex, originalTileMaterials[tileIndex], true);
            }
        }
        
        // Reset state
        isPlacingDistrict = false;
        sourceCity = null;
        districtData = null;
        validTileIndices.Clear();
        originalTileMaterials.Clear();
        currentHoveredTileIndex = -1;
    }
} 