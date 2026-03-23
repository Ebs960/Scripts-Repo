using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Legacy is a long-term bonus earned by completing missions.
/// Civilizations can earn many but only promote a limited number at a time.
/// Promoted legacies apply stat bonuses and shift governor/vassal opinions on policies and governments.
/// </summary>
[CreateAssetMenu(fileName = "New Legacy", menuName = "Data/Legacy Data")]
public class LegacyData : ScriptableObject
{
    [Header("Identity")]
    public string legacyName;
    public Sprite icon;
    [TextArea(3, 6)]
    public string description;
    [TextArea(4, 10)]
    public string flavorText;
    public Sprite bannerImage;

    [Header("Promotion Cost")]
    [Tooltip("Policy points required to promote this legacy into an active slot")]
    public int policyPointCost;
    [Tooltip("Gold required to promote this legacy into an active slot")]
    public int goldCost;

    [Header("Stat Bonuses (while promoted)")]
    public float attackBonus;
    public float defenseBonus;
    public float movementBonus;
    public float foodModifier;
    public float productionModifier;
    public float goldModifier;
    public float scienceModifier;
    public float cultureModifier;
    public float faithModifier;

    [Header("Governor & Vassal Influence")]
    [Tooltip("How this legacy shifts opinions on specific policies")]
    public PolicyBias[] policyBiases;

    [Tooltip("How this legacy shifts opinions on specific governments")]
    public GovernmentBias[] governmentBiases;

    [Tooltip("Personality-based multipliers on the above biases")]
    public PersonalityBiasMultiplier[] personalityMultipliers;

    // ─────────────────────────────────────────────
    //  Inner types (keeps everything in one file)
    // ─────────────────────────────────────────────

    [System.Serializable]
    public class PolicyBias
    {
        public PolicyData policy;
        [Range(-50f, 50f)]
        [Tooltip("+ opinion if this policy is active, − if it's absent (or vice versa)")]
        public float opinionModifier;
        [TextArea(1, 3)]
        public string reason;
    }

    [System.Serializable]
    public class GovernmentBias
    {
        public GovernmentData government;
        [Range(-50f, 50f)]
        public float opinionModifier;
        [TextArea(1, 3)]
        public string reason;
    }

    [System.Serializable]
    public class PersonalityBiasMultiplier
    {
        public PersonalityTrait trait;
        [Tooltip("Multiplier on all opinion biases for governors with this trait. 1.5 = 50% stronger, 0.5 = 50% weaker")]
        public float multiplier = 1f;
    }
}
