using UnityEngine;

public abstract class DiploDeal : ScriptableObject
{
    public string dealName;
    public int durationTurns;
    public abstract bool CanPropose(Civilization from, Civilization to);
    public abstract bool Evaluate(Civilization ai, Civilization proposer);
    public abstract void Enact(Civilization a, Civilization b);
    public abstract void Abort(Civilization a, Civilization b);
}

[CreateAssetMenu(menuName="Data/Diplomacy/Peace Treaty")]
public class PeaceTreaty : DiploDeal
{
    public override bool CanPropose(Civilization from, Civilization to)
    {
        // Can only propose peace if currently at war
        return DiplomacyManager.Instance.GetRelationship(from, to) == DiplomaticState.War;
    }
    
    public override bool Evaluate(Civilization ai, Civilization proposer)
    {
        // Basic logic: accept peace if weaker militarily
        float myStrength = CivilizationManager.Instance.ComputeMilitaryStrength(ai) + 1f;
        float theirStrength = CivilizationManager.Instance.ComputeMilitaryStrength(proposer) + 1f;
        float ratio = myStrength / theirStrength;
        
        return ratio < 0.9f;
    }
    
    public override void Enact(Civilization a, Civilization b)
    {
        DiplomacyManager.Instance.SetState(a, b, DiplomaticState.Peace);
    }
    
    public override void Abort(Civilization a, Civilization b)
    {
        // Breaking a peace treaty causes reputation penalty
// Apply reputation penalty directly in Civilization if needed
        // Example: a.diplomacyReputation -= 10;
    }
}

[CreateAssetMenu(menuName="Data/Diplomacy/Trade Agreement")]
public class TradeAgreement : DiploDeal
{
    public int goldPerTurnExchange = 5;
    
    public override bool CanPropose(Civilization from, Civilization to)
    {
        // Can't trade during war
        return DiplomacyManager.Instance.GetRelationship(from, to) != DiplomaticState.War;
    }
    
    public override bool Evaluate(Civilization ai, Civilization proposer)
    {
        // AI almost always accepts trade
        return true;
    }
    
    public override void Enact(Civilization a, Civilization b)
    {
        DiplomacyManager.Instance.SetState(a, b, DiplomaticState.Trade);
        
        // Add gold per turn to both civilizations
        a.gold += goldPerTurnExchange;
        b.gold += goldPerTurnExchange;
    }
    
    public override void Abort(Civilization a, Civilization b)
    {
        DiplomacyManager.Instance.SetState(a, b, DiplomaticState.Peace);
    }
}

[CreateAssetMenu(menuName="Data/Diplomacy/Alliance")]
public class AllianceDeal : DiploDeal
{
    public override bool CanPropose(Civilization from, Civilization to)
    {
        // Need to be at peace or trading to propose alliance
        var state = DiplomacyManager.Instance.GetRelationship(from, to);
        return state == DiplomaticState.Peace || state == DiplomaticState.Trade;
    }
    
    public override bool Evaluate(Civilization ai, Civilization proposer)
    {
        // Base decision on relative strength and shared enemies
        float myStrength = CivilizationManager.Instance.ComputeMilitaryStrength(ai) + 1f;
        float theirStrength = CivilizationManager.Instance.ComputeMilitaryStrength(proposer) + 1f;
        
        // More likely to ally if they're stronger (for protection)
        if (myStrength < theirStrength * 0.8f)
            return true;
            
        // Count shared enemies
        int sharedEnemies = 0;
        foreach (var civ in CivilizationManager.Instance.GetAllCivs())
        {
            if (civ == ai || civ == proposer) continue;
            
            if (DiplomacyManager.Instance.GetRelationship(ai, civ) == DiplomaticState.War &&
                DiplomacyManager.Instance.GetRelationship(proposer, civ) == DiplomaticState.War)
            {
                sharedEnemies++;
            }
        }
        
        // More likely to ally if we have shared enemies
        return sharedEnemies > 0 || Random.value < 0.3f;
    }
    
    public override void Enact(Civilization a, Civilization b)
    {
        DiplomacyManager.Instance.SetState(a, b, DiplomaticState.Alliance);
        
        // Share vision - removed FogOfWarManager dependency
// Vision sharing would be implemented directly in a visibility system
    }
    
    public override void Abort(Civilization a, Civilization b)
    {
        DiplomacyManager.Instance.SetState(a, b, DiplomaticState.Peace);
        
        // Stop sharing vision - removed FogOfWarManager dependency
// Apply major reputation penalty for breaking alliance
// Apply reputation penalty directly if needed
        // Example: a.diplomacyReputation -= 25;
    }
} 