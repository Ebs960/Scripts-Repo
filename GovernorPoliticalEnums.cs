using System;

/// <summary>
/// Sources of tracked grievance stacks on a Governor.
/// Each source accumulates independently so the player can see exactly why a governor is angry.
/// </summary>
public enum GrievanceSource
{
    CityReassigned,         // City taken from this governor and given to another
    OverruledDecision,      // Player directly countermanded a local decision
    TaxIncreased,           // Civ-wide tax raised above governor preference
    TitleRevoked,           // A rank or privilege was stripped
    CouncilSeatDenied,      // Governor is powerful enough for council but was refused
    ReligionForced,         // State religion imposed; governor follows a different faith
    PrivilegeRevoked,       // Local autonomy reduced or a granted privilege removed
    PublicInsult,           // Diplomatic/event action that humiliated the governor
    AllianceBrokenWithAlly, // Civ broke an alliance the governor valued
    WarLosses,              // Governor's governed territory suffered severe war damage
}

/// <summary>
/// Domains in which a council may exercise veto power.
/// Flags enum so a government can grant multiple veto domains at once.
/// </summary>
[Flags]
public enum VetoDomain
{
    None            = 0,
    WarDeclaration  = 1 << 0,  // Council must approve declaring war
    Succession      = 1 << 1,  // Council must approve succession decisions
    Taxation        = 1 << 2,  // Council must approve tax increases
    Religion        = 1 << 3,  // Council must approve religious policy changes
    TitleRevocation = 1 << 4,  // Council must approve revoking governor titles
    GovernmentChange = 1 << 5, // Council must approve switching government type
    PolicyChange    = 1 << 6,  // Council must approve adopting/revoking policies
    Military        = 1 << 7,  // Council must approve military institutions and doctrine
    All             = WarDeclaration | Succession | Taxation | Religion | TitleRevocation | GovernmentChange | PolicyChange | Military,
}

/// <summary>
/// The political motivation driving a noble faction.
/// Determines what kinds of demands the faction generates.
/// </summary>
public enum FactionAlignment
{
    Independent,   // Wants broader governor autonomy, resists centralization
    Reformist,     // Wants specific policy change (unlock or revoke)
    Conservative,  // Resists government type changes; wants status quo
    Religious,     // Wants religious policy to match their faith
    Separatist,    // Wants to break away entirely
    Mercantile,    // Wants trade-favoring policies and lower tribute burden
}

/// <summary>
/// What a noble faction is formally demanding from the player.
/// </summary>
public enum FactionDemandType
{
    GrantCouncilSeat,      // Add the faction leader (or a member) to the royal council
    RevokePolicy,          // Revoke a specific active policy they oppose
    AdoptPolicy,           // Force adoption of a policy they favor
    ChangeGovernment,      // Transition to a different government type
    ReduceTaxation,        // Lower civ-wide gold/yield drain on subjects
    GrantReligiousFreedom, // Remove forced-religion grievance effects
    AdoptStateReligion,    // Adopt the demand's non-null targetReligion
    EndForcedConversion,   // End coercion and retain the faction's faith
    RecognizeSuccessor,    // Acknowledge a succession claim
    ReturnTerritory,       // Re-assign a previously reassigned city back to the faction leader
    DeclareIndependence,   // Separatist ultimatum before rebellion
}

public enum ReligiousFactionGoal
{
    EstablishOurReligion,
    DefendStateReligion,
    DemandTolerance,
    EndForcedConversion,
}

public enum ReligionProposalType
{
    None,
    AdoptStateReligion,
    RemoveStateReligion,
    RelaxReligiousRestrictions,
    ForceConversion,
}

public enum StateReligionChangeReason
{
    Founding,
    VoluntaryAdoption,
    PoliticalDemand,
    Event,
    ForcedSubjectConversion,
    PlayerDecision,
    AIDecision,
}

/// <summary>
/// How an overlord civ treats the subject civ's local religion.
/// </summary>
public enum ReligionToleranceRule
{
    FullTolerance,         // Local religion is freely practiced; no penalty
    LimitedTolerance,      // Local religion tolerated but cannot spread; mild unrest
    StateReligionRequired, // Subject must adopt state religion or face resentment growth
    ForcedConversion,      // Active pressure; heavy liberty desire gain per turn
}
