using System;
using System.Collections.Generic;

/// <summary>
/// Save DTO. Garrison entries are persistent CombatUnit IDs only; unit state is never duplicated.
/// Asset IDs are resolved by the host save catalogue.
/// </summary>
[Serializable]
public sealed class BandSaveData
{
    public string persistentId;
    public string bandDataId;
    public int ownerIndex;
    public int planetIndex;
    public int tileIndex;
    public BandState state;
    public int movementPoints;
    public int population;
    public int foodReserve;
    public int consecutiveStarvationTurns;
    public List<string> builtStructureIds = new List<string>();
    public string queuedStructureId;
    public string queuedCombatUnitDataId;
    public int productionProgress;
    public List<string> garrisonCombatUnitPersistentIds = new List<string>();
}

[Serializable]
public sealed class CivilianAttachmentSaveData
{
    public string workerPersistentId;
    public string attachedArmyFormationId;
}
