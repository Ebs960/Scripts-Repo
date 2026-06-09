using UnityEngine;

/// <summary>
/// Defines a civilization leader with their unique traits and bonuses.
/// </summary>
[CreateAssetMenu(fileName = "NewLeaderData", menuName = "Data/Leader Data")]
public class LeaderData : ScriptableObject
{
    [Header("Basic Info")]
    public string leaderName;
    public Sprite portrait;
    public string historicalEra;
    [TextArea(3, 6)]
    public string biography;
    
    [Header("Leadership Style")]
    [Range(0, 10)]
    public int aggressiveness = 5;  // Affects likelihood to declare war
    [Range(0, 10)]
    public int diplomacy = 5;       // Affects willingness for treaties
    [Range(0, 10)]
    public int science = 5;         // Affects tech focus
    [Range(0, 10)]
    public int expansion = 5;       // Affects city placement
    [Range(0, 10)]
    public int religion = 5;        // Affects spiritual focus
    
    [Header("Strategic Agendas")]
    [Tooltip("Primary agenda that drives this leader's behavior")]
    public LeaderAgenda primaryAgenda = LeaderAgenda.Balanced;
    [Tooltip("Secondary agenda for more complex behavior")]
    public LeaderAgenda secondaryAgenda = LeaderAgenda.None;
    [Tooltip("What this leader likes to see in other civilizations")]
    public CivilizationTrait[] likesTraits;
    [Tooltip("What this leader dislikes in other civilizations")]
    public CivilizationTrait[] dislikesTraits;
    [Tooltip("Victory condition this leader prioritizes")]
    public VictoryType preferredVictory = VictoryType.Domination;
    
    [Header("Behavioral Modifiers")]
    [Range(0f, 2f)]
    [Tooltip("How much this leader values military strength (affects unit production priority)")]
    public float militaryFocus = 1f;
    [Range(0f, 2f)]
    [Tooltip("How much this leader values economic growth")]
    public float economicFocus = 1f;
    [Range(0f, 2f)]
    [Tooltip("How much this leader values technological advancement")]
    public float scientificFocus = 1f;
    [Range(0f, 2f)]
    [Tooltip("How much this leader values cultural development")]
    public float culturalFocus = 1f;
    [Range(0f, 2f)]
    [Tooltip("How much this leader values religious development")]
    public float religiousFocus = 1f;
    
    [Header("Unique Units")]
    [Tooltip("Leader-specific unique units that replace standard units")]
    public UniqueUnitDefinition[] uniqueUnits;
    
    [Header("Unique Buildings")]
    [Tooltip("Leader-specific unique buildings that replace standard buildings")]
    public UniqueBuildingDefinition[] uniqueBuildings;
    
    [Header("Leader Ability")]
    [Tooltip("The name of this leader's special ability")]
    public string abilityName;
    [TextArea(2, 4)]
    [Tooltip("Description of the special ability")]
    public string abilityDescription;
    
    // Global modifiers specific to this leader
    [Header("Leader Bonuses")]
    public float goldModifier = 0f;            // Percentage boost to gold income
    public float scienceModifier = 0f;         // Percentage boost to science output
    public float productionModifier = 0f;      // Percentage boost to production
    public float foodModifier = 0f;            // Percentage boost to food output
    public float cultureModifier = 0f;         // Percentage boost to culture output
    public float faithModifier = 0f;           // Percentage boost to faith output
    public float militaryStrengthModifier = 0f; // Percentage boost to all military attacks
    public float meleeAttackBonus = 0f;        // Percentage boost to melee attacks
    public float rangedAttackBonus = 0f;       // Percentage boost to ranged attacks
    public float cityAttackBonus = 0f;         // Percentage boost to city attacks

    [Header("Tile Yield Bonuses")]
    [Tooltip("Per-tile yield bonuses granted by this leader to matching worked city tiles.")]
    public TileYieldBonus[] tileYieldBonuses;

    [Tooltip("Per-herd per-turn yield bonuses granted by this leader (can filter by animal species).")]
    public HerdYieldBonus[] herdYieldBonuses;
    
    [Header("Diplomacy Visuals")]
    [Tooltip("Palace / throne-room background shown behind the leader during negotiations")]
    public Sprite background;
    [Tooltip("Animator controller for leader character (idle, speaking, angry, declareWar, happy, agreement triggers)")]
    public RuntimeAnimatorController leaderAnimator;

    [Header("Leader Mood Sprites (fallback when no Animator is set)")]
    [Tooltip("Idle / neutral pose")]
    public Sprite idleSprite;
    [Tooltip("Speaking during negotiations")]
    public Sprite speakingSprite;
    [Tooltip("Angry / displeased expression")]
    public Sprite angrySprite;
    [Tooltip("Declaring war dramatic pose")]
    public Sprite declareWarSprite;
    [Tooltip("Happy / pleased expression")]
    public Sprite happySprite;
    [Tooltip("Agreement / handshake pose")]
    public Sprite agreementSprite;

    [Header("Leader Mood GIFs (StreamingAssets relative paths)")]
    [Tooltip("Animated GIF shown for idle mood. Path relative to StreamingAssets, for example: LeaderGifs/Caesar/idle.gif")]
    public string idleGifPath;
    [Tooltip("Animated GIF shown for speaking mood. Path relative to StreamingAssets.")]
    public string speakingGifPath;
    [Tooltip("Animated GIF shown for angry mood. Path relative to StreamingAssets.")]
    public string angryGifPath;
    [Tooltip("Animated GIF shown for declare-war mood. Path relative to StreamingAssets.")]
    public string declareWarGifPath;
    [Tooltip("Animated GIF shown for happy mood. Path relative to StreamingAssets.")]
    public string happyGifPath;
    [Tooltip("Animated GIF shown for agreement mood. Path relative to StreamingAssets.")]
    public string agreementGifPath;

    [Header("Diplomatic Traits")]
    public bool prefersAlliance = false;       // More likely to accept alliances
    public bool prefersTrade = false;          // More likely to accept trade deals
    public bool isWarmonger = false;           // More likely to declare war
    public bool isIsolationist = false;        // Less likely to engage in diplomacy
    
    /// <summary>
    /// Get the diplomatic modifier for a specific trait
    /// </summary>
    public float GetTraitModifier(CivilizationTrait trait, bool isLiked)
    {
        if (isLiked && System.Array.Exists(likesTraits, t => t == trait))
            return 0.3f; // +30% diplomatic bonus
        else if (!isLiked && System.Array.Exists(dislikesTraits, t => t == trait))
            return -0.3f; // -30% diplomatic penalty
        return 0f;
    }

    /// <summary>
    /// Returns the sprite for the given mood.
    /// Falls back to portrait if no mood sprite is assigned.
    /// </summary>
    public Sprite GetMoodSprite(LeaderMood mood)
    {
        Sprite s = mood switch
        {
            LeaderMood.Idle       => idleSprite,
            LeaderMood.Speaking   => speakingSprite,
            LeaderMood.Angry      => angrySprite,
            LeaderMood.DeclareWar => declareWarSprite,
            LeaderMood.Happy      => happySprite,
            LeaderMood.Agreement  => agreementSprite,
            _ => null,
        };
        return s != null ? s : portrait;
    }

    /// <summary>
    /// Returns the StreamingAssets-relative GIF path for the given mood.
    /// Empty string means no GIF is configured for that mood.
    /// </summary>
    public string GetMoodGifPath(LeaderMood mood)
    {
        return mood switch
        {
            LeaderMood.Idle       => idleGifPath,
            LeaderMood.Speaking   => speakingGifPath,
            LeaderMood.Angry      => angryGifPath,
            LeaderMood.DeclareWar => declareWarGifPath,
            LeaderMood.Happy      => happyGifPath,
            LeaderMood.Agreement  => agreementGifPath,
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Returns the Animator trigger name for a mood.
    /// </summary>
    public static string GetAnimTrigger(LeaderMood mood)
    {
        return mood switch
        {
            LeaderMood.Idle       => "Idle",
            LeaderMood.Speaking   => "Speaking",
            LeaderMood.Angry      => "Angry",
            LeaderMood.DeclareWar => "DeclareWar",
            LeaderMood.Happy      => "Happy",
            LeaderMood.Agreement  => "Agreement",
            _ => "Idle",
        };
    }
    
    /// <summary>
    /// Get the priority weight for a specific focus area
    /// </summary>
    public float GetFocusPriority(FocusArea area)
    {
        return area switch
        {
            FocusArea.Military => militaryFocus,
            FocusArea.Economic => economicFocus,
            FocusArea.Scientific => scientificFocus,
            FocusArea.Cultural => culturalFocus,
            FocusArea.Religious => religiousFocus,
            _ => 1f
        };
    }
}

/// <summary>
/// Strategic agendas that drive AI behavior
/// </summary>
public enum LeaderAgenda
{
    None,
    Balanced,           // No strong preferences
    Militaristic,       // Focuses on military strength and conquest
    Expansionist,       // Wants to settle many cities
    Scientific,         // Prioritizes technology and research
    Cultural,           // Focuses on culture and great works
    Religious,          // Emphasizes faith and religious victory
    Economic,           // Prioritizes trade and gold generation
    Diplomatic,         // Seeks alliances and peaceful solutions
    Isolationist,       // Avoids foreign entanglements
    CityState,          // Protects city-states
    Wonder,             // Builds many wonders
    Naval,              // Emphasizes naval power and exploration
    Environmental       // Likes civs that preserve nature
}

/// <summary>
/// Traits that leaders can like or dislike in other civilizations
/// </summary>
public enum CivilizationTrait
{
    Warmonger,          // Frequently declares war
    Peaceful,           // Rarely goes to war
    Expansionist,       // Settles many cities
    Isolationist,       // Few diplomatic relationships
    Religious,          // Has founded a religion
    Scientific,         // Advanced in technology
    Cultural,           // High culture output
    Wealthy,            // High gold income
    Military,           // Large military
    Wonder,             // Builds many wonders
    CityStateAlly,      // Allied with city-states
    Trader,             // Active in trade
    Environmental       // Preserves natural features
}

/// <summary>
/// Victory types that leaders can prioritize
/// </summary>
public enum VictoryType
{
    Domination,         // Conquer all capitals
    Science,            // Complete space projects
    Culture,            // Achieve cultural dominance
    Religious,          // Convert the world
    Diplomatic,         // Win through alliances and votes
    Economic            // Accumulate vast wealth
}

/// <summary>
/// Mood / animation state for a leader during diplomacy.
/// </summary>
public enum LeaderMood
{
    Idle,
    Speaking,
    Angry,
    DeclareWar,
    Happy,
    Agreement
}

/// <summary>
/// Focus areas for AI decision making
/// </summary>
public enum FocusArea
{
    Military,
    Economic,
    Scientific,
    Cultural,
    Religious
}

/// <summary>
/// Defines a unique unit that replaces a standard unit.
/// </summary>
[System.Serializable]
public class UniqueUnitDefinition
{
    [Tooltip("The unique unit data asset")]
    public CombatUnitData uniqueUnit;
    
    [Tooltip("The standard unit this replaces")]
    public CombatUnitData replacesUnit;
    
    [TextArea(2, 4)]
    [Tooltip("Description of what makes this unit special")]
    public string specialFeature;
}

/// <summary>
/// Defines a unique building that replaces a standard building.
/// </summary>
[System.Serializable]
public class UniqueBuildingDefinition
{
    [Tooltip("The unique building data asset")]
    public BuildingData uniqueBuilding;
    
    [Tooltip("The standard building this replaces")]
    public BuildingData replacesBuilding;
    
    [TextArea(2, 4)]
    [Tooltip("Description of what makes this building special")]
    public string specialFeature;
} 