using System;
using System.Text;
using System.Collections.Generic;
using System.Linq;

public static class CivDescriptionGenerator
{
    private static readonly System.Random rng = new System.Random();

    // Contextual city count templates
    private static readonly string[] cityTinyTemplates = new[] {
        "A humble people, their settlements are but a handful—just {0} cities clinging to survival.",
        "Their presence is faint, with only {0} cities dotting the map like scattered lanterns in the dark.",
        "With merely {0} cities, they are a minor note in the symphony of nations."
    };
    private static readonly string[] citySmallTemplates = new[] {
        "Their {0} cities form a modest realm, quietly enduring.",
        "A small but determined civilization, their {0} cities stand resilient.",
        "With {0} cities, they are neither forgotten nor feared—yet."
    };
    private static readonly string[] cityMediumTemplates = new[] {
        "Their {0} cities form a respectable domain, neither vast nor meager.",
        "A balanced power, their {0} cities anchor their ambitions.",
        "With {0} cities, they are a steady presence on the world stage."
    };
    private static readonly string[] cityLargeTemplates = new[] {
        "Their empire sprawls across the land, boasting {0} cities in a tapestry of power.",
        "With {0} cities, their reach is long and their shadow looms large.",
        "A mighty civilization, their {0} cities pulse with life and ambition."
    };
    private static readonly string[] cityHugeTemplates = new[] {
        "Their cities are beyond counting—an endless sprawl of civilization, with {0} shining metropolises and more rising each year.",
        "A true world-spanning empire, their {0} cities form a constellation of power across continents.",
        "Their dominion is legendary: {0} cities, each a jewel in their imperial crown."
    };

    // Contextual military templates
    private static readonly string[] militaryTinyTemplates = new[] {
        "Their armies are but a rumor, scarcely seen and easily forgotten.",
        "With so few soldiers, their banners rarely flutter on the battlefield.",
        "Their military is a mere formality, more parade than power."
    };
    private static readonly string[] militarySmallTemplates = new[] {
        "Their forces are modest, enough to defend but not to conquer.",
        "A small army stands ready, but their enemies do not tremble.",
        "Their soldiers are few, but their resolve is not in question."
    };
    private static readonly string[] militaryMediumTemplates = new[] {
        "Their military is balanced, prepared for both war and peace.",
        "A respectable force, their armies are neither feared nor dismissed.",
        "Their soldiers are well-drilled, ready for whatever may come."
    };
    private static readonly string[] militaryLargeTemplates = new[] {
        "Their armies are the stuff of legend, marching in endless columns.",
        "A juggernaut of war, their military casts a long shadow.",
        "Their soldiers are many, and their enemies whisper in fear."
    };
    private static readonly string[] militaryHugeTemplates = new[] {
        "Their legions are uncountable, a tidal wave of steel and discipline that sweeps aside all opposition.",
        "No force on earth rivals their military might—an unstoppable war machine that reshapes the world.",
        "Their armies are a myth made real: {0} strong, their banners blot out the sun."
    };

    // Contextual intro templates
    private static readonly string[] introTinyTemplates = new[] {
        "Few have heard of the {0}, a people whose story is just beginning.",
        "The {0} are a quiet presence, their name barely echoing beyond their borders.",
        "In the margins of history, the {0} strive to be remembered."
    };
    private static readonly string[] introSmallTemplates = new[] {
        "The {0} are a modest nation, their ambitions growing with each sunrise.",
        "Though not mighty, the {0} are determined to carve their place in the world.",
        "The {0} are a rising people, their story unfolding with quiet determination."
    };
    private static readonly string[] introMediumTemplates = new[] {
        "The {0} are a steady force, respected by friend and foe alike.",
        "Across the world, the {0} are known for their balance of power and wisdom.",
        "The {0} have earned their place among the nations of the world."
    };
    private static readonly string[] introLargeTemplates = new[] {
        "The {0} are a colossus, their name spoken with awe and envy.",
        "Few can match the might of the {0}, whose influence shapes continents.",
        "The {0} stand as a beacon of civilization, their legacy already legendary."
    };
    private static readonly string[] introHugeTemplates = new[] {
        "The {0} are the architects of an age, their civilization a legend in its own time.",
        "The world bows before the {0}, whose empire is the very definition of greatness.",
        "The {0} are the stuff of myth, their deeds echoing across the ages."
    };

    // Contextual closing templates
    private static readonly string[] closingTinyTemplates = new[] {
        "Their future is uncertain, but hope flickers in their hearts.",
        "Perhaps one day, their name will be known beyond their borders.",
        "For now, they watch and wait, dreaming of greatness."
    };
    private static readonly string[] closingSmallTemplates = new[] {
        "Their journey is just beginning, and the world watches with curiosity.",
        "Allies and rivals alike wonder what heights they may yet reach.",
        "Their next chapter is unwritten, but ambition stirs within."
    };
    private static readonly string[] closingMediumTemplates = new[] {
        "Their saga continues, a tale of balance and resolve.",
        "The world respects their name, and their story is far from over.",
        "In the great game of nations, they are a player to be reckoned with."
    };
    private static readonly string[] closingLargeTemplates = new[] {
        "Their legend grows with every passing year, and all take notice.",
        "Few dare challenge their might, and many seek their favor.",
        "Their destiny is greatness, and the world holds its breath."
    };
    private static readonly string[] closingHugeTemplates = new[] {
        "Their story is the story of the world itself, and all others are but footnotes.",
        "Their legacy is immortal, their name etched into the bones of history.",
        "The world is theirs, and the future bends to their will."
    };

    // Other templates (unchanged)
    private static readonly string[] governmentTemplates = new[] {
        "They are governed by the principles of {0}, shaping every law and custom.",
        "Their society is structured as a {0}, with all the strengths and quirks that entails.",
        "The {0} government guides their destiny, for better or worse.",
        "Their people live under the rule of {0}, a system both ancient and evolving.",
        "At the heart of their civilization lies the {0}, a beacon for their people."
    };
    private static readonly string[] cultureTemplates = new[] {
        "Steeped in the traditions of the {0} world, their culture is a tapestry of old and new.",
        "Their {0} heritage colors every festival and council.",
        "The {0} spirit runs deep in their veins, inspiring art and ambition alike.",
        "From music to philosophy, the {0} influence is unmistakable.",
        "Their culture, rooted in the {0} way, is both shield and sword."
    };
    private static readonly string[] leaderTemplates = new[] {
        "{0} is a leader {1}, whose legend grows with every passing year.",
        "Stories abound of {0}, a ruler {1}.",
        "Under {0}'s {1} leadership, the nation has flourished and faltered in equal measure.",
        "{0}, {1}, stands at the helm, steering their people through calm and storm.",
        "The world watches {0}, a leader {1}, with a mix of awe and apprehension."
    };

    // Tech age flavor lines
    private static readonly Dictionary<TechAge, string> techAgeFlavors = new Dictionary<TechAge, string> {
        { TechAge.PrehistoricAge, "They dwell in the Prehistoric Age, where fire and stone shape their destiny." },
        { TechAge.TribalAge, "They are in the Tribal Age, bound by kinship and ancient rites." },
        { TechAge.IceAge, "They endure the Ice Age, surviving in a world of frost and hardship." },
        { TechAge.VillageAge, "They thrive in the Village Age, where small communities blossom." },
        { TechAge.MonumentAge, "They build wonders in the Monument Age, etching their mark in stone." },
        { TechAge.CopperAge, "They wield copper tools, forging ahead in the Copper Age." },
        { TechAge.BronzeAge, "They are in the Bronze Age, where empires rise and fall on the strength of metal." },
        { TechAge.IronAge, "They march through the Iron Age, their weapons sharp and ambitions sharper." },
        { TechAge.ClassicalAge, "They flourish in the Classical Age, a time of philosophy and conquest." },
        { TechAge.AxialAge, "They ponder the mysteries of the Axial Age, seeking wisdom and unity." },
        { TechAge.MigrationAge, "They wander in the Migration Age, reshaping the world with every step." },
        { TechAge.DarkAge, "They struggle through the Dark Age, where hope flickers but never dies." },
        { TechAge.FeudalAge, "They are entrenched in the Feudal Age, ruled by lords and bound by oaths." },
        { TechAge.CastleAge, "They fortify in the Castle Age, their walls tall and their resolve taller." },
        { TechAge.RenaissanceAge, "They awaken in the Renaissance Age, where art and science bloom." },
        { TechAge.ColonialAge, "They expand in the Colonial Age, their ships seeking distant shores." },
        { TechAge.ReformationAge, "They question and reform in the Reformation Age, faith and reason in conflict." },
        { TechAge.EnlightenmentAge, "They shine in the Enlightenment Age, where knowledge is power." },
        { TechAge.RevolutionAge, "They rise in the Revolution Age, toppling old orders for new dreams." },
        { TechAge.SteamAge, "They roar into the Steam Age, engines churning and cities rising." },
        { TechAge.RailroadAge, "They connect the world in the Railroad Age, iron rails binding continents." },
        { TechAge.ImperialAge, "They dominate in the Imperial Age, their banners flying over distant lands." },
        { TechAge.ModernAge, "They innovate in the Modern Age, where progress is relentless." },
        { TechAge.AtomicAge, "They wield the power of the atom in the Atomic Age, for good or ill." },
        { TechAge.InformationAge, "They thrive in the Information Age, where data is the new gold." },
        { TechAge.NanoAge, "They manipulate the very fabric of matter in the Nano Age." },
        { TechAge.MutantAge, "They adapt in the Mutant Age, where change is the only constant." },
        { TechAge.SolarAge, "They harness the stars in the Solar Age, basking in endless energy." },
        { TechAge.AquarianAge, "They explore the depths in the Aquarian Age, mastering water and wave." },
        { TechAge.PlasmaAge, "They command the Plasma Age, where science borders on magic." },
        { TechAge.InterstellarAge, "They voyage in the Interstellar Age, reaching for distant suns." },
        { TechAge.GalacticAge, "They reign in the Galactic Age, their influence spanning the stars." }
    };

    public static string GenerateDescription(Civilization civ, Civilization viewer)
    {
        if (civ == null || civ.civData == null) return "Unknown civilization.";
        var sb = new StringBuilder();
        var allCivs = CivilizationManager.Instance.GetAllCivs();
        if (allCivs == null || allCivs.Count == 0) allCivs = new List<Civilization> { civ };

        // --- City count context ---
        int cityCount = civ.cities.Count;
        double avgCities = allCivs.Average(c => c.cities.Count);
        int maxCities = allCivs.Max(c => c.cities.Count);
        int minLargeCities = 7;
        int minHugeCities = 12;
        string[] cityTemplates, introTemplates, closingTemplates;
        if (cityCount >= Math.Max(minHugeCities, avgCities * 2.0)) { cityTemplates = cityHugeTemplates; introTemplates = introHugeTemplates; closingTemplates = closingHugeTemplates; }
        else if (cityCount >= Math.Max(minLargeCities, avgCities * 1.3) || cityCount == maxCities) { cityTemplates = cityLargeTemplates; introTemplates = introLargeTemplates; closingTemplates = closingLargeTemplates; }
        else if (cityCount <= Math.Max(2, avgCities * 0.5)) { cityTemplates = cityTinyTemplates; introTemplates = introTinyTemplates; closingTemplates = closingTinyTemplates; }
        else if (cityCount <= Math.Max(4, avgCities * 0.85)) { cityTemplates = citySmallTemplates; introTemplates = introSmallTemplates; closingTemplates = closingSmallTemplates; }
        else { cityTemplates = cityMediumTemplates; introTemplates = introMediumTemplates; closingTemplates = closingMediumTemplates; }

        // --- Military context ---
        var mil = CivilizationManager.Instance.ComputeMilitaryStrength(civ);
        double avgMil = allCivs.Average(c => CivilizationManager.Instance.ComputeMilitaryStrength(c));
        double maxMil = allCivs.Max(c => CivilizationManager.Instance.ComputeMilitaryStrength(c));
        int minLargeMil = 120;
        int minHugeMil = 250;
        string[] militaryTemplates;
        if (mil >= Math.Max(minHugeMil, avgMil * 2.0)) militaryTemplates = militaryHugeTemplates;
        else if (mil >= Math.Max(minLargeMil, avgMil * 1.3) || mil == maxMil) militaryTemplates = militaryLargeTemplates;
        else if (mil <= Math.Max(30, avgMil * 0.5)) militaryTemplates = militaryTinyTemplates;
        else if (mil <= Math.Max(80, avgMil * 0.85)) militaryTemplates = militarySmallTemplates;
        else militaryTemplates = militaryMediumTemplates;

        // --- Economy context ---
        int gold = 0;
        foreach (var city in civ.cities) gold += city.GetGoldPerTurn();
        double avgGold = allCivs.Average(c => c.cities.Sum(city => city.GetGoldPerTurn()));
        int minLargeGold = 100;
        int minHugeGold = 200;
        string econDesc = gold >= Math.Max(minHugeGold, avgGold * 2.0) ? "legendary and inexhaustible" : gold >= Math.Max(minLargeGold, avgGold * 1.3) ? "overflowing with riches" : gold <= avgGold * 0.5 ? "scraping by on meager means" : gold <= avgGold * 0.85 ? "modest and careful" : "steady and reliable";

        // --- Tech Age flavor ---
        string techAgeLine = "";
        if (civ.currentTech != null)
        {
            if (techAgeFlavors.TryGetValue(civ.currentTech.techAge, out var flavor))
                techAgeLine = flavor;
            else
                techAgeLine = $"They are in the {civ.currentTech.techAge}, forging their own path.";
        }
        else
        {
            // Fallback: use most recent researched tech's age if available
            var lastTech = civ.researchedTechs.LastOrDefault();
            if (lastTech != null && techAgeFlavors.TryGetValue(lastTech.techAge, out var flavor))
                techAgeLine = flavor;
        }

        // --- Compose description ---
        bool hasCustomDesc = !string.IsNullOrWhiteSpace(civ.civData.description);
        if (hasCustomDesc)
        {
            sb.AppendLine($"{civ.civData.description.Trim()}");
        }
        else
        {
            sb.AppendLine(string.Format(Pick(introTemplates), civ.civData.civName));
        }
        // Tech Age
        if (!string.IsNullOrEmpty(techAgeLine))
            sb.AppendLine(techAgeLine);
        // Leader
        string leaderTrait = DescribeLeaderPersonality(civ.leader);
        sb.AppendLine(string.Format(Pick(leaderTemplates), civ.leader.leaderName, leaderTrait));
        // Government
        if (civ.currentGovernment != null)
            sb.AppendLine(string.Format(Pick(governmentTemplates), civ.currentGovernment.governmentName));
        // Culture
        sb.AppendLine(string.Format(Pick(cultureTemplates), civ.civData.cultureGroup));
        // City count
        sb.AppendLine(string.Format(Pick(cityTemplates), cityCount));
        // Military
        sb.AppendLine(Pick(militaryTemplates).Replace("{0}", mil.ToString()));
        // Economy
        sb.AppendLine($"Their economy is {econDesc}.");
        // Closing
        sb.AppendLine(Pick(closingTemplates));
        return sb.ToString().Trim();
    }

    private static string Pick(string[] arr) => arr[rng.Next(arr.Length)];

    private static string DescribeLeaderPersonality(LeaderData leader)
    {
        if (leader == null) return "of enigmatic character";
        var traits = new List<string>();
        if (leader.isWarmonger) traits.Add("with a fire for conquest");
        if (leader.prefersAlliance) traits.Add("who weaves alliances like a master diplomat");
        if (leader.prefersTrade) traits.Add("whose markets never sleep");
        if (leader.isIsolationist) traits.Add("who guards their borders with silent resolve");
        if (traits.Count == 0)
        {
            // Use stat-based flavor
            if (leader.aggressiveness >= 8) traits.Add("whose ambition knows no bounds");
            if (leader.diplomacy >= 8) traits.Add("whose silver tongue charms friend and foe alike");
            if (leader.science >= 8) traits.Add("whose mind is ever on the next discovery");
            if (leader.expansion >= 8) traits.Add("who dreams of empires without end");
            if (leader.religion >= 8) traits.Add("whose faith shapes the soul of the nation");
        }
        if (traits.Count == 0) traits.Add("of many talents and mysteries");
        return string.Join(", ", traits);
    }

    public static string GenerateDescription(CivData civData, LeaderData leaderData)
    {
        var sb = new System.Text.StringBuilder();

        // Use a generic intro as we don't have live game data
        sb.AppendLine($"The {civData.civName} are a notable civilization.");

        if (leaderData != null)
        {
            sb.AppendLine($"They are often led by {leaderData.leaderName}, known for their {leaderData.primaryAgenda.ToString().ToLower()} approach.");
        }
        else if (civData.availableLeaders != null && civData.availableLeaders.Count > 0)
        {
            sb.AppendLine($"They can be led by figures such as {civData.availableLeaders[0].leaderName}.");
        }

        // General bonuses
        var bonuses = new List<string>();
        if (civData.productionModifier > 0) bonuses.Add("production");
        if (civData.goldModifier > 0) bonuses.Add("wealth");
        if (civData.scienceModifier > 0) bonuses.Add("innovation");
        if (civData.cultureModifier > 0) bonuses.Add("cultural pursuits");
        if (civData.faithModifier > 0) bonuses.Add("faith");

        if (bonuses.Count > 0)
        {
            sb.Append($"Their people are known for their focus on {string.Join(", ", bonuses)}.");
        }

        return sb.ToString();
    }
} 