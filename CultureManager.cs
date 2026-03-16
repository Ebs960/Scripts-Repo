// Assets/Scripts/Civs/CultureManager.cs
using System.Collections.Generic;
using UnityEngine;
using System;

public class CultureManager : MonoBehaviour
{
    public static CultureManager Instance { get; private set; }
    public List<CultureData> allCultures = new List<CultureData>();
    public event Action<Civilization, CultureData> OnCultureResearchCompleted;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    /// <summary>
    /// Which cultures can this civ adopt right now?
    /// </summary>
    public List<CultureData> GetAvailableCultures(Civilization civ)
    {
        var available = new List<CultureData>();

        foreach (var cult in allCultures)
        {
            if (cult == null) continue;
            if (civ.researchedCultures.Contains(cult)) 
                continue;

            // tech prerequisites
            bool meetsTechReqs = true;
            if (cult.requiredTechnologies != null)
            {
                foreach (var req in cult.requiredTechnologies)
                {
                    if (req != null && !civ.researchedTechs.Contains(req))
                    {
                        meetsTechReqs = false;
                        break;
                    }
                }
            }
            if (!meetsTechReqs) continue;

            // culture prerequisites
            bool meetsCultReqs = true;
            if (cult.requiredCultures != null)
            {
                foreach (var req in cult.requiredCultures)
                {
                    if (req != null && !civ.researchedCultures.Contains(req))
                    {
                        meetsCultReqs = false;
                        break;
                    }
                }
            }
            if (!meetsCultReqs) continue;

            // city count
            if (civ.cities.Count < cult.requiredCityCount) 
                continue;

            // biome control
            bool meetsBiomeReq = true;
            if (cult.requiredControlledBiomes != null)
            {
                foreach (var biome in cult.requiredControlledBiomes)
                {
                    // O(1) check via Civilization-owned biome aggregates (maintained by TileSystem.SetTileOwner).
                    if (!civ.HasControlledBiome(biome))
                    {
                        meetsBiomeReq = false;
                        break;
                    }
                }
            }
            if (!meetsBiomeReq) continue;

            // Age gating: civ must have reached the culture's age via tech research
            if (civ.GetCurrentAge() < cult.cultureAge) continue;

            available.Add(cult);
        }

        return available;
    }

    /// <summary>
    /// Initiates a culture adoption (spends culture points, unlocks bonuses).
    /// </summary>
    public void StartCulture(Civilization civ, CultureData cult)
    {
        if (civ == null || cult == null) return;
        if (!civ.CanCultivate(cult)) return;
        civ.StartCulture(cult);
    }

    /// <summary>
    /// Called when a culture is fully adopted by a civilization.
    /// </summary>
    public void CompleteCultureAdoption(Civilization civ, CultureData cult)
    {
        if (civ == null || cult == null) return;

        // Mirror tech completion timing: clear active progress before applying completion effects.
        civ.currentCulture = null;
        civ.currentCultureProgress = 0;

        // Inform the civilization (which will add to researchedCultures and apply bonuses)
        civ.OnCultureAdopted(cult);

        OnCultureResearchCompleted?.Invoke(civ, cult);
    }
}
