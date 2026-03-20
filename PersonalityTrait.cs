/// <summary>
/// CK-lite personality traits for governors. Each governor gets 2-3 of these
/// at creation. They drive the opinion system and influence event generation.
/// Separate from GovernorTrait (which are unlockable skill-like bonuses).
/// </summary>
public enum PersonalityTrait
{
    Loyal,       // +20 base opinion, less likely to rebel
    Ambitious,   // -10 base opinion, schemes for power
    Generous,    // Responds well to gifts (+50% gift effect)
    Greedy,      // Wants more gold, -opinion if taxes are high
    Brave,       // Full levy contribution, won't back down
    Craven,      // Reduced levy, but complies with threats
    Honest,      // Detects plots, won't join schemes
    Deceitful,   // Schemes more, harder to detect
    Zealous,     // +opinion if same religion, -opinion if not
    Cynical,     // Ignores religious matters
    Content,     // +15 base opinion, never schemes
    Cruel        // Intimidates neighbors, -opinion from other governors
}

/// <summary>
/// A tracked reason for opinion change. Decays over time unless permanent.
/// </summary>
[System.Serializable]
public struct OpinionModifier
{
    public string reason;
    public float value;
    public int turnsRemaining; // -1 = permanent, 0 = expired (remove next tick)

    public OpinionModifier(string reason, float value, int duration = -1)
    {
        this.reason = reason;
        this.value = value;
        this.turnsRemaining = duration;
    }
}
