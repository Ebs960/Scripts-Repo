using System.Collections.Generic;
using UnityEngine;

public static class MapTypeDescriptionGenerator
{
    // Climate description templates
    private static readonly string[] climateDescriptions = {
        "This frozen landscape is dominated by harsh, icy conditions throughout most of the year.",
        "This cold region experiences long winters and short growing seasons. Hardy evergreen vegetation and cold-adapted wildlife are common here.",
        "This temperate region has distinct seasons with moderate temperature variations. Deciduous forests and diverse ecosystems thrive in these balanced conditions.",
        "This warm environment enjoys extended growing seasons and mild winters. Grasslands and mixed forests flourish in these favorable conditions.",
        "This hot climate features scorching temperatures and intense sunlight year-round.",
        "This extremely hot landscape experiences blistering heat waves and minimal temperature variations."
    };

    // Moisture description templates
    private static readonly string[] moistureDescriptions = {
        "Extremely dry conditions limit vegetation to hardy, drought-resistant species. Water management is critical for any settlement.",
        "The arid environment supports limited plant life, primarily consisting of scrub and tough grasses. Water sources are precious and rare.",
        "Balanced precipitation allows for moderate vegetation growth. Water is generally available but not overly abundant.",
        "The moist conditions support lush plant growth and diverse ecosystems. Rivers and lakes dot the landscape, providing ample water.",
        "The wet climate creates verdant landscapes with abundant rainfall. Dense vegetation and numerous water bodies characterize this environment.",
        "Perpetual rainfall and high humidity make this environment incredibly soggy. Water is everywhere, creating extensive wetlands and waterlogged soils.",
        "Extreme rainfall and constant humidity. The land is perpetually drenched, supporting the most dense and diverse ecosystems imaginable."
    };

    // Land type description templates
    private static readonly string[] landTypeDescriptions = {
        "Countless small islands scatter across vast ocean expanses, creating isolated ecological pockets. Naval travel is essential for any meaningful exploration.",
        "Significant island chains rise from the waters, offering more substantial landmasses. Ocean currents and winds greatly influence local climates.",
        "Several major continents dominate the planet, separated by significant bodies of water. Diverse geological formations create varied terrain across these landmasses.",
        "A massive supercontinent encompasses most of the planet's land area. Inland regions can be extremely remote from oceanic influence."
    };

    // Elevation description templates
    private static readonly string[] elevationDescriptions = {
        "The predominantly flat terrain allows for easy travel and settlement.",
        "Rolling hills and elevated plateaus create natural boundaries between regions.",
        "Towering mountain ranges dramatically divide the landscape."
    };

    // Continent-specific elevation descriptions
    private static readonly string[] elevationDescriptionsContinents = {
        "Vast tablelands and broad plateaus stretch across the continent, offering sweeping vistas and fertile ground for expansion.",
        "Immense continental plateaus and escarpments create dramatic elevation changes, shaping the flow of rivers and civilizations alike.",
        "Colossal massif and continental divides form the backbone of the land, their sheer scale dwarfing even the greatest mountain ranges elsewhere."
    };

    // Special combination descriptions for climate + moisture
    private static readonly string[,] climateMoistureDescriptions = {
        // Desert      Arid        Standard     Moist        Wet         Oceanic      Rainforest
        { "A frozen desert of ice and snow.", 
          "A frigid landscape with minimal snowfall and fierce winter winds.", 
          "A snow-covered realm with moderate winter storms and brief summer thaws.", 
          "A surprisingly moist frozen environment where iced-over lakes and rivers are common.", 
          "Heavy snowfall and perpetual winter, with massive glaciers forming year-round.", 
          "Extreme cold mixed with constant precipitation creates spectacular ice formations.",
          "A unique environment where constant precipitation meets freezing temperature." },  // Frozen
          
        { "A tundra-like environment where precipitation is rare and the ground remains frozen much of the year.", 
          "Cold steppes stretch across the landscape with sparse vegetation adapted to both cold and dry conditions.", 
          "Boreal forests dominate the landscape with seasonal snow cover and moderate precipitation.", 
          "Conifer forests thrive in this cold yet relatively moist environment.", 
          "Dense pine forests flourish despite the cold, supported by abundant rainfall.", 
          "Perpetually foggy conditions combine with the cold to create ethereal misty woodlands.",
          "An extraordinary cold rainforest where hardy vegetation thrives despite the challenging conditions." },  // Cold
          
        { "Mediterranean-like conditions with hot, dry summers and mild, slightly damper winters.", 
          "Prairie grasslands dominate this moderately dry temperate zone.", 
          "Temperate deciduous forests thrive with reliable seasonal rainfall.", 
          "Lush temperate rainforests with moss-covered trees and abundant ferns.", 
          "Extremely fertile valleys and floodplains result from consistent heavy rainfall.", 
          "Constantly dripping temperate jungle conditions with moss and fungi covering every surface.",
          "A temperate paradise where constant rainfall creates the most biodiverse temperate rainforests on the planet." },  // Temperate
          
        { "Warm savannas with seasonal droughts and minimal tree cover.", 
          "Warm grasslands with scattered drought-resistant trees and scrub.", 
          "Subtropical conditions with moderate rainfall supporting diverse ecosystems.", 
          "Lush subtropical forests teeming with diverse plant and animal life.", 
          "Tropical forests with abundant rainfall supporting incredible biodiversity.", 
          "Warm mangrove swamps and perpetually flooded wetlands dominate the landscape.",
          "Vast rainforests stretch across the warm landscape, creating a paradise of biodiversity and constant rainfall." },  // Warm
          
        { "Scorching desert conditions with minimal vegetation limited to rare oases.", 
          "Arid scrubland where tough, heat-adapted vegetation clings to existence.", 
          "Hot seasonal forests with trees adapted to both heat and periodic dry seasons.", 
          "Hot and humid jungle conditions supporting dense vegetation.", 
          "Steamy rainforests with canopies so thick they create perpetual twilight below.", 
          "Oppressively hot and humid conditions create challenging living conditions despite water abundance.",
          "The ultimate rainforest environment where extreme heat and constant rainfall create the dense tropical conditions." },  // Hot
          
        { "An almost uninhabitable furnace where even extremophile organisms struggle.", 
          "Punishing heat combined with water scarcity creates a landscape of bare rock and sand.", 
          "Scattered vegetation survives in this scorching environment.", 
          "Surprisingly lush vegetation thrives despite the intense heat, creating unique ecosystems.", 
          "A challenging combination of extreme heat and heavy rainfall creates steamy, jungle-like conditions.", 
          "Constant rainfall in this scorching climate creates a perpetual sauna effect.",
          "An intense combination of extreme heat and maximum rainfall creates a lush environment." }   // Scorching
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

        // Identify special map types based on the same logic as MapTypeNameGenerator
        if (climate == 5 && moisture == 5) // Demonic
        {
            return "A hellish world dominated by the Hellscape and Brimstone biomes. The extreme heat and sulfurous atmosphere make this one of the most hostile environments imaginable. " + 
                   geopoliticalDesc + 
                   " The landscape is scarred by eternal flames and toxic fumes, where demon barbarians roam freely. Only the most resilient civilizations can hope to survive here, but the rewards in resources are immense.";
        }
        else if (climate == 3 && moisture == 5) // Monsoon
        {
            return "A monsoon world dominated by seasonal flooding and unique Floodlands biomes. Powerful rains transform the landscape periodically, creating rich agricultural opportunities. " +
                   geopoliticalDesc +
                   " The constant cycle of flood and retreat shapes both the terrain and the civilizations that call it home.";
        }
        else if (climate == 3 && moisture == 4) // Rainforest
        {
            return "A true rainforest world where perpetual rainfall creates the densest possible vegetation. Massive trees form multiple canopy layers, and the constant moisture supports incredible biodiversity. " +
                   geopoliticalDesc +
                   " The environment provides abundant resources but challenges movement and visibility.";
        }
        else if (climate == 5 && moisture == 0) // Scorched
        {
            return "A scorched wasteland dominated by the Ashlands and Charred Forest biomes. The intense heat and minimal moisture create a challenging environment where only the hardiest life survives. " +
                   geopoliticalDesc +
                   " Units must be cautious of terrain damage in the most extreme regions.";
        }
        else if (climate == 5 && moisture == 4) // Infernal
        {
            return "An infernal landscape where intense heat meets abundant moisture, creating unique Steam vents and Volcanic terrain. " +
                   geopoliticalDesc +
                   " The combination of extreme temperatures and high humidity makes this a challenging but resource-rich environment.";
        }

        // Add special waterworld description for archipelago/islands with oceanic moisture
        if (landType <= 1 && moisture >= 4)
        {
            return firstSentence + " This world is dominated by water, with limited land rising above the endless seas. " +
                   geopoliticalDesc +
                   " Ocean travel is essential for any civilization to thrive here.";
        }

        // Special description for extreme mountain desert combinations
        if (elevation == 2 && moisture <= 1)
        {
            return firstSentence + " Jagged mountain ranges with little vegetation create a harsh, forbidding landscape. " +
                   geopoliticalDesc +
                   " The peaks capture what little moisture exists, leaving rain-shadow deserts below.";
        }

        // Special description for high elevation with wet conditions
        if (elevation == 2 && moisture >= 4)
        {
            return firstSentence + " Constant precipitation in the mountains creates spectacular waterfalls and raging rivers that carve deep valleys. " +
                   geopoliticalDesc +
                   " Cloud forests cling to the slopes where conditions allow.";
        }

        // Default: add land type and elevation descriptions
        string elevationDesc = (landType == 3) ? elevationDescriptionsContinents[elevation] : elevationDescriptions[elevation];
        string desc = firstSentence + " " + landTypeDescriptions[landType] + " " + elevationDesc + " " + geopoliticalDesc;

        // If the map type name contains 'Rivers', append a note about double rivers
        string mapTypeName = MapTypeNameGenerator.GetMapTypeName(climate, moisture, landType, elevation);
        if (!string.IsNullOrEmpty(mapTypeName) && mapTypeName.IndexOf("Rivers", System.StringComparison.OrdinalIgnoreCase) >= 0)
        {
            desc += " This map type spawns twice as many rivers as normal, making river-adjacent locations much more common.";
        }

        // Add animal prevalence flavor text
        switch (animalPrevalence)
        {
            case 0:
                desc += " Wildlife is virtually extinct; the world feels eerily empty.";
                break;
            case 1:
                desc += " Animals are a rare sight, and the land is mostly quiet.";
                break;
            case 2:
                desc += " Wildlife is present, but only in isolated pockets.";
                break;
            case 3:
                // Normal, no extra text
                break;
            case 4:
                desc += " The world teems with wildlife; herds, flocks, and predators are a constant presence.";
                break;
            case 5:
                desc += " This is a world ruled by beasts! Animal migrations, predator packs, and swarms will shape every turn.";
                break;
        }

        return desc;
    }
    
    // Generate complex geopolitical descriptions based on civilization counts
    private static string GenerateGeopoliticalDescription(int civCount, int cityStateCount, int tribeCount, int climate, int moisture, int landType, int elevation)
    {
        System.Text.StringBuilder desc = new System.Text.StringBuilder();
        
        // Calculate geopolitical complexity metrics
        int totalEntities = civCount + cityStateCount + tribeCount;  // Total number of entities for summary checks
        
        // Civilization dynamics
        if (civCount <= 2)
        {
            desc.Append("In this bipolar world, two great civilizations will inevitably clash for supremacy, their rivalry shaping the course of history. ");
        }
        else if (civCount <= 4)
        {
            desc.Append("A delicate balance of power exists between " + GetNumberWord(civCount) + " peoples. The fate of nations is often determined by shifting alliances. ");
        }
        else if (civCount <= 6)
        {
            desc.Append("The world stage hosts " + GetNumberWord(civCount) + " ambitious civilizations, creating a complex web of diplomacy where regional powers vie for dominance. ");
        }
        else if (civCount <= 8)
        {
            desc.Append("With " + GetNumberWord(civCount) + " major civilizations competing for resources and territory. Every move has far-reaching consequences. ");
        }
        else
        {
            desc.Append("An unprecedented " + civCount + " civilizations crowd this world, creating a powder keg of competing interests. Even minor disputes can escalate into world wars. ");
        }
        
        // City-state dynamics
        if (cityStateCount == 0)
        {
            desc.Append("There are no city-states to serve as buffers or trading partners.");
        }
        else if (cityStateCount <= 3)
        {
            desc.Append("A handful of fiercely independent city-states control strategic locations, becoming valuable allies or contested prizes in the great game of empires. ");
        }
        else if (cityStateCount <= 6)
        {
            desc.Append("Numerous city-states dot the landscape, forming a mercantile network that enriches those who court their favor. ");
        }
        else if (cityStateCount <= 10)
        {
            desc.Append("A thriving constellation of city-states creates a complex diplomatic ecosystem. ");
        }
        else
        {
            desc.Append("An extraordinary " + cityStateCount + " city-states fragment the political landscape, creating a patchwork of micro-nations. ");
        }
        
        // Tribal dynamics
        if (tribeCount == 0)
        {
            desc.Append("The absence of tribal societies suggests a world where civilization has already triumphed over primitivism, leaving no unconquered frontiers. ");
        }
        else if (tribeCount <= 2)
        {
            desc.Append("Isolated tribal societies persist in the harshest regions, occasionally raiding civilized lands but mostly serving as reminders of a primitive past. ");
        }
        else if (tribeCount <= 4)
        {
            desc.Append("Warlike tribes control significant wilderness areas, forcing civilizations to garrison their frontiers against constant barbarian incursions. ");
        }
        else if (tribeCount <= 6)
        {
            desc.Append("Numerous tribal confederations dominate the untamed lands, creating a constant threat that can unite civilizations in common cause or exploit their divisions. ");
        }
        else
        {
            desc.Append("A staggering " + tribeCount + " tribal groups roam the wilderness, creating vast ungovernable regions where civilization's light struggles to penetrate the darkness. ");
        }
        
        // Environmental impact on geopolitics
        if (landType <= 1) // Archipelago/Islands
        {
            desc.Append("The fragmented geography naturally isolates civilizations. Naval supremacy has proven the key to empire, despite the diversity of the peoples formed by this land.. ");
        }
        else if (landType == 4) // Pangaea
        {
            desc.Append("The unified landmass ensures that no civilization can hide behind geographic barriers, guaranteeing intense competition and making isolationism impossible. ");
        }
        
        // Elevation and civilization count interactions
        if (elevation == 2) // Mountainous terrain
        {
            if (civCount >= 8)
            {
                desc.Append("The mountainous terrain will channel these " + civCount + " civilizations into fierce competition for limited passes and valleys. Every mountain pass becomes a strategic chokepoint and control of high ground determines the fate of empires. A scarcity of arable land in these peaks means warriors spill blood for drops of farmlad. ");
            }
            else if (civCount <= 3)
            {
                desc.Append("The towering mountains provide natural borders between the few civilizations, creating isolated pockets of development. The terrain favors defenders so heavily that diplomatic solutions may prove more effective than military conquest. ");
            }
            
            if (cityStateCount >= 6)
            {
                desc.Append("Mountain city-states control crucial passes and resource-rich valleys, becoming wealthy toll-keepers and mining centers that major powers must court or conquer. ");
            }
        }
        else if (elevation == 0) // Flat terrain
        {
            if (civCount >= 7)
            {
                desc.Append("With no natural barriers on these vast plains, " + civCount + " civilizations clash in massive open-field battles where cavalry charges and rapid troop movements determine victory. ");
            }
            else
            {
                desc.Append("The open plains allow civilizations to expand rapidly in all directions, but also leave them vulnerable to swift attacks from any angle. ");
            }
            
            if (tribeCount >= 5)
            {
                desc.Append("Barbarian horsemen roam these steppes freely, striking settlements and vanishing into the horizon before defenders can respond. ");
            }
        }
        else if (elevation == 1) // Hills and moderate elevation
        {
            desc.Append("The rolling hills create a dynamic battlefield where positioning and terrain knowledge provide significant advantages. ");
            
            if (civCount >= 5)
            {
                desc.Append("With " + civCount + " civilizations vying for control, constant skirmishes over hilltops and valleys are the norm. The terrain allows for diverse military strategies - some civilizations become masters of hilltop fortresses while others perfect the art of valley ambushes. ");
            }
            
            if (cityStateCount >= 10)
            {
                desc.Append("City-states have claimed the best defensive positions atop hills, creating a complex network of fortified settlements that fragment the landscape into dozens of small territories. ");
            }
        }
        
        // Climate impact on conflict
        if (climate <= 1) // Frozen/Cold
        {
            desc.Append("The harsh climate limits population growth and creates fierce competition. Every temperate valley is a potential flashpoint. ");
        }
        else if (climate >= 4) // Hot/Scorching
        {
            desc.Append("The oppressive heat clusters the world around a water sources and cooler elevations. Civilizations have two choices: cooperation or conflict. ");
        }
        
        // Entity count-based predictions with elevation context
        if (totalEntities >= 15)
        {
            desc.Append("With so many civilizations, city-states, and tribes competing for space, border tensions are constant and vicious. ");
            
            if (elevation == 2)
            {
                desc.Append("The crowded mountain valleys will witness warfare on an unprecedented scale, with peoples building crowding closer and closer together when they can't expand. ");
            }
            else if (elevation == 0)
            {
                desc.Append("On these crowded plains, expect massive battlefield clashes as multiple civilizations converge on the same strategic locations simultaneously. ");
            }
        }
        else if (totalEntities <= 8)
        {
            desc.Append("With relatively few powers in the world, vast distances have allowed for long periods of peaceful development. When cultures finally meet, the clash may be catastrophic. ");
            
            if (elevation == 1)
            {
                desc.Append("The scattered civilizations among the hills will develop unique cultures in isolation, leading to fascinating diplomatic opportunities when they finally meet. ");
            }
        }
        
        // Elevation-based resource distribution
        if (elevation == 2)
        {
            desc.Append("Mountain civilizations will enjoy abundant mineral wealth but struggle with food production, making trade relationships essential for survival. ");
            
            if (cityStateCount >= 8)
            {
                desc.Append("Mountain city-states will monopolize rare metal deposits, becoming the weapons dealers of the world. ");
            }
        }
        else if (elevation == 0 && moisture >= 3)
        {
            desc.Append("The flat, fertile plains provide agricultural abundance, allowing civilizations to support larger populations and armies than their mountainous neighbors. ");
        }
        else if (elevation == 1)
        {
            desc.Append("The varied elevation of hills provides a perfect balance of agricultural valleys and mineral-rich heights, giving civilizations here the most diverse economic options. ");
        }
        
        
        // Special combinations
        if (civCount > 6 && cityStateCount > 8 && tribeCount > 4)
        {
            desc.Append("This complex world is a living, breathing organism of competing interests. Master diplomats thrive while warmongers often find themselves overwhelmed by coalitions. ");
        }
        else if (civCount <= 3 && cityStateCount == 0 && tribeCount >= 6)
        {
            desc.Append("A primordial struggle between civilization and barbarism defines this world, where a few advanced societies stand as beacons of progress against the encroaching darkness. ");
        }
        else if (cityStateCount > civCount * 2)
        {
            desc.Append("IN this world, local identity trumps imperial ambition, creating a medieval patchwork of competing city states. ");
        }
        
        // Additional elevation-based scenarios
        if (elevation == 2 && tribeCount >= 8)
        {
            desc.Append("The mountains have become a barbarian stronghold! These high-altitude tribes have adapted to the thin air and treacherous terrain, launching devastating raids from hidden valleys and alpine passes. Traditional armies will struggle with altitude sickness and narrow paths while these mountain warriors strike with impunity. Civilizations must develop specialized mountain units or risk losing their foothill settlements to constant raids. ");
        }
        else if (elevation == 0 && totalEntities >= 15)
        {
            desc.Append("The combination of flat terrain and many competing powers creates a powder keg - with no natural barriers to slow conflicts, wars will spread like wildfire across the plains. Every civilization can see and reach every other, making long-term peace nearly impossible. The lack of defensive terrain means the only safety lies in offensive action, encouraging preemptive strikes and aggressive expansion. ");
        }
        else if (elevation == 1 && civCount == 2)
        {
            desc.Append("The rolling hills provide the perfect stage for an epic duel between two civilizations. Each will develop unique approaches to the terrain - one might become masters of hilltop fortifications while the other perfects mobile warfare in the valleys. The varied elevation creates natural battle lines that will shift back and forth throughout the ages, with certain hills becoming legendary for the battles fought over them. ");
        }
        
        // Climate and elevation combinations
        if (climate <= 1 && elevation == 2)
        {
            desc.Append("Frozen peaks create some of the harshest conditions imaginable - civilizations must balance the defensive advantages of mountain fortresses against the brutal reality of feeding populations at high altitude in perpetual winter. ");
        }
        else if (climate >= 4 && elevation == 0)
        {
            desc.Append("The scorching flatlands offer no escape from the heat, forcing civilizations to cluster around water sources and creating inevitable conflict zones wherever rivers flow. ");
        }
        
        // More elevation interactions with moisture
        if (elevation == 2 && moisture >= 5)
        {
            desc.Append("Cloud forests shroud these mountains in perpetual mist, creating a mystical landscape where armies can disappear into fog-covered valleys. ");
        }
        else if (elevation == 0 && moisture <= 1)
        {
            desc.Append("These bone-dry flatlands stretch endlessly under the sun, creating vast desert plains where only the hardiest civilizations survive. Caravans must carefully plan routes between rare oases, and control of these water sources means control of entire regions. ");
        }
        else if (elevation == 1 && moisture >= 4)
        {
            desc.Append("Lush, rolling hills covered in dense forests create a paradise for civilizations that master forestry. The abundant rainfall feeds countless streams that carve the hills into a maze of valleys perfect for ambushes. ");
        }
        
        // Elevation with specific landtypes
        if (elevation == 2 && landType <= 1)
        {
            desc.Append("Volcanic island chains thrust dramatically from the ocean, creating vertical civilizations where every acre of flat land is precious. ");
        }
        else if (elevation == 0 && landType == 4)
        {
            desc.Append("This vast, flat supercontinent is an enormous chessboard. With nowhere to hide and everywhere to expand, speed is key. ");
        }
        
        // Complex elevation scenarios with multiple factors
        if (elevation == 2 && civCount >= 6 && tribeCount >= 6)
        {
            desc.Append("The mountains are contested by both civilizations and tribes, creating three-dimensional conflicts across the globe. ");
        }
        else if (elevation == 1 && cityStateCount >= 12 && civCount <= 4)
        {
            desc.Append("The hills are dominated by independent city-states that have turned every defensible hilltop into a mini-fortress. Peoples must navigate this political minefield. ");
        }
        else if (elevation == 0 && civCount >= 10)
        {
            desc.Append("With no defensive advantages, and very little space, the strategy of many a people is to attack first and attack hard. ");
        }
        
        // Elevation affecting different game phases
        if (elevation == 2)
        {
            if (civCount <= 4)
            {
                desc.Append("Mountains keep civilizations separated, allowing peaceful development. ");
            }
            else
            {
                desc.Append("The many civilizations dotting these mountains are industrious and warlike. ");
            }
        }
        else if (elevation == 0 && tribeCount >= 10)
        {
            desc.Append("The flat terrain gives barbarian cavalry free reign to terrorize the countryside. ");
        }
        
        // Ultra-specific elevation combinations
        if (elevation == 2 && climate == 3 && moisture >= 4)
        {
            desc.Append("These temperate mountain rainforests create beautiful and defensible terrain. ");
        }
        else if (elevation == 1 && civCount == 5 && cityStateCount == 5)
        {
            desc.Append("The perfect balance of hills, civilizations, and city-states creates a complex diplomatic puzzle. ");
        }
        else if (elevation == 0 && climate == 0 && civCount >= 6)
        {
            desc.Append("This is survival of the fittest at its most brutal. The barren land and freezing climate permits only the most efficient peoples to survive. ");
        }
        
        // Elevation affecting unit types and warfare
        if (elevation == 2)
        {
            desc.Append("Towering peaks dominate this world and the peoples call it home. ");
            
            if (landType <= 1)
            {
                desc.Append("These mountain islands hide secrets few are brave enough to find. ");
            }
        }
        else if (elevation == 0)
        {
            desc.Append("Horse peoples breed strong steeds and train the finest riders ... when they aren't raising their neighbors. ");
        }
        else if (elevation == 1 && tribeCount >= 7)
        {
            desc.Append("The hills provide just enough cover for barbarian ambushes but not enough to stop their raids entirely. ");
        }
        
        // Final elevation summary
        if (elevation == 2 && tribeCount + civCount + cityStateCount >= 20)
        {
            desc.Append("These crowded mountains create a complex world, where every valley becomes a kingdom and every peak a fort. ");
        }
        else if (elevation == 0 && totalEntities >= 15)
        {
            desc.Append("These crowded flatlands guarantee constant warfare - with nowhere to hide and too many competitors, this world will know no lasting peace. ");
        }
        else if (elevation == 1)
        {
            desc.Append("The rolling hills strike a perfect balance between defensibility and accessibility, creating a world where both peaceful builders and aggressive conquerors can find success. ");
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