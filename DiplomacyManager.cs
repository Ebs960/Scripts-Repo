using System;
using System.Collections.Generic;
using UnityEngine;

// DiplomaticState enum already defined in Civilization.cs, removing duplicate

public enum DealType
{
    Peace,
    War,
    Trade,
    Alliance,
    Vassal
}

// NEW: Diplomatic memory system
[System.Serializable]
public class DiplomaticMemory
{
    public Dictionary<Civilization, List<DiplomaticEvent>> events = new Dictionary<Civilization, List<DiplomaticEvent>>();
    public Dictionary<Civilization, float> reputation = new Dictionary<Civilization, float>(); // -100 to +100
    public Dictionary<Civilization, int> trustLevel = new Dictionary<Civilization, int>(); // 0-10
    
    public void RecordEvent(Civilization other, DiplomaticEventType eventType, int severity = 1)
    {
        if (!events.ContainsKey(other))
            events[other] = new List<DiplomaticEvent>();
            
        events[other].Add(new DiplomaticEvent
        {
            eventType = eventType,
            severity = severity,
            turnOccurred = TurnManager.Instance?.round ?? 0
        });
        
        // Update reputation based on event
        if (!reputation.ContainsKey(other))
            reputation[other] = 0f;
            
        float reputationChange = GetReputationChange(eventType, severity);
        reputation[other] = Mathf.Clamp(reputation[other] + reputationChange, -100f, 100f);
        
        // Update trust level
        UpdateTrustLevel(other);
    }
    
    private float GetReputationChange(DiplomaticEventType eventType, int severity)
    {
        return eventType switch
        {
            DiplomaticEventType.BrokePeace => -20f * severity,
            DiplomaticEventType.DeclaredWar => -15f * severity,
            DiplomaticEventType.Denounced => -10f * severity,
            DiplomaticEventType.RefusedTrade => -2f * severity,
            DiplomaticEventType.AcceptedAlliance => 15f * severity,
            DiplomaticEventType.HonoredTreaty => 5f * severity,
            DiplomaticEventType.SharedInformation => 3f * severity,
            DiplomaticEventType.ProvidedAid => 10f * severity,
            DiplomaticEventType.AttackedAlly => -25f * severity,
            _ => 0f
        };
    }
    
    private void UpdateTrustLevel(Civilization other)
    {
        float rep = reputation.GetValueOrDefault(other, 0f);
        int newTrust = rep switch
        {
            >= 80f => 10,
            >= 60f => 8,
            >= 40f => 7,
            >= 20f => 6,
            >= 0f => 5,
            >= -20f => 4,
            >= -40f => 3,
            >= -60f => 2,
            >= -80f => 1,
            _ => 0
        };
        
        trustLevel[other] = newTrust;
    }
    
    public float GetReputation(Civilization other) => reputation.GetValueOrDefault(other, 0f);
    public int GetTrustLevel(Civilization other) => trustLevel.GetValueOrDefault(other, 5);
    
    public bool HasRecentEvent(Civilization other, DiplomaticEventType eventType, int withinTurns = 10)
    {
        if (!events.ContainsKey(other)) return false;
        
        int currentTurn = TurnManager.Instance?.round ?? 0;
        foreach (var evt in events[other])
        {
            if (evt.eventType == eventType && (currentTurn - evt.turnOccurred) <= withinTurns)
                return true;
        }
        return false;
    }
}

[System.Serializable]
public class DiplomaticEvent
{
    public DiplomaticEventType eventType;
    public int severity;
    public int turnOccurred;
}

public enum DiplomaticEventType
{
    BrokePeace,
    DeclaredWar,
    Denounced,
    RefusedTrade,
    AcceptedAlliance,
    HonoredTreaty,
    SharedInformation,
    ProvidedAid,
    AttackedAlly,
    SpiedOn,
    TradedWithEnemy
}

public class DiplomacyManager : MonoBehaviour
{
    public static DiplomacyManager Instance { get; private set; }

    // relations[a][b] == state of 'a' toward 'b'
    private Dictionary<Civilization, Dictionary<Civilization, DiplomaticState>> relations =
        new Dictionary<Civilization, Dictionary<Civilization, DiplomaticState>>();

    // NEW: Each civ has diplomatic memory of others
    private Dictionary<Civilization, DiplomaticMemory> diplomaticMemories = 
        new Dictionary<Civilization, DiplomaticMemory>();

    /// <summary>
    /// Fires when any two civs change their mutual state.
    /// </summary>
    public event Action<Civilization, Civilization, DiplomaticState> OnDiplomacyChanged;
    
    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        // Initialize empty dictionaries for every civ
        foreach (var civ in CivilizationManager.Instance.GetAllCivs())
        {
            relations[civ] = new Dictionary<Civilization, DiplomaticState>();
            diplomaticMemories[civ] = new DiplomaticMemory();
            
            foreach (var other in CivilizationManager.Instance.GetAllCivs())
            {
                if (other == civ) continue;
                relations[civ][other] = DiplomaticState.Peace;
            }
        }
    }

    /// <summary>
    /// What is the current state between A and B (from A's perspective)?
    /// </summary>
    public DiplomaticState GetRelationship(Civilization a, Civilization b)
    {
        if (relations.TryGetValue(a, out var map) && map.TryGetValue(b, out var state))
            return state;
        return DiplomaticState.Peace;
    }

    /// <summary>
    /// Get diplomatic memory for a civilization
    /// </summary>
    public DiplomaticMemory GetDiplomaticMemory(Civilization civ)
    {
        if (!diplomaticMemories.ContainsKey(civ))
            diplomaticMemories[civ] = new DiplomaticMemory();
        return diplomaticMemories[civ];
    }

    /// <summary>
    /// Propose a deal from 'from' to 'to'.  
    /// If 'to' is AI, auto-evaluate; if 'to' is player, fire UI event.
    /// </summary>
    public void ProposeDeal(Civilization from, Civilization to, DealType deal)
    {
        // Already in that state?
        if (GetRelationship(from, to).ToString() == deal.ToString())
            return;

        // If 'to' is not player: enhanced AI evaluation
        if (!to.isPlayerControlled)
        {
            bool accept = EvaluateProposalAI(to, from, deal);
            if (accept) 
            {
                ExecuteDeal(from, to, deal);
                // Record positive diplomatic event
                GetDiplomaticMemory(to).RecordEvent(from, DiplomaticEventType.AcceptedAlliance);
            }
            else
            {
                // Record refusal
                GetDiplomaticMemory(to).RecordEvent(from, DiplomaticEventType.RefusedTrade);
            }
            return;
        }

        // Otherwise, let UI listen for this event and pop up accept/decline
        OnDiplomacyChanged?.Invoke(from, to, (DiplomaticState)(-1)); 
        // -1 indicates "proposal pending" in UI logic
    }

    /// <summary>
    /// Call this from your UI once the player accepts or rejects a proposal.
    /// </summary>
    public void ResolveProposal(Civilization from, Civilization to, DealType deal, bool accepted)
    {
        if (accepted) 
        {
            ExecuteDeal(from, to, deal);
            GetDiplomaticMemory(from).RecordEvent(to, DiplomaticEventType.AcceptedAlliance);
        }
        else
        {
            GetDiplomaticMemory(from).RecordEvent(to, DiplomaticEventType.RefusedTrade);
        }
    }

    /// <summary>
    /// Actually enact the deal: update both sides' state (symmetric).
    /// </summary>
    private void ExecuteDeal(Civilization a, Civilization b, DealType deal)
    {
        var newState = (DiplomaticState)Enum.Parse(typeof(DiplomaticState), deal.ToString());

        relations[a][b] = newState;
        relations[b][a] = newState;

        OnDiplomacyChanged?.Invoke(a, b, newState);
        OnDiplomacyChanged?.Invoke(b, a, newState);
    }

    /// <summary>
    /// Enhanced AI logic with personality, memory, and situational awareness
    /// </summary>
    private bool EvaluateProposalAI(Civilization ai, Civilization other, DealType deal)
    {
        // Get leader personality from leadership traits if available
        float personalityFactor = 1.0f;
        if (ai.leader != null)
        {
            LeaderData leader = ai.leader;
            
            switch (deal)
            {
                case DealType.Peace:
                    // Less aggressive leaders more likely to accept peace
                    personalityFactor = 1.0f - (leader.aggressiveness / 10.0f) + (leader.diplomacy / 20.0f);
                    break;
                case DealType.War:
                    // More aggressive leaders more likely to accept war
                    personalityFactor = leader.aggressiveness / 10.0f;
                    break;
                case DealType.Alliance:
                    // Alliance-preferring leaders more likely to accept
                    personalityFactor = leader.diplomacy / 10.0f;
                    personalityFactor *= leader.prefersAlliance ? 1.5f : 1.0f;
                    personalityFactor *= leader.isIsolationist ? 0.5f : 1.0f;
                    break;
                case DealType.Trade:
                    // Trade-preferring leaders more likely to accept
                    personalityFactor = leader.diplomacy / 10.0f;
                    personalityFactor *= leader.prefersTrade ? 1.5f : 1.0f;
                    break;
                case DealType.Vassal:
                    // Isolationist leaders less likely to become vassals
                    personalityFactor = 1.0f - (leader.isIsolationist ? 0.3f : 0.0f);
                    break;
            }
        }

        // NEW: Factor in diplomatic memory and reputation
        var memory = GetDiplomaticMemory(ai);
        float reputationModifier = memory.GetReputation(other) / 100f; // -1 to +1
        int trustLevel = memory.GetTrustLevel(other);
        
        // Trust affects willingness to make deals
        float trustModifier = (trustLevel - 5) / 10f; // -0.5 to +0.5
        
        // Check for recent betrayals
        if (memory.HasRecentEvent(other, DiplomaticEventType.BrokePeace, 20))
        {
            personalityFactor *= 0.3f; // Much less likely to trust
        }
        
        // Check for recent positive interactions
        if (memory.HasRecentEvent(other, DiplomaticEventType.HonoredTreaty, 10))
        {
            personalityFactor *= 1.3f; // More likely to trust
        }

        float myStrength    = CivilizationManager.Instance.ComputeMilitaryStrength(ai) + 1f;
        float theirStrength = CivilizationManager.Instance.ComputeMilitaryStrength(other) + 1f;
        float ratio = myStrength / theirStrength;

        // Apply all modifiers
        ratio *= personalityFactor * (1 + reputationModifier + trustModifier);

        // NEW: Consider shared enemies and allies
        float diplomaticContext = CalculateDiplomaticContext(ai, other, deal);
        ratio *= diplomaticContext;

        switch (deal)
        {
            case DealType.War:
                // accept war if stronger, but consider reputation cost
                return ratio > 1.1f && !memory.HasRecentEvent(other, DiplomaticEventType.AcceptedAlliance, 15);
            case DealType.Peace:
                // accept peace if losing or if reputation is very bad
                return ratio < 0.9f || reputationModifier < -0.5f;
            case DealType.Alliance:
                // more likely if enemies of shared rivals and good reputation
                return ratio >= 0.8f && reputationModifier > -0.3f && trustLevel >= 4;
            case DealType.Trade:
                // consider reputation and recent trade history
                return reputationModifier > -0.7f && !memory.HasRecentEvent(other, DiplomaticEventType.TradedWithEnemy, 5);
            case DealType.Vassal:
                // only if far weaker and trust is reasonable
                return ratio < 0.5f && trustLevel >= 3;
            default:
                return false;
        }
    }

    /// <summary>
    /// Calculate diplomatic context based on shared enemies, allies, etc.
    /// </summary>
    private float CalculateDiplomaticContext(Civilization ai, Civilization other, DealType deal)
    {
        float contextModifier = 1.0f;
        
        // Count shared enemies
        int sharedEnemies = 0;
        int sharedAllies = 0;
        
        foreach (var civ in CivilizationManager.Instance.GetAllCivs())
        {
            if (civ == ai || civ == other) continue;
            
            var aiRelation = GetRelationship(ai, civ);
            var otherRelation = GetRelationship(other, civ);
            
            if (aiRelation == DiplomaticState.War && otherRelation == DiplomaticState.War)
                sharedEnemies++;
            else if (aiRelation == DiplomaticState.Alliance && otherRelation == DiplomaticState.Alliance)
                sharedAllies++;
        }
        
        // Shared enemies make alliances more likely
        if (deal == DealType.Alliance && sharedEnemies > 0)
            contextModifier *= 1.0f + (sharedEnemies * 0.3f);
            
        // Shared allies make war less likely
        if (deal == DealType.War && sharedAllies > 0)
            contextModifier *= 1.0f - (sharedAllies * 0.2f);
            
        return contextModifier;
    }

    /// <summary>
    /// Set the diplomatic state between two civilizations directly.
    /// </summary>
    public void SetState(Civilization a, Civilization b, DiplomaticState state)
    {
        if (!relations.ContainsKey(a)) relations[a] = new Dictionary<Civilization, DiplomaticState>();
        if (!relations.ContainsKey(b)) relations[b] = new Dictionary<Civilization, DiplomaticState>();
        
        var oldState = relations[a].GetValueOrDefault(b, DiplomaticState.Peace);
        
        relations[a][b] = state;
        relations[b][a] = state;

        // Record diplomatic events based on state changes
        if (oldState != state)
        {
            RecordStateChangeEvents(a, b, oldState, state);
        }

        OnDiplomacyChanged?.Invoke(a, b, state);
        OnDiplomacyChanged?.Invoke(b, a, state);
    }
    
    /// <summary>
    /// Record appropriate diplomatic events when states change
    /// </summary>
    private void RecordStateChangeEvents(Civilization a, Civilization b, DiplomaticState oldState, DiplomaticState newState)
    {
        if (oldState == DiplomaticState.Peace && newState == DiplomaticState.War)
        {
            GetDiplomaticMemory(a).RecordEvent(b, DiplomaticEventType.DeclaredWar);
            GetDiplomaticMemory(b).RecordEvent(a, DiplomaticEventType.DeclaredWar);
        }
        else if (oldState == DiplomaticState.War && newState == DiplomaticState.Peace)
        {
            GetDiplomaticMemory(a).RecordEvent(b, DiplomaticEventType.HonoredTreaty);
            GetDiplomaticMemory(b).RecordEvent(a, DiplomaticEventType.HonoredTreaty);
        }
        else if (newState == DiplomaticState.Alliance)
        {
            GetDiplomaticMemory(a).RecordEvent(b, DiplomaticEventType.AcceptedAlliance);
            GetDiplomaticMemory(b).RecordEvent(a, DiplomaticEventType.AcceptedAlliance);
        }
    }
}
