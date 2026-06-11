// Assets/Scripts/Civs/TechManager.cs
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;

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

        EnsureTechListLoaded();
    }

    private void EnsureTechListLoaded()
    {
        if (allTechs != null && allTechs.Any(t => t != null)) return;

        var loadedTechs = ResourceCache.GetAllTechData();
        allTechs = loadedTechs != null
            ? new List<TechData>(loadedTechs.Where(t => t != null))
            : new List<TechData>();

        if (allTechs.Count == 0)
        {
            Debug.LogWarning("[TechManager] No TechData assets were loaded into allTechs.");
        }
    }

    /// <summary>
    /// Returns the list of techs this civ can start researching now.
    /// </summary>
    public List<TechData> GetAvailableTechs(Civilization civ)
    {
        EnsureTechListLoaded();
        var available = new List<TechData>();

        if (civ == null) return available;

        // Defensive nulls for civ collections
        var civResearched = civ.researchedTechs ?? new List<TechData>();
        int civCityCount = civ.cities != null ? civ.cities.Count : 0;

        foreach (var tech in allTechs)
        {
            if (tech == null) continue;

            // skip already researched
            if (civResearched.Contains(tech)) continue;

            // check tech prerequisites (safe if null)
            bool meetsTechReqs = true;
            if (tech.requiredTechnologies != null)
            {
                foreach (var req in tech.requiredTechnologies)
                {
                    if (req != null && !civResearched.Contains(req))
                    {
                        meetsTechReqs = false;
                        break;
                    }
                }
            }
            if (!meetsTechReqs) continue;

            // check city count
            if (civCityCount < tech.requiredCityCount) continue;

            // check controlled biomes (safe if null)
            bool meetsBiomeReq = true;
            if (tech.requiredControlledBiomes != null)
            {
                foreach (var biome in tech.requiredControlledBiomes)
                {
                    if (!civ.HasControlledBiome(biome))
                    {
                        meetsBiomeReq = false;
                        break;
                    }
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
        EnsureTechListLoaded();
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
        // Ensure the civ does not get progress the same turn (minimum 1 turn research)
        civ.MarkResearchStartedThisTurn();
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

        if (tech.IsVictoryTech)
        {
            string civName = civ.civData != null ? civ.civData.civName : "A civilization";
            string techName = !string.IsNullOrWhiteSpace(tech.techName) ? tech.techName : tech.name;
            GameManager.Instance?.EndGame(civ, tech, $"{civName} wins by researching {techName}!");
        }
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
