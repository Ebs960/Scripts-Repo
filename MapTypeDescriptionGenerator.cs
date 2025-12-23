using System.Collections.Generic;
using UnityEngine;

public static class MapTypeDescriptionGenerator
{
    // Climate description templates (shortened)
    private static readonly string[] climateDescriptions = {
        "Frozen landscapes of iron-hard ice, glittering drifts, and knife-edge winds.",
        "Cold regions with long winters, brittle forests, and short growing seasons that reward careful planning.",
        "Temperate realms of shifting seasons—fertile valleys, stormy coasts, and steady, livable heartlands.",
        "Warm environments with long summers, heavy skies, and rich growth where water can be secured.",
        "Hot climates where sun-baked earth and mirage heat punish the unprepared and elevate control of water.",
        "Scorching worlds of blistering heat waves and hostile horizons—survival itself is a victory condition."
    };

    // Moisture description templates (shortened)
    private static readonly string[] moistureDescriptions = {
        "Extremely dry conditions: dust, scrub, and wide-open skies with only stubborn life clinging on.",
        "Arid environments with scarce water sources—oases, seasonal rivers, and contested wells define expansion.",
        "Balanced precipitation supporting mixed biomes, reliable agriculture, and varied strategic options.",
        "Moist conditions with vigorous plant growth, deep soils, and rapid regrowth after conflict.",
        "Wet climates with frequent storms, swollen rivers, and plentiful biomass—movement is the main tax.",
        "Oceanic conditions with perpetual rainfall, fog banks, and coastlines that never truly dry."
    };

    // Land type description templates (shortened)
    private static readonly string[] landTypeDescriptions = {
        "A scattered world of countless islands, where sea lanes are lifelines and isolation shapes empires.",
        "Great island chains and broad seas—naval reach decides which shores become heartlands.",
        "Multiple major continents divided by oceans, creating distinct theaters of war and exploration.",
        "A dominant supercontinent sprawling across the globe, where borders are long and rivals are close."
    };

    // Elevation description templates (shortened)
    private static readonly string[] elevationDescriptions = {
        "Predominantly flat terrain—fast expansion, long sightlines, and few natural chokepoints.",
        "Rolling hills and elevated plateaus—defensible ridges, varied routes, and tactical high ground.",
        "Towering mountain ranges—hard borders, narrow passes, and dramatic strategic choke points."
    };

    // Continent-specific elevation descriptions (shortened)
    private static readonly string[] elevationDescriptionsContinents = {
        "Vast tablelands and broad plateaus stretch across the continent.",
        "Immense continental plateaus and escarpments create dramatic elevation changes.",
        "Colossal massif and continental divides form the backbone of the land."
    };

        // Special combination descriptions for climate + moisture (shortened)
        private static readonly string[,] climateMoistureDescriptions = {
        // Desert      Arid        Standard     Moist        Wet         Oceanic      Rainforest
        { "A frozen desert of ice and snow.", 
          "A frigid landscape with minimal snowfall.", 
          "A snow-covered realm with moderate storms.", 
          "A moist frozen environment with iced-over lakes.", 
          "Heavy snowfall and massive glaciers.", 
          "Extreme cold with constant precipitation.",
          "Constant precipitation meets freezing temperature." },  // Frozen
          
        { "A tundra environment with rare precipitation.", 
          "Cold steppes with sparse vegetation.", 
          "Boreal forests with seasonal snow cover.", 
          "Conifer forests in a cold, moist environment.", 
          "Dense pine forests despite the cold.", 
          "Foggy conditions create misty woodlands.",
          "A cold rainforest with hardy vegetation." },  // Cold
          
        { "Mediterranean conditions with dry summers.", 
          "Prairie grasslands dominate the landscape.", 
          "Temperate deciduous forests thrive.", 
          "Lush temperate rainforests with abundant ferns.", 
          "Fertile valleys and floodplains from heavy rainfall.", 
          "Constantly dripping temperate jungle conditions.",
          "A temperate paradise with constant rainfall." },  // Temperate
          
        { "Warm savannas with seasonal droughts.", 
          "Warm grasslands with scattered trees.", 
          "Subtropical conditions with moderate rainfall.", 
          "Lush subtropical forests teeming with life.", 
          "Tropical forests with abundant rainfall.", 
          "Warm mangrove swamps and flooded wetlands.",
          "Vast rainforests with constant rainfall." },  // Warm
          
        { "Scorching desert conditions with rare oases.", 
          "Arid scrubland with heat-adapted vegetation.", 
          "Hot seasonal forests adapted to periodic dry seasons.", 
          "Hot and humid jungle conditions.", 
          "Steamy rainforests with thick canopies.", 
          "Oppressively hot and humid conditions.",
          "Extreme heat and constant rainfall create dense tropical conditions." },  // Hot
          
        { "An almost uninhabitable furnace.", 
          "Punishing heat with bare rock and sand.", 
          "Scattered vegetation in scorching conditions.", 
          "Lush vegetation thrives despite intense heat.", 
          "Extreme heat and heavy rainfall create steamy jungles.", 
          "Constant rainfall creates a perpetual sauna.",
          "Extreme heat and maximum rainfall create a lush environment." }   // Scorching
    };

    // Get a description for a specific map type
    public static string GetDescription(int climate, int moisture, int landType, int elevation)
    {
        return GetDescription(climate, moisture, landType, elevation, 4, 2, 2, 3);
    }
    
    // New overloaded method with civilization counts
    public static string GetDescription(int climate, int moisture, int landType, int elevation, int aiCivCount, int cityStateCount, int tribeCount)
    {
        return GetDescription(climate, moisture, landType, elevation, aiCivCount, cityStateCount, tribeCount, 3);
    }
    
    // Main method with animalPrevalence
    public static string GetDescription(int climate, int moisture, int landType, int elevation, int aiCivCount, int cityStateCount, int tribeCount, int animalPrevalence)
    {
        // Ensure indices are within bounds
        climate = Mathf.Clamp(climate, 0, climateDescriptions.Length - 1);
        moisture = Mathf.Clamp(moisture, 0, moistureDescriptions.Length - 1);
        landType = Mathf.Clamp(landType, 0, landTypeDescriptions.Length - 1);
        elevation = Mathf.Clamp(elevation, 0, elevationDescriptions.Length - 1);

        // Use the specialized climate+moisture description if available
        string firstSentence = climateMoistureDescriptions[climate, moisture];
        
        // Calculate total civilizations for complexity analysis
        int totalCivs = aiCivCount + 1; // +1 for player
        int totalEntities = totalCivs + cityStateCount + tribeCount;
        
        // Generate geopolitical complexity description
        string geopoliticalDesc = GenerateGeopoliticalDescription(totalCivs, cityStateCount, tribeCount, climate, moisture, landType, elevation);

        // Identify special map types based on the same logic as MapTypeNameGenerator (shortened)
        if (climate == 5 && moisture == 5) // Demonic
        {
            return "A hellish world dominated by Hellscape and Brimstone biomes. Extreme heat and sulfurous atmosphere make this a hostile environment. " + 
                   geopoliticalDesc + 
                   " Eternal flames and toxic fumes scar the landscape.";
        }
        else if (climate == 3 && moisture == 5) // Monsoon
        {
            return "A monsoon world with seasonal flooding and Floodlands biomes. Powerful rains transform the landscape periodically. " +
                   geopoliticalDesc;
        }
        else if (climate == 3 && moisture == 4) // Rainforest
        {
            return "A rainforest world with dense vegetation and constant rainfall. Abundant resources but challenging movement. " +
                   geopoliticalDesc;
        }
        else if (climate == 5 && moisture == 0) // Scorched
        {
            return "A scorched wasteland with Ashlands and Charred Forest biomes. Intense heat and minimal moisture challenge survival. " +
                   geopoliticalDesc;
        }
        else if (climate == 5 && moisture == 4) // Infernal
        {
            return "An infernal landscape with Steam vents and Volcanic terrain. Extreme temperatures and high humidity create challenges. " +
                   geopoliticalDesc;
        }

        // Add special waterworld description for archipelago/islands with oceanic moisture
        if (landType <= 1 && moisture >= 4)
        {
            return firstSentence + " This world is dominated by water, with limited land. " +
                   geopoliticalDesc;
        }

        // Special description for extreme mountain desert combinations
        if (elevation == 2 && moisture <= 1)
        {
            return firstSentence + " Jagged mountain ranges with little vegetation. " +
                   geopoliticalDesc;
        }

        // Special description for high elevation with wet conditions
        if (elevation == 2 && moisture >= 4)
        {
            return firstSentence + " Constant precipitation creates waterfalls and raging rivers. " +
                   geopoliticalDesc;
        }

        // Default: add land type and elevation descriptions (shortened)
        string elevationDesc = (landType == 3) ? elevationDescriptionsContinents[elevation] : elevationDescriptions[elevation];
        string desc =
            firstSentence + " " +
            landTypeDescriptions[landType] + " " +
            elevationDesc + ". " +
            geopoliticalDesc;

        // Add a short “what matters here” kicker (kept deterministic from indices only).
        if (landType <= 1)
            desc += " Expect coastal strongholds, convoy routes, and wars decided by who controls the straits.";
        else if (landType == 4)
            desc += " Expect relentless border pressure—there are few places to hide from a determined neighbor.";
        else
            desc += " Expect distinct fronts and regional power blocs as civilizations consolidate their continents.";

        // If the map type name contains 'Rivers', append a note about double rivers
        string mapTypeName = MapTypeNameGenerator.GetMapTypeName(climate, moisture, landType, elevation);
        if (!string.IsNullOrEmpty(mapTypeName) && mapTypeName.IndexOf("Rivers", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            desc += " Double rivers spawn here.";
        }

        // Add animal prevalence flavor text (shortened)
        switch (animalPrevalence)
        {
            case 0:
                desc += " Wildlife is virtually extinct—nature offers little beyond silence and bones.";
                break;
            case 1:
                desc += " Animals are rare, glimpsed more often in stories than in the wild.";
                break;
            case 2:
                desc += " Wildlife survives in isolated pockets, wary and hard to track.";
                break;
            case 3:
                // Normal, no extra text
                break;
            case 4:
                desc += " The world teems with wildlife—hunters prosper and borders are never truly safe.";
                break;
            case 5:
                desc += " A world ruled by beasts—every expedition risks becoming prey.";
                break;
        }

        return desc;
    }
    
    // Generate complex geopolitical descriptions based on civilization counts (shortened)
    private static string GenerateGeopoliticalDescription(int civCount, int cityStateCount, int tribeCount, int climate, int moisture, int landType, int elevation)
    {
        System.Text.StringBuilder desc = new System.Text.StringBuilder();
        
        // Calculate geopolitical complexity metrics
        int totalEntities = civCount + cityStateCount + tribeCount;
        
        // Civilization dynamics (shortened)
        if (civCount <= 2)
        {
            desc.Append("Two great civilizations will clash for supremacy. ");
        }
        else if (civCount <= 4)
        {
            desc.Append("A balance of power between " + GetNumberWord(civCount) + " civilizations with shifting alliances. ");
        }
        else if (civCount <= 6)
        {
            desc.Append(GetNumberWord(civCount) + " civilizations create complex diplomacy. ");
        }
        else if (civCount <= 8)
        {
            desc.Append(GetNumberWord(civCount) + " civilizations compete for dominance. ");
        }
        else
        {
            desc.Append(civCount + " civilizations crowd this world, creating intense competition. ");
        }
        
        // City-state dynamics (shortened)
        if (cityStateCount == 0)
        {
            // No city-states, skip
        }
        else if (cityStateCount <= 3)
        {
            desc.Append("A few city-states control strategic locations. ");
        }
        else if (cityStateCount <= 6)
        {
            desc.Append("Numerous city-states form a mercantile network. ");
        }
        else
        {
            desc.Append("Many city-states fragment the political landscape. ");
        }
        
        // Tribal dynamics (shortened)
        if (tribeCount == 0)
        {
            // No tribes, skip
        }
        else if (tribeCount <= 2)
        {
            desc.Append("Isolated tribes persist in harsh regions. ");
        }
        else if (tribeCount <= 4)
        {
            desc.Append("Warlike tribes control wilderness areas. ");
        }
        else if (tribeCount <= 6)
        {
            desc.Append("Tribal confederations dominate untamed lands. ");
        }
        else
        {
            desc.Append("Many tribal groups roam the wilderness. ");
        }
        
        // Environmental impact on geopolitics (shortened)
        if (landType <= 1) // Archipelago/Islands
        {
            desc.Append("Fragmented geography isolates civilizations. Naval supremacy is key. ");
        }
        else if (landType == 4) // Pangaea
        {
            desc.Append("Unified landmass ensures intense competition with no geographic barriers. ");
        }
        
        // Elevation and civilization count interactions (shortened)
        if (elevation == 2) // Mountainous terrain
        {
            if (civCount >= 8)
            {
                desc.Append("Mountainous terrain channels civilizations into competition for limited passes. ");
            }
            else if (civCount <= 3)
            {
                desc.Append("Mountains provide natural borders between civilizations. ");
            }
        }
        else if (elevation == 0) // Flat terrain
        {
            if (civCount >= 7)
            {
                desc.Append("Vast plains see massive open-field battles. ");
            }
            else
            {
                desc.Append("Open plains allow rapid expansion but leave vulnerabilities. ");
            }
        }
        else if (elevation == 1) // Hills
        {
            desc.Append("Rolling hills create dynamic battlefields. ");
        }
        
        // Climate impact on conflict (shortened)
        if (climate <= 1) // Frozen/Cold
        {
            desc.Append("Harsh climate limits population growth. ");
        }
        else if (climate >= 4) // Hot/Scorching
        {
            desc.Append("Oppressive heat clusters civilizations around water sources. ");
        }
        
        return desc.ToString();
    }
    
    // Helper method to convert numbers to words for better readability
    private static string GetNumberWord(int number)
    {
        switch (number)
        {
            case 2: return "two";
            case 3: return "three";
            case 4: return "four";
            case 5: return "five";
            case 6: return "six";
            case 7: return "seven";
            case 8: return "eight";
            case 9: return "nine";
            case 10: return "ten";
            default: return number.ToString();
        }
    }
    
    // Estimate land tiles based on land type (rough approximation)
    private static int GetEstimatedLandTiles(int landType)
    {
        switch (landType)
        {
            case 0: return 200;  // Archipelago - very little land
            case 1: return 400;  // Islands - some land
            case 2: return 600;  // Standard - balanced
            case 3: return 700;  // Continents - more land
            case 4: return 800;  // Pangaea - mostly land
            default: return 600;
        }
    }
} 