using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages legacy promotion/demotion for civilizations, applies stat bonuses,
/// and injects governor/vassal opinion modifiers based on active legacies and policies.
/// </summary>
public class LegacyManager : MonoBehaviour
{
    public static LegacyManager Instance { get; private set; }

    [Header("All Legacies")]
    [Tooltip("Master list of every legacy in the game")]
    public List<LegacyData> allLegacies = new List<LegacyData>();

    /// <summary>Fired when a legacy is earned from a mission.</summary>
    public event Action<Civilization, LegacyData> OnLegacyEarned;
    /// <summary>Fired when a legacy is promoted into an active slot.</summary>
    public event Action<Civilization, LegacyData> OnLegacyPromoted;
    /// <summary>Fired when a legacy is demoted from an active slot.</summary>
    public event Action<Civilization, LegacyData> OnLegacyDemoted;

    private const string LEGACY_OPINION_PREFIX = "[Legacy] ";

    private bool subscribedToTurnManager;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        TrySubscribeToTurnManager();
    }

    void Update()
    {
        if (!subscribedToTurnManager) TrySubscribeToTurnManager();
    }

    void OnDestroy()
    {
        if (subscribedToTurnManager && TurnManager.Instance != null)
            TurnManager.Instance.OnCivTurnStarting -= HandleCivTurnStarting;
    }

    private void TrySubscribeToTurnManager()
    {
        if (subscribedToTurnManager || TurnManager.Instance == null) return;
        TurnManager.Instance.OnCivTurnStarting += HandleCivTurnStarting;
        subscribedToTurnManager = true;
    }

    // ─── Public API ───

    /// <summary>
    /// Award a legacy to a civilization (earned from mission completion).
    /// Does not promote it automatically — the player must spend resources to promote.
    /// </summary>
    public void AwardLegacy(Civilization civ, LegacyData legacy)
    {
        if (civ == null || legacy == null) return;
        if (civ.earnedLegacies.Contains(legacy)) return;

        civ.earnedLegacies.Add(legacy);
        OnLegacyEarned?.Invoke(civ, legacy);
        Debug.Log($"[LegacyManager] {civ.civData?.civName} earned legacy '{legacy.legacyName}'");
    }

    /// <summary>
    /// Promote a legacy into an active slot. Costs gold + policy points.
    /// Returns false if the civ can't afford it or has no free slots.
    /// </summary>
    public bool PromoteLegacy(Civilization civ, LegacyData legacy)
    {
        if (civ == null || legacy == null) return false;
        if (!civ.earnedLegacies.Contains(legacy)) return false;
        if (civ.activeLegacies.Contains(legacy)) return false;
        if (civ.activeLegacies.Count >= civ.maxActiveLegacies) return false;
        if (civ.gold < legacy.goldCost) return false;
        if (civ.policyPoints < legacy.policyPointCost) return false;

        // Deduct costs
        civ.gold -= legacy.goldCost;
        civ.policyPoints -= legacy.policyPointCost;

        // Add to active and apply bonuses
        civ.activeLegacies.Add(legacy);
        ApplyLegacyBonuses(civ, legacy);
        RefreshGovernorOpinions(civ);

        OnLegacyPromoted?.Invoke(civ, legacy);
        Debug.Log($"[LegacyManager] {civ.civData?.civName} promoted legacy '{legacy.legacyName}'");
        return true;
    }

    /// <summary>
    /// Demote a legacy from an active slot. Frees the slot but does not refund costs.
    /// </summary>
    public bool DemoteLegacy(Civilization civ, LegacyData legacy)
    {
        if (civ == null || legacy == null) return false;
        if (!civ.activeLegacies.Contains(legacy)) return false;

        civ.activeLegacies.Remove(legacy);
        RemoveLegacyBonuses(civ, legacy);
        RefreshGovernorOpinions(civ);

        OnLegacyDemoted?.Invoke(civ, legacy);
        Debug.Log($"[LegacyManager] {civ.civData?.civName} demoted legacy '{legacy.legacyName}'");
        return true;
    }

    /// <summary>Check if a civ can afford to promote a given legacy.</summary>
    public bool CanPromote(Civilization civ, LegacyData legacy)
    {
        if (civ == null || legacy == null) return false;
        if (!civ.earnedLegacies.Contains(legacy)) return false;
        if (civ.activeLegacies.Contains(legacy)) return false;
        if (civ.activeLegacies.Count >= civ.maxActiveLegacies) return false;
        if (civ.gold < legacy.goldCost) return false;
        if (civ.policyPoints < legacy.policyPointCost) return false;
        return true;
    }

    // ─── Stat bonuses ───

    private void ApplyLegacyBonuses(Civilization civ, LegacyData legacy)
    {
        // Flat/legacy bonuses (backwards-compatible)
        civ.attackBonus += legacy.attackBonus;
        civ.defenseBonus += legacy.defenseBonus;
        civ.movementBonus += legacy.movementBonus;
        // Percentage-style modifiers: legacy fields are fractional (0.1 = +10%).
        // Convert to the existing civ attack/defense/movement scale by multiplying by 100 so
        // that inspector/tooltip displays (which expect percent-like numbers) remain meaningful.
        civ.attackBonus += legacy.attackModifier * 100f;
        civ.defenseBonus += legacy.defenseModifier * 100f;
        civ.movementBonus += legacy.movementModifier * 100f;
        civ.foodModifier += legacy.foodModifier;
        civ.productionModifier += legacy.productionModifier;
        civ.goldModifier += legacy.goldModifier;
        civ.scienceModifier += legacy.scienceModifier;
        civ.cultureModifier += legacy.cultureModifier;
        civ.faithModifier += legacy.faithModifier;
    }

    private void RemoveLegacyBonuses(Civilization civ, LegacyData legacy)
    {
        // Remove flat/legacy bonuses
        civ.attackBonus -= legacy.attackBonus;
        civ.defenseBonus -= legacy.defenseBonus;
        civ.movementBonus -= legacy.movementBonus;
        // Remove percentage-style modifiers (converted the same way as when applied)
        civ.attackBonus -= legacy.attackModifier * 100f;
        civ.defenseBonus -= legacy.defenseModifier * 100f;
        civ.movementBonus -= legacy.movementModifier * 100f;
        civ.foodModifier -= legacy.foodModifier;
        civ.productionModifier -= legacy.productionModifier;
        civ.goldModifier -= legacy.goldModifier;
        civ.scienceModifier -= legacy.scienceModifier;
        civ.cultureModifier -= legacy.cultureModifier;
        civ.faithModifier -= legacy.faithModifier;
    }

    // ─── Governor opinion integration ───

    /// <summary>
    /// Recalculate all legacy-driven opinion modifiers for a civ's governors.
    /// Called when legacies or policies change.
    /// </summary>
    public void RefreshGovernorOpinions(Civilization civ)
    {
        if (civ == null || civ.governors == null) return;

        foreach (var governor in civ.governors)
        {
            if (governor == null) continue;

            // Remove old legacy opinion modifiers
            governor.OpinionModifiers.RemoveAll(m => m.reason.StartsWith(LEGACY_OPINION_PREFIX));

            // Apply biases from each active legacy
            foreach (var legacy in civ.activeLegacies)
            {
                if (legacy == null) continue;

                float personalityMult = GetPersonalityMultiplier(governor, legacy);

                // Policy biases
                if (legacy.policyBiases != null)
                {
                    foreach (var bias in legacy.policyBiases)
                    {
                        if (bias.policy == null) continue;

                        bool policyActive = civ.activePolicies.Contains(bias.policy);
                        // Positive bias = likes this policy. If active, positive opinion. If absent, negative.
                        float opinion = policyActive ? bias.opinionModifier : -bias.opinionModifier;
                        opinion *= personalityMult;

                        if (Mathf.Abs(opinion) > 0.01f)
                        {
                            string reason = LEGACY_OPINION_PREFIX + (string.IsNullOrEmpty(bias.reason)
                                ? $"{legacy.legacyName}: {bias.policy.policyName}"
                                : bias.reason);
                            governor.OpinionModifiers.Add(new OpinionModifier(reason, opinion, -1));
                        }
                    }
                }

                // Government biases
                if (legacy.governmentBiases != null)
                {
                    foreach (var bias in legacy.governmentBiases)
                    {
                        if (bias.government == null) continue;

                        bool isCurrentGov = civ.currentGovernment == bias.government;
                        float opinion = isCurrentGov ? bias.opinionModifier : -bias.opinionModifier;
                        opinion *= personalityMult;

                        if (Mathf.Abs(opinion) > 0.01f)
                        {
                            string reason = LEGACY_OPINION_PREFIX + (string.IsNullOrEmpty(bias.reason)
                                ? $"{legacy.legacyName}: {bias.government.governmentName}"
                                : bias.reason);
                            governor.OpinionModifiers.Add(new OpinionModifier(reason, opinion, -1));
                        }
                    }
                }
            }
        }
    }

    private float GetPersonalityMultiplier(Governor governor, LegacyData legacy)
    {
        if (legacy.personalityMultipliers == null || legacy.personalityMultipliers.Length == 0)
            return 1f;

        float mult = 1f;
        foreach (var pm in legacy.personalityMultipliers)
        {
            if (governor.HasPersonality(pm.trait))
                mult *= pm.multiplier;
        }
        return mult;
    }

    // ─── Turn hook: refresh opinions at start of each civ turn ───

    private void HandleCivTurnStarting(Civilization civ, int round)
    {
        if (civ == null || civ.activeLegacies == null || civ.activeLegacies.Count == 0) return;
        RefreshGovernorOpinions(civ);
    }
}
