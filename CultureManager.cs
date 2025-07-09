// Assets/Scripts/Civs/CultureManager.cs
using System.Collections.Generic;
using UnityEngine;

public class CultureManager : MonoBehaviour
{
    public static CultureManager Instance { get; private set; }
    public List<CultureData> allCultures = new List<CultureData>();

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
        var planet = FindAnyObjectByType<PlanetGenerator>();
        
        if (planet == null)
        {
            Debug.LogError("PlanetGenerator not found");
            return available;
        }

        foreach (var cult in allCultures)
        {
            if (civ.researchedCultures.Contains(cult)) 
                continue;

            // tech prereqs, if any
            bool meetsCultReqs = true;
            foreach (var req in cult.requiredCultures)
            {
                if (!civ.researchedCultures.Contains(req))
                {
                    meetsCultReqs = false;
                    break;
                }
            }
            if (!meetsCultReqs) continue;

            // city count
            if (civ.cities.Count < cult.requiredCityCount) 
                continue;

            // biome control
            bool meetsBiomeReq = true;
            foreach (var biome in cult.requiredControlledBiomes)
            {
                int count = civ.ownedTileIndices.FindAll(idx => {
                    var td = planet.GetHexTileData(idx);
                    return td != null && td.biome == biome;
                }).Count;
                if (count == 0)
                {
                    meetsBiomeReq = false;
                    break;
                }
            }
            if (!meetsBiomeReq) continue;

            available.Add(cult);
        }

        return available;
    }

    /// <summary>
    /// Initiates a culture adoption (spends culture points, unlocks bonuses).
    /// </summary>
    public void StartCulture(Civilization civ, CultureData cult)
    {
        if (!civ.CanCultivate(cult)) return;
        civ.StartCulture(cult);
        Debug.Log($"{civ.civData.civName} adopted culture {cult.cultureName}");
    }

    /// <summary>
    /// Called when a culture is fully adopted by a civilization.
    /// </summary>
    public void CompleteCultureAdoption(Civilization civ, CultureData cult)
    {
        if (civ == null || cult == null) return;

        Debug.Log($"{civ.civData.civName} completed adoption of culture: {cult.cultureName}");

        // Inform the civilization (which will add to researchedCultures and apply bonuses)
        civ.OnCultureAdopted(cult);

        // Reset current culture research in the civilization
        civ.currentCulture = null;
        civ.currentCultureProgress = 0;

        // TODO: Trigger UI updates or other game events as needed
        // For example, an event like:
        // OnCultureFullyAdopted?.Invoke(civ, cult);
    }
}
