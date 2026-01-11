// Assets/Scripts/Civs/TechManager.cs
using System.Collections.Generic;
using UnityEngine;
using System;

public class TechManager : MonoBehaviour
{
    public static TechManager Instance { get; private set; }

    [Tooltip("All TechData assets in the game")]
    public List<TechData> allTechs = new List<TechData>();
    
    // Event raised when a tech is fully researched
    public event Action<Civilization, TechData> OnTechResearchCompleted;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// Returns the list of techs this civ can start researching now.
    /// </summary>
    public List<TechData> GetAvailableTechs(Civilization civ)
    {
        var available = new List<TechData>();
        foreach (var tech in allTechs)
        {
            // skip already researched
            if (civ.researchedTechs.Contains(tech)) continue;

            // check tech prerequisites
            bool meetsTechReqs = true;
            foreach (var req in tech.requiredTechnologies)
            {
                if (!civ.researchedTechs.Contains(req))
                {
                    meetsTechReqs = false;
                    break;
                }
            }
            if (!meetsTechReqs) continue;

            // check city count
            if (civ.cities.Count < tech.requiredCityCount) continue;

            // check controlled biomes
            bool meetsBiomeReq = true;
            foreach (var biome in tech.requiredControlledBiomes)
            {
                // Use FindAnyObjectByType instead of Instance
                // Use GameManager API for multi-planet support
        PlanetGenerator planetGen = GameManager.Instance?.GetCurrentPlanetGenerator();
                int count = civ.ownedTileIndices
                    .FindAll(idx => {
                        var td = planetGen.GetHexTileData(idx);
                        return td != null && td.biome == biome;
                    })
                    .Count;
                if (count == 0)
                {
                    meetsBiomeReq = false;
                    break;
                }
            }
            if (!meetsBiomeReq) continue;

            available.Add(tech);
        }
        return available;
    }

    /// <summary>
    /// When the user clicks a tech in the UI to research it
    /// </summary>
    public void StartResearch(Civilization civ, TechData tech)
    {
        if (civ == null || tech == null) return;
        
        // First check if it's already researched
        if (civ.researchedTechs.Contains(tech))
        {
return;
        }
        
        // Check if prereqs are met
        foreach (TechData prereq in tech.requiredTechnologies)
        {
            if (prereq != null && !civ.researchedTechs.Contains(prereq))
            {
return;
            }
        }
        
        // Start researching
        civ.currentTech = tech;
        civ.currentTechProgress = 0;
// TODO: play sound, show feedback, etc.
    }
    
    /// <summary>
    /// Called when a tech is fully researched
    /// </summary>
    public void CompleteResearch(Civilization civ, TechData tech)
    {
        if (civ == null || tech == null) return;
// Inform the civilization
        civ.HandleTechResearched(tech);
        
        // Reset current research
        civ.currentTech = null;
        civ.currentTechProgress = 0;
        
        // Trigger UI updates
        if (OnTechResearchCompleted != null)
            OnTechResearchCompleted(civ, tech);
    }
    
    /// <summary>
    /// Check if a unit can be produced based on tech requirements
    /// </summary>
    public bool CanProduceUnit(Civilization civ, CombatUnitData unitData)
    {
        return unitData.AreRequirementsMet(civ);
    }
    
    /// <summary>
    /// Check if a worker unit can be produced based on tech requirements
    /// </summary>
    public bool CanProduceWorkerUnit(Civilization civ, WorkerUnitData unitData)
    {
        return unitData.AreRequirementsMet(civ);
    }
}
