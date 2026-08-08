// Assets/Scripts Repo/MilitaryFormationSummary.cs
using System.Collections.Generic;

/// <summary>
/// Read-only snapshot of a military formation (an "army") derived from the CombatUnits
/// that share the same MilitaryFormationId, plus any commander assignments from
/// MilitaryCommanderAssignmentService. Used by MilitaryPanel to populate the Armies tab.
/// </summary>
public class MilitaryFormationSummary
{
    public string FormationId;
    public string FormationName;
    public MilitaryFormationType FormationType;
    public List<CombatUnit> Members = new List<CombatUnit>();
    public List<MilitaryCommanderAssignment> Commanders = new List<MilitaryCommanderAssignment>();
}
