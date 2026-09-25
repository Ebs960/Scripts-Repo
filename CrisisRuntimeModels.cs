using System;
using System.Collections.Generic;

[Flags]
public enum CrisisActorTag
{
    None=0, Predator=1, Cannibal=2, Barbarian=4, Rebel=8, CoupRebel=16,
    Revolutionary=32, Terrorist=64, IndependenceRebel=128, RobotUprising=256,
    Mutant=512, Alien=1024
}

[Serializable] public class CrisisOccurrenceRecord
{
    public string crisisIdentity;
    public int occurrenceCount;
    public int lastTriggerTurn = -1;
    public int lastResolutionTurn = -1;
}

[Serializable] public class CrisisBaselineSnapshot
{
    public int civilizationIndex, population, cityCount, buildingCount, improvementCount, farms;
    public int militaryUnits, robotUnits, gold, food, startingTradeRoutes;
    public float tradeIncome, goldIncome, averageOrder, averageHappiness;
}

[Serializable] public class CrisisActorCountSnapshot
{
    public CrisisActorTag tag;
    public int count;
}

[Serializable] public class CrisisRuntimeContext
{
    public string crisisIdentity;
    public List<int> targetCivilizationIndices = new List<int>();
    public int targetCityId = -1, targetContinentId = -1, overlordIndex = -1, subjectIndex = -1;
    public int triggerTurn;
    public string riskExplanation;
    public float resolvedRisk;
    public List<int> spawnedActorIds = new List<int>();
    public List<CrisisActorCountSnapshot> actorCountsAtActivation = new List<CrisisActorCountSnapshot>();
    public List<int> integratedActorIds = new List<int>();
    public List<int> alienSettlementCivilizationIndices = new List<int>();
    public List<int> damagedInfrastructureIds = new List<int>();
    public List<CrisisInfrastructureRecord> infrastructureStates = new List<CrisisInfrastructureRecord>();
    public List<CrisisBaselineSnapshot> baselines = new List<CrisisBaselineSnapshot>();
    public List<CrisisProjectProgress> projectProgress = new List<CrisisProjectProgress>();
    public List<CrisisAttributedLoss> attributedLosses = new List<CrisisAttributedLoss>();
    public List<CrisisNarrativeSnapshot> narrativeSnapshots = new List<CrisisNarrativeSnapshot>();
}

[Serializable] public class CrisisNarrativeSnapshot
{
    public int civilizationIndex;
    public string factionName, demand, factionDemandSummary, primaryGrievance;
    public int distinctDemandCount;
}

[Serializable] public class CrisisInfrastructureRecord
{
    public int planetIndex;
    public int tileIndex;
    public ImprovementManager.CrisisImprovementState state;
    public int warningUntilTurn;
}

[Serializable] public class CrisisProjectProgress
{
    public string projectName;
    public int civilizationIndex;
    public int cityId = -1;
    public int productionInvested;
    public bool completed;
}

[Serializable] public class CrisisAttributedLoss
{
    public enum LossKind { Unit, Worker, Population, City, Building, Improvement }
    public LossKind kind;
    public int victimCivilizationIndex = -1;
    public int sourceActorId = -1;
    public CrisisActorTag sourceTags;
    public int amount = 1;
    public int turn;
}

public static class CrisisEligibility
{
    public static bool IsConstitutional(GovernmentData government) => government != null
        && (government.archetypes & GovernmentArchetypeFlags.Representative) != 0
        && (government.archetypes & GovernmentArchetypeFlags.CollectiveMind) == 0;
    public static bool IsConventionalCoup(GovernmentData government) => government != null
        && (government.archetypes & GovernmentArchetypeFlags.CollectiveMind) == 0
        && (government.archetypes & GovernmentArchetypeFlags.Representative) == 0
        && (government.archetypes & (GovernmentArchetypeFlags.CentralizedExecutive | GovernmentArchetypeFlags.Oligarchic | GovernmentArchetypeFlags.Clerical | GovernmentArchetypeFlags.HereditaryExecutive)) != 0;
}

public static class CrisisOccurrenceRules
{
    public static bool CanTrigger(CrisisData crisis, CrisisOccurrenceRecord record, int turn, bool presentInLegacyHistory=false)
    {
        if (crisis == null) return false;
        if (record == null) return !presentInLegacyHistory;
        if (crisis.repeatMode == CrisisData.CrisisRepeatMode.OneTime) return record.occurrenceCount == 0;
        if (crisis.maximumOccurrences > 0 && record.occurrenceCount >= crisis.maximumOccurrences) return false;
        return record.lastResolutionTurn < 0 || turn-record.lastResolutionTurn >= crisis.cooldownTurns;
    }
}
