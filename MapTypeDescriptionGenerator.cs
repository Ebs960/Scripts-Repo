using System.Collections.Generic;
using UnityEngine;

public static class MapTypeDescriptionGenerator
{
    // Climate description templates - evocative fantasy descriptions of the land
    private static readonly string[] climateDescriptions = {
        // Frozen (0)
        "An ancient realm locked in eternal winter, where glaciers carve through mountains like the fingers of sleeping giants. " +
        "The aurora dances across skies of deepest black, casting ethereal light upon endless fields of pristine snow. " +
        "Ice-locked seas groan and crack, their frozen surfaces hiding depths that have not seen sunlight in millennia.",
        
        // Cold (1)
        "A stark and beautiful land where winter reigns for most of the year, its grip only loosening for brief, precious summers. " +
        "Vast boreal forests of pine and spruce stretch to the horizon, their dark boughs heavy with snow. " +
        "Frozen lakes shimmer like scattered mirrors across the tundra, and the northern lights paint the long nights with ghostly fire.",
        
        // Temperate (2)
        "A land blessed by the changing seasons, where spring brings carpets of wildflowers, summer ripens golden fields, " +
        "autumn sets the forests ablaze with color, and winter blankets the world in peaceful silence. " +
        "Rolling hills give way to misty valleys, and ancient oak forests shelter countless secrets within their shadowed depths.",
        
        // Warm (3)
        "A sun-drenched realm where the warmth of long summers seeps into the very stones. " +
        "Golden savannas stretch beneath vast blue skies, punctuated by acacia groves and watering holes that draw all manner of life. " +
        "The air shimmers with heat, carrying the scent of sun-baked earth and the distant promise of rain.",
        
        // Hot (4)
        "A scorching land where the sun beats down with relentless fury, baking the earth until it cracks like ancient parchment. " +
        "Vast deserts of shifting dunes give way to hardy scrubland, where only the most tenacious life survives. " +
        "Mirages dance on the horizon, and oases are jewels more precious than gold.",
        
        // Scorching (5)
        "A merciless furnace of a world, where the very air burns the lungs and shadows offer no respite from the infernal heat. " +
        "The ground is cracked and blackened, volcanic vents spew sulfurous fumes, and rivers of molten rock carve paths through the wasteland. " +
        "Only the hardiest—or most desperate—dare make their home in this hellish realm."
    };

    // Moisture description templates - detailed water and vegetation
    private static readonly string[] moistureDescriptions = {
        // Desert (0)
        "Water is the rarest treasure here, hoarded jealously by those who find it. " +
        "The land is a study in browns and ochres, with hardy succulents and thorny scrub the only vegetation for leagues. " +
        "Dust devils spin across cracked earth, and the bones of less fortunate travelers bleach beneath the unforgiving sky.",
        
        // Arid (1)
        "Scarce rainfall means every drop is precious, and settlements cluster around seasonal rivers and hidden springs. " +
        "Tough grasses cling to life in sheltered valleys, and gnarled trees with deep taproots mark the locations of underground water. " +
        "The wind carries red dust that stains everything it touches.",
        
        // Standard (2)
        "Balanced rainfall nurtures a diverse tapestry of life, from open meadows to dense woodland. " +
        "Clear streams wind through the countryside, feeding lakes and marshes that teem with waterfowl. " +
        "The land is generous to those who work it, yielding crops and timber in abundance.",
        
        // Moist (3)
        "Frequent rains keep the land perpetually green, with lush vegetation covering every surface. " +
        "Rivers run swift and full, their banks lined with willows and reeds. Mushrooms and mosses thrive in the damp shade, " +
        "and the air is thick with the scent of rich, fertile earth and growing things.",
        
        // Wet (4)
        "Near-constant precipitation transforms the land into a waterlogged realm of swamps, bogs, and flooded forests. " +
        "Mist hangs in the air like a living thing, and the ground squelches underfoot even on the driest days. " +
        "Waterways are the true roads here, winding through curtains of hanging moss and stands of cypress.",
        
        // Oceanic (5)
        "The sea dominates all aspects of life, its salt spray carried inland by endless winds. " +
        "Perpetual fog blankets the coastlines, and storms sweep in without warning, their fury legendary. " +
        "The boundary between land and water is ever-shifting, with tides that can strand the unwary far from shore."
    };

    // Land type descriptions - geographic character
    private static readonly string[] landTypeDescriptions = {
        // Archipelago (0)
        "A scattered constellation of islands spreads across endless seas, each a world unto itself. " +
        "Some are mere specks of sand and palm, while others rise as volcanic peaks wreathed in cloud. " +
        "The ocean connects and divides these lands—those who master the waves can travel freely between distant shores.",
        
        // Islands (1)
        "Great islands and lesser isles dot the expansive waters, connected by treacherous straits and hidden reefs. " +
        "Each landmass bears its own character: some verdant and teeming with life, others barren and wind-scoured. " +
        "The sea shapes all aspects of life here, connecting distant communities and isolating others in their own island worlds.",
        
        // Standard (2)
        "Multiple continents divide the world's great oceans, each a stage for its own stories of discovery and civilization. " +
        "Peninsulas reach out like grasping fingers, sheltered bays invite settlement, and mountain ranges create natural divisions " +
        "that have shaped the development of distinct cultures and regions since time immemorial.",
        
        // Continents (3)
        "Vast continental landmasses dominate this world, their interiors stretching for countless leagues from coast to coast. " +
        "Here, geography writes history in bold strokes—great rivers serve as arteries of commerce, mountain ranges create natural boundaries, " +
        "and the sheer scale of the land means that many regions have never seen the face of an outsider.",
        
        // Pangaea (4)
        "A single colossal supercontinent sprawls across the world, its borders touching every climate from frozen pole to blistering equator. " +
        "There are no distant shores to discover here—all peoples share the same immense landmass, " +
        "and events that occur at one edge inevitably ripple across the entire world.",

        // Terrestrial (5)
        "Land overwhelms sea on this world, with broad continental interiors broken only by inland seas, narrow straits, and a handful of distant coasts. " +
        "Campaigns unfold across enormous connected fronts, where geography favors long marches, continental rivalries, and wars fought over the few true maritime chokepoints."
    };

    // Elevation descriptions - terrain character
    private static readonly string[] elevationDescriptions = {
        // Flat (0)
        "The land lies flat and open beneath vast skies, with horizons that seem to stretch into infinity. " +
        "Travelers can journey for days without encountering a hill worth naming, and the endless plains offer unobstructed views in every direction. " +
        "There are few places to hide and fewer natural landmarks—communities must build their own identity upon this blank canvas.",
        
        // Hilly (1)
        "Rolling hills and weathered ridges break up the landscape, offering commanding views of the surrounding countryside. " +
        "Valleys shelter fertile farmland and peaceful settlements, while hilltop communities have watched over their domains for generations. " +
        "The terrain creates natural boundaries and shelters, shaping the flow of life across the land.",
        
        // Mountainous (2)
        "Towering peaks pierce the clouds, their snow-capped summits visible for hundreds of leagues. " +
        "Ancient mountain ranges divide the world into isolated regions, with narrow passes providing the only routes between valleys. " +
        "Legends speak of treasures hidden in high caves and forgotten valleys deep within these remote peaks."
    };

    // Continent-specific elevation descriptions
    private static readonly string[] elevationDescriptionsContinents = {
        // Flat (0)
        "Vast tablelands and sweeping plateaus stretch across the continental interior, their surfaces worn smooth by eons of wind and rain. " +
        "The scale is almost incomprehensible—one can travel for weeks and see nothing but grassland meeting sky at the horizon. " +
        "Great herds migrate across these open spaces, following ancient paths known only to the wisest hunters.",
        
        // Hilly (1)
        "Immense continental plateaus rise and fall in great waves, their escarpments creating dramatic vistas that steal the breath. " +
        "River systems have carved deep canyons through the uplands, creating natural pathways through the rugged terrain. " +
        "The highlands are rich in minerals, drawing prospectors and miners from distant lands.",
        
        // Mountainous (2)
        "A colossal massif forms the spine of the continent, its peaks so high that they scrape the heavens themselves. " +
        "The great continental divide determines the fate of all water that falls upon the land, sending rivers flowing to opposite ends of the world. " +
        "These mountains are old beyond reckoning, and the creatures that dwell in their heights are unlike any found in the lowlands."
    };

    // Special combination descriptions for climate + moisture
        private static readonly string[,] climateMoistureDescriptions = {
        // Desert      Arid        Standard     Moist        Wet         Oceanic      Rainforest
        { "A frozen desert where ice replaces sand and blizzards scour the land clean of all but the hardiest life. Snow dunes shift like their sandy cousins, burying all traces of those who came before.", 
          "A frigid wasteland where sparse snowfall leaves the frozen ground exposed to cutting winds. Hardy lichens cling to rocks, providing sustenance for the few creatures adapted to this harsh realm.", 
          "A snow-blanketed realm of pristine beauty, where frozen forests stand like crystal sculptures and ice-bound lakes hide secrets beneath their glittering surfaces.", 
          "A frozen wetland where ice-covered lakes and frozen marshes create a labyrinthine landscape. In the brief summer thaw, these lands explode with life before winter reclaims its dominion.", 
          "A land of perpetual winter storms, where glaciers grind inexorably forward and avalanches reshape the mountains. The snow never stops falling, burying entire forests beneath its weight.", 
          "An ice-locked realm of constant precipitation, where freezing rain and sleet transform the world into a treacherous wonderland of ice-coated beauty.",
          "A paradox of constant snowfall and hardy vegetation, where ancient evergreens bow beneath tons of accumulated snow yet somehow endure through endless winters." },
          
        { "A cold steppe where bitter winds sweep across frozen grasslands and survival means constant movement. Herds of woolly beasts roam these lands, hunted by predators and nomads alike.", 
          "Boreal forests give way to tundra, with permafrost lurking just beneath the surface. Short growing seasons yield hardy crops for those patient enough to coax life from the reluctant earth.", 
          "Vast taiga forests of spruce and pine stretch endlessly, their needle-carpeted floors silent save for the crunch of snow and the distant howl of wolves. The cold is a constant companion, but life perseveres.", 
          "Dense conifer forests thrive despite the cold, their canopies sheltering a surprising diversity of life. Crystal-clear streams teem with fish, and the undergrowth is thick with berry bushes.", 
          "A cold, wet realm where fog and mist obscure towering pines. Mosses and ferns carpet the forest floor, and the air is thick with the scent of damp earth and evergreen.", 
          "Perpetual fog banks roll in from frigid seas, creating an ethereal landscape where visibility is measured in paces. The forests here are ancient and moss-draped, haunted by strange calls.",
          "A cold rainforest of impossible beauty, where ancient trees drip with moisture and the undergrowth is a tangle of ferns and fungi. Life here is hardy, adapted to both the wet and the chill." },
          
        { "A Mediterranean paradise where dry summers bake golden hills and winter rains bring brief, explosive growth. Olive groves and vineyards thrive on the sun-drenched slopes.", 
          "Rolling prairies of golden grass stretch to the horizon, dotted with wildflowers in spring and rippling like a golden sea in summer. This is a land of big skies and bigger dreams.", 
          "The epitome of temperate beauty, where deciduous forests blaze with autumn color and spring meadows burst with wildflowers. Four distinct seasons paint the land in ever-changing hues.", 
          "Lush temperate rainforests drip with moisture, their ancient trees festooned with moss and fern. Salmon-choked rivers carve through verdant valleys, sustaining a web of life unmatched in its complexity.", 
          "Frequent rainfall nurtures fertile valleys and floodplains where crops grow tall and harvests are bountiful. The rivers run full year-round, their banks lined with prosperous settlements.", 
          "A temperate jungle of sorts, where constant rainfall supports vegetation so thick that sunlight barely reaches the forest floor. The air is perpetually damp, and mushrooms grow to enormous sizes.",
          "A temperate paradise where gentle rains fall almost daily, nurturing forests of breathtaking beauty. Every surface is covered in green, and the land seems to pulse with vibrant life." },
          
        { "Warm savannas stretch beneath vast skies, their golden grasses punctuated by acacia trees and termite mounds. The dry season tests all who dwell here, but the rains bring miraculous renewal.", 
          "Subtropical grasslands with scattered woodlands support a diverse array of grazers and the predators that hunt them. The warm climate and seasonal rains create a land of dramatic abundance.", 
          "A subtropical realm of pleasant warmth and moderate rainfall, where palm trees sway in gentle breezes and fruit grows heavy on the vine. Life here is comfortable, perhaps too comfortable.", 
          "Lush subtropical forests teem with colorful birds and flowering plants. The warm, humid air carries the scent of a thousand blossoms, and life exists in dazzling, almost overwhelming variety.", 
          "Tropical forests thrive in the abundant rainfall, their canopies alive with the calls of countless creatures. Vines drape every surface, and the competition for light drives trees to staggering heights.", 
          "Warm mangrove swamps and flooded wetlands create a watery maze where land and sea blend into one. Life here is amphibious by necessity, adapted to both elements.",
          "Vast rainforests stretch unbroken for countless leagues, their biodiversity beyond cataloging. Every footstep disturbs a dozen creatures, and the forest hums with the ceaseless activity of a million lives." },
          
        { "A scorching desert where dunes of golden sand shift with each hot wind, Oases are fiercely guarded prizes.", 
          "Arid scrublands bake beneath an unforgiving sun, where thorny plants and venomous creatures have evolved to survive on almost nothing. Water is worth more than gold.", 
          "Hot seasonal forests have adapted to cycles of growth and dormancy, their deciduous trees shedding leaves to conserve water during scorching dry seasons.", 
          "Steamlands jungles where the heat and humidity combine to create a greenhouse of explosive growth. Sweat is constant, comfort is rare, but life flourishes in overwhelming abundance.", 
          "Dense rainforests swelter beneath perpetual clouds, their canopies so thick that the forest floor exists in permanent twilight. The heat is oppressive, but the life is spectacular.", 
          "Oppressive heat combines with constant moisture to create conditions that test the endurance of all but the most adapted creatures. Fungi and insects thrive while others suffer.",
          "A hothouse realm where extreme heat and torrential rainfall create vegetation of almost alarming vigor. Plants grow visibly day by day, and the jungle reclaims any clearing within weeks." },
          
        { "An almost uninhabitable furnace where exposed rock glows with heat and shade is a luxury worth killing for. Only the most desperate or determined attempt to survive here.", 
          "A hellish landscape of bare rock and shifting sand, where temperatures can kill an unprepared traveler within hours. Life here is sparse, specialized, and remarkably tenacious.", 
          "Against all odds, scattered vegetation clings to existence in this scorching realm, finding purchase in sheltered crevices and drawing water from sources unknown.", 
          "The impossible combination of extreme heat and abundant moisture creates a pressure-cooker environment where life grows at a feverish pace, competing frantically for every resource.", 
          "Extreme heat meets constant rainfall in a Steamlands nightmare where visibility is measured in feet and the air itself seems to sweat. The jungles here are alien, primal, and utterly unforgiving.", 
          "A perpetual sauna where the heat and humidity combine to create conditions nearly intolerable to normal life. Strange creatures adapted to this inferno lurk in the scalding mists.",
          "Where extreme heat meets maximum rainfall, a lush but terrifying environment emerges. Toxic plants, venomous creatures, and diseases unknown elsewhere make this a deadly paradise." }
    };

    // Wildlife descriptions by climate and prevalence
    private static readonly string[][] wildlifeDescriptions = {
        // Frozen (0)
        new[] {
            "Wildlife is virtually extinct—only bones and frozen carcasses remain as testament to creatures that once roamed these frozen wastes.",
            "Animals are exceedingly rare, with only the hardiest arctic foxes and snow hares glimpsed in the eternal twilight.",
            "Scattered populations of polar bears, arctic wolves, and reindeer survive in isolated pockets, their white coats blending with the endless snow.",
            "The frozen realm supports surprising diversity: polar bears hunt seals on the ice, wolves track caribou herds, and snowy owls glide silently through the darkness.",
            "Wildlife thrives despite the cold—massive polar bears command the ice floes, herds of woolly mammoths shake the frozen ground, and packs of dire wolves howl at the aurora.",
            "The tundra teems with life: mammoth herds darken the horizon, saber-toothed cats stalk the unwary, and great white bears grow fat on abundant prey."
        },
        // Cold (1)
        new[] {
            "The forests are silent and empty—whatever creatures once lived here have long since perished or fled.",
            "Occasional tracks in the snow hint at the presence of wolves or elk, but sightings are rare treasures.",
            "Bears fish the salmon runs, moose browse the lakeshores, and wolves maintain their ancient territories in the deepest woods.",
            "The boreal forests support robust populations of brown bears, elk, wolves, and countless smaller creatures. The rivers run thick with fish during spawning season.",
            "Wildlife flourishes in the cold forests—massive elk clash antlers in autumn, bear families grow fat on berry bushes, and wolf packs raise their young in ancient dens.",
            "A wild bounty fills the taiga: dire wolves rule the pack, cave bears grow to monstrous size, and herds of giant elk number in the thousands."
        },
        // Temperate (2)
        new[] {
            "The once-vibrant forests are eerily quiet—human activity or some unknown catastrophe has stripped the land of its wildlife.",
            "Deer are occasionally spotted at dawn, and songbirds have begun to return, but the forests feel hollow and waiting.",
            "Standard wildlife populations maintain a delicate balance: deer graze the meadows, foxes hunt the fields, and hawks circle lazily overhead.",
            "The temperate lands support diverse wildlife: deer and boar roam the forests, rabbits populate the meadows, and predators like wolves and mountain lions keep the herds in check.",
            "Wildlife abounds in these fertile lands—great stags lead their herds through ancient forests, wild boar root through the underbrush, and the skies darken with flocks of migratory birds.",
            "A paradise for hunters and naturalists alike: the forests echo with the calls of countless creatures, from the mighty aurochs to the cunning fox."
        },
        // Warm (3)
        new[] {
            "The savannas lie empty and still—the great herds that once thundered across these plains are gone, leaving only sun-bleached bones.",
            "Scattered antelopes and wary lions are all that remain of once-great populations, their survival a daily struggle.",
            "The warm grasslands support their classic inhabitants: zebras, wildebeest, and gazelles graze under the watchful eyes of lions and hyenas.",
            "Rich wildlife fills the savannas: elephant families traverse their ancestral routes, giraffes browse the treetops, and the great cats maintain their territories.",
            "The warm lands teem with magnificent beasts—elephant herds darken the horizon, prides of lions rule the grasslands, and rhinoceros defend their watering holes with ancient fury.",
            "An explosion of wildlife fills every ecological niche: hippos crowd the rivers, crocodiles lurk in the shallows, and great apes claim the forest edges as their domain."
        },
        // Hot (4)
        new[] {
            "The desert appears lifeless—whatever creatures survived the heat have been driven out or eliminated entirely.",
            "Scorpions and snakes are the most common sights, with larger predators like jackals only rarely spotted near oases.",
            "Adapted wildlife clings to existence: camels traverse the dunes, sand cats hunt by night, and vultures circle endlessly overhead.",
            "The hot lands support specialized creatures: desert foxes, sand vipers, and oasis-dwelling crocodiles have all carved out niches in this harsh environment.",
            "Despite the heat, wildlife thrives—desert lions have adapted to hunt by night, massive monitor lizards bask on sun-baked rocks, and oases teem with life.",
            "The scorching realm is paradoxically alive: great serpents rule the dunes, massive scorpions emerge at dusk, and predators of terrible cunning hunt the unwary."
        },
        // Scorching (5)
        new[] {
            "Nothing lives here—even the hardiest creatures have abandoned this hellish realm to the fire and ash.",
            "Strange, twisted creatures are rumored to survive near volcanic vents, but none have been reliably documented.",
            "Fire salamanders and heat-resistant lizards represent the only wildlife adapted to these infernal conditions.",
            "Unlikely life persists: fire beetles scuttle across cooling lava flows, ash drakes hunt in the smoke-filled skies, and something massive stirs in the deepest magma pools.",
            "The infernal landscape crawls with creatures that should not exist—fire-breathing lizards, obsidian-scaled serpents, and things that feed on heat itself.",
            "A nightmare menagerie stalks these lands: demons made flesh, creatures of living flame, and beasts whose very blood is molten rock."
        }
    };

    // Get a description for a specific map type
    public static string GetDescription(int climate, int moisture, int landType, int elevation)
    {
        return GetDescription(climate, moisture, landType, elevation, 4, 2, 2, 3);
    }
    
    // Overloaded method with civilization counts
    public static string GetDescription(int climate, int moisture, int landType, int elevation, int aiCivCount, int cityStateCount, int tribeCount)
    {
        return GetDescription(climate, moisture, landType, elevation, aiCivCount, cityStateCount, tribeCount, 3);
    }
    
    // Main method with animalPrevalence
    public static string GetDescription(int climate, int moisture, int landType, int elevation, int aiCivCount, int cityStateCount, int tribeCount, int animalPrevalence)
    {
        climate = Mathf.Clamp(climate, 0, climateDescriptions.Length - 1);
        moisture = Mathf.Clamp(moisture, 0, moistureDescriptions.Length - 1);
        landType = Mathf.Clamp(landType, 0, landTypeDescriptions.Length - 1);
        elevation = Mathf.Clamp(elevation, 0, elevationDescriptions.Length - 1);
        animalPrevalence = Mathf.Clamp(animalPrevalence, 0, 5);

        int peopleCount = aiCivCount + 1;
        string mapTypeName = MapTypeNameGenerator.GetMapTypeName(climate, moisture, landType, elevation);

        string landMood = GetLandMoodSentence(climate, moisture);
        string terrain = GetTerrainSentence(landType, elevation);
        string wildlife = GetWildlifePhrase(climate, animalPrevalence);
        string special = GetSpecialWorldModifier(mapTypeName);
        string human = GetHumanPresenceSentence(peopleCount, cityStateCount, tribeCount);

        System.Text.StringBuilder desc = new System.Text.StringBuilder();
        desc.Append(landMood);
        desc.Append(" ");
        desc.Append(terrain);
        if (!string.IsNullOrEmpty(special))
        {
            desc.Append(" ");
            desc.Append(special);
        }
        desc.Append(" ");
        desc.Append(wildlife);
        desc.Append(" ");
        desc.Append(human);

        return desc.ToString().Replace("\n", " ").Trim();
    }

    private static string GetLandMoodSentence(int climate, int moisture)
    {
        string climateBase = climate switch
        {
            0 => "A pale wilderness of ice, stone, and cutting wind lies beneath a hard sky.",
            1 => "Dark forests, cold rivers, and long winters shape this northern wilderness.",
            2 => "Seasonal forests, meadows, and river valleys spread across a living temperate land.",
            3 => "Warm grasslands, open woods, and sunlit waterholes stretch beneath wide skies.",
            4 => "Dry heat, scrub, and hard earth define a land where water draws every living thing.",
            _ => "Ash, heat, and broken stone mark a violent land where the ground itself feels restless."
        };

        string moistureMod = moisture switch
        {
            0 => "Dry valleys and exposed stone leave water scarce.",
            1 => "Dusty winds and thin streams keep every spring precious.",
            2 => "Balanced rain feeds clear streams and open meadows.",
            3 => "Frequent rain, moss, and soft marsh edges darken the ground.",
            4 => "Flooded woods and wet marshes hold standing water through every season.",
            _ => "Fog, storm winds, and shifting shores blur the line between land and sea."
        };

        return climateBase + " " + moistureMod;
    }

    private static string GetTerrainSentence(int landType, int elevation)
    {
        string terrainBase = landType switch
        {
            0 => "Broken islands and open seas divide the horizon, leaving each shore exposed to wind and tide.",
            1 => "Large islands and lesser isles rise from dangerous water, their interiors shaped by hidden paths and sheltered coves.",
            2 => "Forests, plains, rivers, and high ground break the land into natural regions.",
            3 => "Broad landmasses stretch far inland, where rivers and distant ridges fade into haze.",
            4 => "One vast landmass spreads without a true far shore, carrying forests, plains, and mountains across an immense horizon.",
            _ => "Land overwhelms the sea here, with enormous interiors broken by rare coasts, inland waters, and long overland paths."
        };

        string elevationMod = elevation switch
        {
            0 => "Open ground leaves every movement visible beneath the wide sky.",
            1 => "Ridges and hill paths offer shelter, lookout points, and hidden crossings.",
            _ => "High peaks and narrow passes divide the wilderness into hard, isolated places."
        };

        return terrainBase + " " + elevationMod;
    }

    private static string GetWildlifePhrase(int climate, int animalPrevalence)
    {
        return animalPrevalence switch
        {
            0 => "The wild places are strangely quiet, shaped more by weather than by animal movement.",
            1 or 2 => "Only scattered tracks mark the passage of wary animals.",
            3 => "Deer, birds, predators, and smaller creatures move through the land in steady numbers.",
            4 => "Animal trails are common, and the land stirs with herds, birds, and hunters.",
            _ => "The wilderness is crowded with life, from great herds to predators that follow them through the dark."
        };
    }

    private static string GetHumanPresenceSentence(int peopleCount, int gatheringPlaceCount, int tribeCount)
    {
        string basePresence = peopleCount <= 2
            ? "Only a few scattered peoples move through this wide land, leaving most places to beasts, weather, and silence."
            : peopleCount <= 5
                ? "Several peoples already follow rivers, valleys, and open ground, their fires appearing wherever shelter is found."
                : "Many peoples share this land from the beginning, their trails, camps, and hunting grounds already crossing in the wild places.";

        string extra = "";
        if (tribeCount >= 4)
        {
            extra = "Tribal bands are common here, their camps and remembered paths woven deeply into the wilderness.";
        }
        else if (tribeCount >= 1)
        {
            extra = "A few wandering tribes keep to the margins, following older paths through forest, hill, and marsh.";
        }
        else if (gatheringPlaceCount >= 4)
        {
            extra = "Many independent gathering places mark the easier ground, drawing travelers, stories, and smoke from the surrounding wilds.";
        }
        else if (gatheringPlaceCount >= 1)
        {
            extra = "A few independent gathering places stand near water and stone, small lights in a much larger wilderness.";
        }

        return string.IsNullOrEmpty(extra) ? basePresence : basePresence + " " + extra;
    }

    private static string GetSpecialWorldModifier(string mapTypeName)
    {
        if (string.IsNullOrEmpty(mapTypeName))
            return string.Empty;

        if (mapTypeName.IndexOf("Rivers", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return "Great rivers carry life through the land, cutting paths that animals and people follow.";
        if (mapTypeName.IndexOf("Rainforest", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return "The canopy closes overhead, turning every sound into a warning.";
        if (mapTypeName.IndexOf("Infernal", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return "Smoke, vents, and hot stone make the land feel alive beneath the feet.";
        if (mapTypeName.IndexOf("Demonic", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return "The wilderness feels wrong here, as if shadow and fire have taken root in the soil.";
        if (mapTypeName.IndexOf("Scorched", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return "Ash drifts across the open ground, and the sun leaves little untouched.";
        if (mapTypeName.IndexOf("Ice", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            mapTypeName.IndexOf("Icicle", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
            mapTypeName.IndexOf("Cryo", System.StringComparison.OrdinalIgnoreCase) >= 0)
            return "Crystal forests and hard blue ice give the cold a strange, beautiful shape.";

        return string.Empty;
    }

    // Compatibility wrapper with prehistoric tone
    private static string GenerateGeopoliticalDescription(int civCount, int cityStateCount, int tribeCount, int climate, int moisture, int landType, int elevation)
    {
        return GetHumanPresenceSentence(civCount, cityStateCount, tribeCount);
    }

// Helper method to convert numbers to words
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
}
