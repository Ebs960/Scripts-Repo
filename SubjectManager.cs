using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>Outcome of a player-facing subject management action; reason explains a failure or notes a side effect.</summary>
public struct SubjectActionResult
{
    public bool success;
    public string reason;

    public static SubjectActionResult Ok(string note = null) => new SubjectActionResult { success = true, reason = note };
    public static SubjectActionResult Fail(string reason) => new SubjectActionResult { success = false, reason = reason };
}

/// <summary>
/// Singleton that owns all active VassalContracts, processes tribute each turn,
/// ticks liberty desire, enforces behavioral restrictions on subjects, and handles
/// interference actions from overlords.
///
/// Attach to a persistent scene GameObject. Registers with SaveGameRegistry for save/load.
/// Called each turn by ClimateManager or GameManager after civ BeginTurn passes.
/// </summary>
public class SubjectManager : MonoBehaviour, ISaveGameParticipant
{
    public static SubjectManager Instance { get; private set; }

    /// <summary>Raised (overlord, subject) whenever a contract is created, changed, or dissolved so UI can refresh.</summary>
    public static event System.Action<Civilization, Civilization> ContractChanged;

    // Tuning for the generalized contract setters. Tribute/forced-religion/governor numbers reuse the existing interference values.
    public const float MaxTributePct = 0.50f;
    public const int MaxMilitaryObligation = 10;
    private const float TributeReliefReferenceStep = 0.05f;
    private const float TributeReliefOpinion = 8f;
    private const float TributeReliefLiberty = 5f;
    private const float AutonomyReductionResentmentPerPoint = 0.5f;
    private const int AutonomyGrievanceThresholdPoints = 10;
    private const float AutonomyGrantOpinionPerPoint = 1.8f;
    private const float AutonomyGrantLibertyPerPoint = 1.2f;
    private const float MilitaryObligationResentmentPerUnit = 3f;
    private const float ReligionTighteningResentmentPerStep = 10f;
    private const float ReligionRelaxOpinionPerStep = 6f;

    private static void RaiseContractChanged(Civilization overlord, Civilization subject)
        => ContractChanged?.Invoke(overlord, subject);

    // ── ISaveGameParticipant ──────────────────────────────────────────────────
    public string SaveKey => "SubjectManager_v1";

    // ── State ─────────────────────────────────────────────────────────────────
    private List<VassalContract> _contracts = new List<VassalContract>();
    private List<IndependenceDemand> _independenceDemands = new List<IndependenceDemand>();
    private List<DiplomaticOpinionModifier> _diplomaticOpinionModifiers = new List<DiplomaticOpinionModifier>();

    [System.Serializable]
    public class DiplomaticOpinionModifier
    {
        [System.NonSerialized] public Civilization holder;
        [System.NonSerialized] public Civilization toward;
        public string holderCivName, towardCivName, reason;
        public float value;
        public int expiresOnTurn;
    }

    public IReadOnlyList<IndependenceDemand> IndependenceDemands => _independenceDemands;
    public IReadOnlyList<DiplomaticOpinionModifier> DiplomaticOpinionModifiers => _diplomaticOpinionModifiers;

    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        SaveGameRegistry.Register(this);
    }

    private void OnDestroy()
    {
        SaveGameRegistry.Unregister(this);
    }

    // ── Contract Management ───────────────────────────────────────────────────

    /// <summary>Get the contract between two civs (either direction), or null.</summary>
    public VassalContract GetContract(Civilization overlord, Civilization subject)
        => _contracts.FirstOrDefault(c => c.overlord == overlord && c.subject == subject);

    /// <summary>Is this civ a subject of any overlord?</summary>
    public bool IsSubject(Civilization civ) => _contracts.Any(c => c.subject == civ);

    /// <summary>Is this civ an overlord of at least one subject?</summary>
    public bool IsOverlord(Civilization civ) => _contracts.Any(c => c.overlord == civ);

    /// <summary>Get all contracts where this civ is the overlord.</summary>
    public List<VassalContract> GetSubjects(Civilization overlord)
        => _contracts.Where(c => c.overlord == overlord).ToList();

    /// <summary>Get the contract where this civ is the subject, or null.</summary>
    public VassalContract GetOverlordContract(Civilization subject)
        => _contracts.FirstOrDefault(c => c.subject == subject);

    public IndependenceDemand GetPendingIndependenceDemand(Civilization overlord, Civilization subject)
        => _independenceDemands.FirstOrDefault(d => !d.resolved && d.overlord == overlord && d.subject == subject);

    /// <summary>
    /// Create a new vassal contract. Sets the DiplomaticState to Vassal on both civs.
    /// </summary>
    public VassalContract CreateContract(
        Civilization overlord, Civilization subject,
        float goldPct = 0.10f, float sciencePct = 0f,
        int autonomy = 50, bool capitulated = false, int currentTurn = 0)
    {
        if (overlord == null || subject == null || overlord == subject) return null;
        // Remove any pre-existing contract
        DissolveContract(overlord, subject);

        var contract = new VassalContract
        {
            overlord         = overlord,
            subject          = subject,
            overlordCivName  = overlord.civData?.civName ?? overlord.name,
            subjectCivName   = subject.civData?.civName ?? subject.name,
            goldTributePct   = goldPct,
            scienceTributePct = sciencePct,
            autonomyLevel    = autonomy,
            isCapitulated    = capitulated,
            contractStartTurn = currentTurn,
        };
        _contracts.Add(contract);

        // Mirror in the diplomatic state
        overlord.SetRelation(subject, DiplomaticState.Vassal);
        subject.SetRelation(overlord, DiplomaticState.Vassal);

        Debug.Log($"[SubjectManager] Vassal contract created: {contract.overlordCivName} → {contract.subjectCivName} " +
                  $"(gold {goldPct:P0}, autonomy {autonomy})");
        RaiseContractChanged(overlord, subject);
        return contract;
    }

    /// <summary>Dissolve a vassal contract. Sets relations back to Peace.</summary>
    public bool DissolveContract(Civilization overlord, Civilization subject)
    {
        var contract = GetContract(overlord, subject);
        if (contract == null) return false;
        _contracts.Remove(contract);

        overlord.SetRelation(subject, DiplomaticState.Peace);
        subject.SetRelation(overlord, DiplomaticState.Peace);
        Debug.Log($"[SubjectManager] Vassal contract dissolved: {contract.overlordCivName} ← {contract.subjectCivName}");
        RaiseContractChanged(overlord, subject);
        return true;
    }

    // ── Per-Turn Processing ───────────────────────────────────────────────────

    /// <summary>
    /// Transfer tribute from all subjects to their overlords.
    /// Call once per game turn (after civ yield is calculated but before BeginTurn completes).
    /// </summary>
    public void ProcessTributeTick(int currentTurn)
    {
        foreach (var c in _contracts)
        {
            if (c.overlord == null || c.subject == null) continue;

            float tributeModifier = GetLegacyInstitutionTotal(c.overlord, m=>m.subjectTributeModifier);
            int goldTransfer    = Mathf.FloorToInt(c.subject.cachedGoldPerTurn * c.goldTributePct * (1f+tributeModifier));
            int sciTransfer     = Mathf.FloorToInt(c.subject.cachedSciencePerTurn * c.scienceTributePct * (1f+tributeModifier));
            int foodTransfer    = Mathf.FloorToInt(c.subject.cachedFoodPerTurn * c.foodTributePct * (1f+tributeModifier));

            // Deduct from subject
            c.subject.gold    = Mathf.Max(0, c.subject.gold    - goldTransfer);
            c.subject.science = Mathf.Max(0, c.subject.science - sciTransfer);
            c.subject.food    = Mathf.Max(0, c.subject.food    - foodTransfer);

            // Credit to overlord
            c.overlord.gold    += goldTransfer;
            c.overlord.science += sciTransfer;
            c.overlord.food    += foodTransfer;

            // Tribute exhaustion accumulates when tribute takes a large share of income
            float totalPctBurden = c.goldTributePct + c.scienceTributePct + c.foodTributePct;
            c.tributeExhaustion = Mathf.Clamp(c.tributeExhaustion + totalPctBurden * 5f, 0f, 100f);

            // Subject opinion worsens proportionally to burden
            c.subjectOpinion = Mathf.Clamp(c.subjectOpinion - totalPctBurden * 2f, -100f, 100f);
        }
    }

    /// <summary>
    /// Tick liberty desire for all subject civs. Call once per game turn.
    /// Also checks for breakaway attempts.
    /// </summary>
    public void ProcessLibertyTick(int currentTurn)
    {
        _diplomaticOpinionModifiers.RemoveAll(m => currentTurn >= m.expiresOnTurn);
        for (int i = _contracts.Count - 1; i >= 0; i--)
        {
            var c = _contracts[i];
            if (c.overlord == null || c.subject == null) continue;

            EnforceSubjectReligionRule(c);

            c.TickLibertyDesire(currentTurn, GetEffectiveSubjectOpinion(c));
            float libertyModifier=GetLegacyInstitutionTotal(c.overlord,m=>m.subjectLibertyGrowthModifier);
            if (!Mathf.Approximately(libertyModifier,0f)) c.libertyDesire=Mathf.Clamp(c.libertyDesire*(1f+libertyModifier),0f,100f);

            // Update military confidence (rough proxy: subject unit count vs overlord)
            int subjectMilitary  = c.subject.combatUnits?.Count ?? 0;
            int overlordMilitary = c.overlord.combatUnits?.Count ?? 0;
            if (overlordMilitary > 0)
                c.militaryConfidence = Mathf.Clamp((float)subjectMilitary / overlordMilitary * 100f, 0f, 100f);

            if (c.WantsIndependence())
                AttemptIndependence(c, currentTurn);
        }
    }

    private static float GetLegacyInstitutionTotal(Civilization civ, System.Func<LegacyInstitutionModifiers,float> selector)
    {
        if (civ?.activeLegacies == null) return 0f;
        float total=0f;
        foreach (var legacy in civ.activeLegacies)
            if (legacy?.institutions != null) total+=selector(legacy.institutions);
        return total;
    }

    /// <summary>Current opinion including promoted overlord legacies, without mutating saved relationship history.</summary>
    public float GetEffectiveSubjectOpinion(VassalContract contract)
    {
        if (contract == null) return 0f;
        return Mathf.Clamp(contract.subjectOpinion
            + GetLegacyInstitutionTotal(contract.overlord, m => m.subjectOpinionModifier), -100f, 100f);
    }

    // ── Behavioral Restrictions ───────────────────────────────────────────────

    /// <summary>Can this subject civ declare war on the given target?</summary>
    public bool CanDeclareWar(Civilization subject, Civilization target)
    {
        var contract = GetOverlordContract(subject);
        if (contract == null) return true;
        // Subject can only declare war if overlord is also at war with target, or autonomy is very high
        if (contract.autonomyLevel >= 80) return true;
        return contract.overlord.relations.TryGetValue(target, out var state) && state == DiplomaticState.War;
    }

    /// <summary>Can this subject civ form an alliance with another civ?</summary>
    public bool CanFormAlliance(Civilization subject, Civilization ally)
    {
        var contract = GetOverlordContract(subject);
        if (contract == null) return true;
        if (contract.autonomyLevel >= 75) return true;
        // Cannot ally with overlord's enemies
        if (contract.overlord.relations.TryGetValue(ally, out var state) && state == DiplomaticState.War)
            return false;
        return true;
    }

    /// <summary>Can this subject civ found a new city?</summary>
    public bool CanFoundCity(Civilization subject)
    {
        var contract = GetOverlordContract(subject);
        if (contract == null) return true;
        return contract.autonomyLevel >= 60;
    }

    /// <summary>Can this subject civ independently change their state religion?</summary>
    public bool CanChangeReligion(Civilization subject)
    {
        var contract = GetOverlordContract(subject);
        if (contract == null) return true;
        return contract.religionRule == ReligionToleranceRule.FullTolerance
            || contract.religionRule == ReligionToleranceRule.LimitedTolerance;
    }

    // ── Interference Actions ──────────────────────────────────────────────────

    /// <summary>Can the overlord interfere with this subject right now (contract exists, cooldown elapsed)?</summary>
    public SubjectActionResult CanInterfere(Civilization overlord, Civilization subject, int currentTurn)
    {
        var contract = GetContract(overlord, subject);
        if (contract == null) return SubjectActionResult.Fail("No vassal contract exists with this realm.");
        if (contract.IsInterferenceOnCooldown(currentTurn))
        {
            int remaining = contract.interferenceCooldown - (currentTurn - contract.lastInterferenceTurn);
            return SubjectActionResult.Fail($"Interference is on cooldown for {Mathf.Max(1, remaining)} more turn(s).");
        }
        return SubjectActionResult.Ok();
    }

    /// <summary>
    /// Overlord replaces a local governor in the subject civ.
    /// Angers both the replaced governor and the subject civ's governors.
    /// </summary>
    public SubjectActionResult InterfereReplaceGovernor(Civilization overlord, Civilization subject, Governor newGov, int currentTurn)
    {
        var check = CanInterfere(overlord, subject, currentTurn);
        if (!check.success) return check;
        var contract = GetContract(overlord, subject);

        var targetCity = subject.cities?
            .Where(c => c != null && c.owner == subject)
            .OrderByDescending(c => c.isCapital)
            .ThenByDescending(c => c.level)
            .FirstOrDefault();
        if (targetCity == null) return SubjectActionResult.Fail("The subject has no city whose governor could be replaced.");

        var oldGov = targetCity.governor;

        if (newGov == null)
        {
            newGov = subject.governors?
                .FirstOrDefault(g => g != null && g != oldGov && !g.Cities.Contains(targetCity));

            if (newGov == null && subject.governorsEnabled && subject.governors.Count < subject.governorCount)
            {
                var specialization = oldGov != null ? oldGov.specialization : Governor.Specialization.Military;
                newGov = subject.CreateGovernor("Imperial Appointee", specialization);
            }
        }

        if (oldGov != null)
        {
            subject.RemoveGovernorFromCity(oldGov, targetCity);
            oldGov.AddGrievance(GrievanceSource.TitleRevoked);
            oldGov.AddOpinionModifier("Removed by Overlord", -20f, 25);
        }

        if (newGov != null)
        {
            subject.AssignGovernorToCity(newGov, targetCity);
            newGov.AddOpinionModifier("Installed by Overlord", 8f, 20);
        }

        contract.resentment = Mathf.Min(100f, contract.resentment + 20f);
        contract.lastInterferenceTurn = currentTurn;

        // Anger all subject governors
        foreach (var gov in subject.governors)
            gov.AddOpinionModifier("Overlord Replaced Local Governor", -12f, 20);

        Debug.Log($"[SubjectManager] {overlord.civData?.civName} replaced the governor of {targetCity.cityName} in {subject.civData?.civName}.");
        RaiseContractChanged(overlord, subject);
        return SubjectActionResult.Ok();
    }

    /// <summary>
    /// Overlord forces a religion conversion on the subject civ.
    /// </summary>
    public SubjectActionResult InterfereForceReligion(Civilization overlord, Civilization subject, int currentTurn)
    {
        var check = CanInterfere(overlord, subject, currentTurn);
        if (!check.success) return check;
        var contract = GetContract(overlord, subject);

        contract.religionRule = ReligionToleranceRule.ForcedConversion;
        contract.resentment = Mathf.Min(100f, contract.resentment + 30f);
        contract.lastInterferenceTurn = currentTurn;

        ApplyForcedConversion(contract);

        // Anger zealous subject governors hardest
        foreach (var gov in subject.governors)
        {
            float anger = gov.HasPersonality(PersonalityTrait.Zealous) ? -25f : -12f;
            gov.AddGrievance(GrievanceSource.ReligionForced);
            gov.AddOpinionModifier("State Religion Imposed by Overlord", anger, 30);
        }

        Debug.Log($"[SubjectManager] {overlord.civData?.civName} forced religion on {subject.civData?.civName}.");
        RaiseContractChanged(overlord, subject);
        return SubjectActionResult.Ok();
    }

    /// <summary>
    /// Overlord alters tribute terms (raises them). Angers subject governors.
    /// </summary>
    public SubjectActionResult InterfereAlterTribute(Civilization overlord, Civilization subject, float newGoldPct, int currentTurn)
    {
        var check = CanInterfere(overlord, subject, currentTurn);
        if (!check.success) return check;
        var contract = GetContract(overlord, subject);

        ApplyTributeChange(contract, newGoldPct, contract.scienceTributePct, contract.foodTributePct, currentTurn);

        Debug.Log($"[SubjectManager] {overlord.civData?.civName} altered tribute from {subject.civData?.civName} to {newGoldPct:P0}.");
        RaiseContractChanged(overlord, subject);
        return SubjectActionResult.Ok();
    }

    // Shared consequence logic: any tribute change costs resentment and starts the cooldown; a heavier burden angers governors.
    private static void ApplyTributeChange(VassalContract contract, float newGold, float newScience, float newFood, int currentTurn)
    {
        newGold = Mathf.Clamp(newGold, 0f, MaxTributePct);
        newScience = Mathf.Clamp(newScience, 0f, MaxTributePct);
        newFood = Mathf.Clamp(newFood, 0f, MaxTributePct);

        float dGold = newGold - contract.goldTributePct;
        float dScience = newScience - contract.scienceTributePct;
        float dFood = newFood - contract.foodTributePct;
        float absoluteChange = Mathf.Abs(dGold) + Mathf.Abs(dScience) + Mathf.Abs(dFood);
        float burdenChange = dGold + dScience + dFood;

        contract.goldTributePct = newGold;
        contract.scienceTributePct = newScience;
        contract.foodTributePct = newFood;
        contract.resentment = Mathf.Min(100f, contract.resentment + absoluteChange * 100f);
        contract.lastInterferenceTurn = currentTurn;

        if (burdenChange > 0f && contract.subject?.governors != null)
        {
            foreach (var gov in contract.subject.governors)
            {
                if (gov == null) continue;
                gov.AddGrievance(GrievanceSource.TaxIncreased);
                gov.AddOpinionModifier("Tribute Increased by Overlord", -10f, 20);
            }
        }
    }

    // ── Player-facing contract management ─────────────────────────────────────
    // UI must go through these; each one shares the interference cooldown and consequences above.

    public SubjectActionResult TrySetTributeTerms(Civilization overlord, Civilization subject,
        float goldPct, float sciencePct, float foodPct, int currentTurn)
    {
        var check = CanInterfere(overlord, subject, currentTurn);
        if (!check.success) return check;
        var contract = GetContract(overlord, subject);

        goldPct = Mathf.Clamp(goldPct, 0f, MaxTributePct);
        sciencePct = Mathf.Clamp(sciencePct, 0f, MaxTributePct);
        foodPct = Mathf.Clamp(foodPct, 0f, MaxTributePct);
        if (Mathf.Approximately(goldPct, contract.goldTributePct)
            && Mathf.Approximately(sciencePct, contract.scienceTributePct)
            && Mathf.Approximately(foodPct, contract.foodTributePct))
            return SubjectActionResult.Fail("Tribute terms are unchanged.");

        float oldTotal = contract.goldTributePct + contract.scienceTributePct + contract.foodTributePct;
        ApplyTributeChange(contract, goldPct, sciencePct, foodPct, currentTurn);

        // Relief scales with the cut (the Diplomacy screen grants the full amount for a 5-point step).
        float reduction = oldTotal - (contract.goldTributePct + contract.scienceTributePct + contract.foodTributePct);
        if (reduction > 0f)
        {
            float relief = Mathf.Clamp01(reduction / TributeReliefReferenceStep);
            contract.subjectOpinion = Mathf.Clamp(contract.subjectOpinion + TributeReliefOpinion * relief, -100f, 100f);
            contract.libertyDesire = Mathf.Clamp(contract.libertyDesire - TributeReliefLiberty * relief, 0f, 100f);
        }

        RaiseContractChanged(overlord, subject);
        return SubjectActionResult.Ok();
    }

    public SubjectActionResult TrySetAutonomy(Civilization overlord, Civilization subject, int autonomy, int currentTurn)
    {
        var check = CanInterfere(overlord, subject, currentTurn);
        if (!check.success) return check;
        var contract = GetContract(overlord, subject);

        autonomy = Mathf.Clamp(autonomy, 0, 100);
        int delta = autonomy - contract.autonomyLevel;
        if (delta == 0) return SubjectActionResult.Fail("Autonomy is unchanged.");

        contract.autonomyLevel = autonomy;
        contract.lastInterferenceTurn = currentTurn;
        if (delta < 0)
        {
            contract.resentment = Mathf.Min(100f, contract.resentment - delta * AutonomyReductionResentmentPerPoint);
            if (-delta >= AutonomyGrievanceThresholdPoints && subject.governors != null)
                foreach (var gov in subject.governors)
                    gov?.AddGrievance(GrievanceSource.PrivilegeRevoked);
        }
        else
        {
            contract.subjectOpinion = Mathf.Clamp(contract.subjectOpinion + delta * AutonomyGrantOpinionPerPoint, -100f, 100f);
            contract.libertyDesire = Mathf.Clamp(contract.libertyDesire - delta * AutonomyGrantLibertyPerPoint, 0f, 100f);
        }

        RaiseContractChanged(overlord, subject);
        return SubjectActionResult.Ok();
    }

    public SubjectActionResult TrySetMilitaryObligation(Civilization overlord, Civilization subject, int count, int currentTurn)
    {
        var check = CanInterfere(overlord, subject, currentTurn);
        if (!check.success) return check;
        var contract = GetContract(overlord, subject);

        count = Mathf.Clamp(count, 0, MaxMilitaryObligation);
        int delta = count - contract.militaryObligationCount;
        if (delta == 0) return SubjectActionResult.Fail("Military obligation is unchanged.");

        contract.militaryObligationCount = count;
        contract.lastInterferenceTurn = currentTurn;
        if (delta > 0)
            contract.resentment = Mathf.Min(100f, contract.resentment + delta * MilitaryObligationResentmentPerUnit);

        RaiseContractChanged(overlord, subject);
        return SubjectActionResult.Ok();
    }

    public SubjectActionResult TrySetReligionRule(Civilization overlord, Civilization subject, ReligionToleranceRule rule, int currentTurn)
    {
        var check = CanInterfere(overlord, subject, currentTurn);
        if (!check.success) return check;
        var contract = GetContract(overlord, subject);
        if (rule == contract.religionRule) return SubjectActionResult.Fail("This religious policy is already in force.");

        // Forced conversion keeps its dedicated, harsher consequences.
        if (rule == ReligionToleranceRule.ForcedConversion)
            return InterfereForceReligion(overlord, subject, currentTurn);

        int steps = (int)rule - (int)contract.religionRule;
        contract.religionRule = rule;
        contract.lastInterferenceTurn = currentTurn;
        if (steps > 0)
        {
            contract.resentment = Mathf.Min(100f, contract.resentment + steps * ReligionTighteningResentmentPerStep);
            if (subject.governors != null)
                foreach (var gov in subject.governors)
                    if (gov != null && gov.PersonalReligion != overlord.StateReligion)
                        gov.AddOpinionModifier("Religious Restrictions Tightened", -8f, 20);
        }
        else
        {
            contract.subjectOpinion = Mathf.Clamp(contract.subjectOpinion - steps * ReligionRelaxOpinionPerStep, -100f, 100f);
        }

        RaiseContractChanged(overlord, subject);
        return SubjectActionResult.Ok();
    }

    /// <summary>Peacefully ends a vassal contract. A pending independence demand is resolved as accepted.</summary>
    public SubjectActionResult TryReleaseSubject(Civilization overlord, Civilization subject, int currentTurn)
    {
        if (GetContract(overlord, subject) == null) return SubjectActionResult.Fail("No vassal contract exists with this realm.");

        var demand = GetPendingIndependenceDemand(overlord, subject);
        if (demand != null)
            return AcceptIndependenceDemand(demand, currentTurn)
                ? SubjectActionResult.Ok()
                : SubjectActionResult.Fail("The independence demand could not be accepted.");

        if (!DissolveContract(overlord, subject)) return SubjectActionResult.Fail("The contract could not be dissolved.");
        UIManager.Instance?.ShowNotification($"Vassalage ended with {subject.civData?.civName ?? subject.name}.");
        return SubjectActionResult.Ok();
    }

    // ── Military Obligation ───────────────────────────────────────────────────

    /// <summary>
    /// Called when the subject is dragged into a war by their overlord.
    /// Transfers up to militaryObligationCount of the subject's strongest combat units
    /// to the overlord's army by reassigning their owner civ, giving the overlord real
    /// military support rather than just a diplomatic flag.
    /// Adds a WarLosses grievance to subject governors who lose units this way.
    /// </summary>
    public void FulfilMilitaryObligation(VassalContract contract, int currentTurn)
    {
        if (contract == null || contract.militaryObligationCount <= 0) return;
        if (contract.subject == null || contract.overlord == null) return;

        var subject  = contract.subject;
        var overlord = contract.overlord;

        // Pick strongest available units (by max health as a proxy for combat power)
        var candidates = subject.combatUnits?
            .Where(u => u != null && !u.isGarrisonedInCity)
            .OrderByDescending(u => u.MaxHealth)
            .Take(contract.militaryObligationCount)
            .ToList();

        if (candidates == null || candidates.Count == 0) return;

        foreach (var unit in candidates)
        {
            // Transfer unit to overlord
            subject.combatUnits.Remove(unit);
            unit.Initialize(unit.data, overlord);
            overlord.combatUnits.Add(unit);
        }

        // Anger subject governors for the levy
        foreach (var gov in subject.governors)
            gov.AddGrievance(GrievanceSource.WarLosses);

        // Increase tribute exhaustion to reflect the burden
        contract.tributeExhaustion = Mathf.Min(100f, contract.tributeExhaustion + candidates.Count * 5f);

        Debug.Log($"[SubjectManager] {subject.civData?.civName ?? subject.name} provided " +
                  $"{candidates.Count} units to overlord {overlord.civData?.civName ?? overlord.name}.");
    }

    // ── Independence ──────────────────────────────────────────────────────────

    private void AttemptIndependence(VassalContract contract, int currentTurn)
    {
        if (contract == null || GetPendingIndependenceDemand(contract.overlord, contract.subject) != null) return;
        // Simple probability gate: confident, resentful subjects break away
        float breakawayChance = (contract.libertyDesire - contract.EffectiveBreakawayThreshold) * 0.02f
                              + contract.militaryConfidence * 0.005f;

        if (Random.value < breakawayChance)
        {
            IssueIndependenceDemand(contract, currentTurn);
        }
    }

    public IndependenceDemand IssueIndependenceDemand(VassalContract contract, int currentTurn, bool resolveImmediately = true)
    {
        if (contract == null || contract.overlord == null || contract.subject == null) return null;
        var existing = GetPendingIndependenceDemand(contract.overlord, contract.subject);
        if (existing != null) return existing;
        var demand = new IndependenceDemand {
            overlord=contract.overlord, subject=contract.subject,
            overlordCivName=contract.overlordCivName, subjectCivName=contract.subjectCivName,
            turnIssued=currentTurn, demandType=SubjectDemandType.FullIndependence
        };
        _independenceDemands.Add(demand);
        Debug.Log($"[SubjectManager] {demand.subjectCivName} demands full independence from {demand.overlordCivName}.");
        RaiseContractChanged(contract.overlord, contract.subject);
        if (!resolveImmediately) return demand;
        if (contract.overlord == CivilizationManager.Instance?.playerCiv)
            UIManager.Instance?.ShowIndependenceDemand(demand, () => AcceptIndependenceDemand(demand, CurrentTurn), () => RejectIndependenceDemand(demand));
        else
            ResolveAiIndependenceDemand(demand, contract);
        return demand;
    }

    public bool AcceptIndependenceDemand(IndependenceDemand demand, int currentTurn)
    {
        if (!CanResolve(demand)) return false;
        demand.resolved=true;
        if (!DissolveContract(demand.overlord,demand.subject)) return false;
        _diplomaticOpinionModifiers.Add(new DiplomaticOpinionModifier {
            holder=demand.subject, toward=demand.overlord, holderCivName=demand.subjectCivName,
            towardCivName=demand.overlordCivName, reason="Peaceful Independence", value=15f,
            expiresOnTurn=currentTurn+30
        });
        UIManager.Instance?.ShowNotification($"{demand.subjectCivName} peacefully gained full independence.");
        return true;
    }

    public bool RejectIndependenceDemand(IndependenceDemand demand)
    {
        if (!CanResolve(demand)) return false;
        demand.resolved=true;
        var overlord=demand.overlord; var subject=demand.subject;
        if (!DissolveContract(overlord,subject)) return false;
        overlord.SetRelation(subject,DiplomaticState.War);
        subject.SetRelation(overlord,DiplomaticState.War);
        bool started=CrisisManager.Instance != null && CrisisManager.Instance.TriggerWarOfIndependence(overlord,subject);
        UIManager.Instance?.ShowNotification($"{demand.overlordCivName} rejected {demand.subjectCivName}'s demand. The War of Independence has begun!");
        return started;
    }

    private bool CanResolve(IndependenceDemand demand) => demand != null && !demand.resolved
        && demand.overlord != null && demand.subject != null
        && GetContract(demand.overlord,demand.subject) != null;

    public float GetDiplomaticOpinionModifier(Civilization holder, Civilization toward, int currentTurn)
        => _diplomaticOpinionModifiers.Where(m=>m.holder==holder && m.toward==toward && currentTurn<m.expiresOnTurn).Sum(m=>m.value);

    public float ScoreIndependenceDemandRejection(VassalContract contract)
    {
        if (contract == null) return float.MinValue;
        float overlordStrength=contract.overlord?.combatUnits?.Count ?? 0;
        float subjectStrength=contract.subject?.combatUnits?.Count ?? 0;
        int otherWars=contract.overlord?.relations?.Count(r=>r.Value==DiplomaticState.War) ?? 0;
        // Positive rejects: strength and valuable tribute. Negative accepts: overextension,
        // confident rebels, hostile opinion, and extreme liberty desire.
        return (overlordStrength-subjectStrength)*8f
            +(contract.goldTributePct+contract.scienceTributePct+contract.foodTributePct)*100f
            -otherWars*18f-contract.militaryConfidence*.25f-contract.libertyDesire*.15f
            +contract.subjectOpinion*.10f;
    }

    private void ResolveAiIndependenceDemand(IndependenceDemand demand, VassalContract contract)
    {
        if (ScoreIndependenceDemandRejection(contract)>=0f) RejectIndependenceDemand(demand);
        else AcceptIndependenceDemand(demand,CurrentTurn);
    }

    /// <summary>Current game round, for UI callers that pass a turn into the contract setters.</summary>
    public int CurrentTurn => TurnManager.Instance != null ? TurnManager.Instance.round : GameManager.Instance?.currentTurn ?? 0;

    private void EnforceSubjectReligionRule(VassalContract contract)
    {
        if (contract == null || contract.religionRule != ReligionToleranceRule.ForcedConversion)
            return;

        ApplyForcedConversion(contract);
    }

    private void ApplyForcedConversion(VassalContract contract)
    {
        var subject = contract?.subject;
        var overlord = contract?.overlord;
        if (subject == null || overlord == null) return;
        if (overlord.StateReligion == null) return;

        bool changedReligion = subject.StateReligion != overlord.StateReligion;
        ReligionPoliticsService.TrySetStateReligion(subject, overlord.StateReligion,
            StateReligionChangeReason.ForcedSubjectConversion, false, out _);

        if (subject.governors != null)
        {
            foreach (var gov in subject.governors)
            {
                if (gov == null) continue;
                if (gov.PersonalReligion != overlord.StateReligion)
                    gov.AddGrievance(GrievanceSource.ReligionForced);
            }
        }

        if (changedReligion)
        {
            contract.subjectOpinion = Mathf.Clamp(contract.subjectOpinion - 10f, -100f, 100f);
            contract.resentment = Mathf.Min(100f, contract.resentment + 5f);
        }
    }

    // ── Save / Load ───────────────────────────────────────────────────────────

    [System.Serializable]
    private class ContractSaveData
    {
        public string overlordCivName;
        public string subjectCivName;
        public float goldTributePct;
        public float scienceTributePct;
        public float foodTributePct;
        public int militaryObligationCount;
        public int autonomyLevel;
        public string religionRule;
        public int lastInterferenceTurn;
        public float libertyDesire;
        public float breakawayThreshold;
        public float subjectOpinion;
        public float resentment;
        public float tributeExhaustion;
        public float militaryConfidence;
        public bool isCapitulated;
        public int contractStartTurn;
    }

    [System.Serializable] private class DemandSaveData { public string overlordCivName,subjectCivName,demandType; public int turnIssued; public bool resolved; }
    [System.Serializable] private class OpinionSaveData { public string holderCivName,towardCivName,reason; public float value; public int expiresOnTurn; }

    [System.Serializable]
    private class SavePayload { public List<ContractSaveData> contracts = new(); public List<DemandSaveData> demands = new(); public List<OpinionSaveData> opinions = new(); }

    public string CaptureStateJson()
    {
        var payload = new SavePayload();
        foreach (var c in _contracts)
        {
            payload.contracts.Add(new ContractSaveData
            {
                overlordCivName       = c.overlordCivName,
                subjectCivName        = c.subjectCivName,
                goldTributePct        = c.goldTributePct,
                scienceTributePct     = c.scienceTributePct,
                foodTributePct        = c.foodTributePct,
                militaryObligationCount = c.militaryObligationCount,
                autonomyLevel         = c.autonomyLevel,
                religionRule          = c.religionRule.ToString(),
                lastInterferenceTurn  = c.lastInterferenceTurn,
                libertyDesire         = c.libertyDesire,
                breakawayThreshold    = c.breakawayThreshold,
                subjectOpinion        = c.subjectOpinion,
                resentment            = c.resentment,
                tributeExhaustion     = c.tributeExhaustion,
                militaryConfidence    = c.militaryConfidence,
                isCapitulated         = c.isCapitulated,
                contractStartTurn     = c.contractStartTurn,
            });
        }
        foreach(var d in _independenceDemands) payload.demands.Add(new DemandSaveData { overlordCivName=d.overlordCivName,subjectCivName=d.subjectCivName,turnIssued=d.turnIssued,demandType=d.demandType.ToString(),resolved=d.resolved });
        foreach(var m in _diplomaticOpinionModifiers) payload.opinions.Add(new OpinionSaveData { holderCivName=m.holderCivName,towardCivName=m.towardCivName,reason=m.reason,value=m.value,expiresOnTurn=m.expiresOnTurn });
        return JsonUtility.ToJson(payload);
    }

    public void RestoreStateJson(string json)
    {
        _contracts.Clear();
        _independenceDemands.Clear();
        _diplomaticOpinionModifiers.Clear();
        if (string.IsNullOrEmpty(json)) return;

        var payload = JsonUtility.FromJson<SavePayload>(json);
        if (payload?.contracts == null) return;

        var allCivs = GameManager.Instance?.civilizationManager?.GetAllCivs();
        if (allCivs == null) return;

        foreach (var cd in payload.contracts)
        {
            Civilization overlord = null, subject = null;
            foreach (var civ in allCivs)
            {
                string civName = civ.civData?.civName ?? civ.name;
                if (civName == cd.overlordCivName) overlord = civ;
                if (civName == cd.subjectCivName)  subject  = civ;
            }
            if (overlord == null || subject == null) continue;

            var contract = new VassalContract
            {
                overlord              = overlord,
                subject               = subject,
                overlordCivName       = cd.overlordCivName,
                subjectCivName        = cd.subjectCivName,
                goldTributePct        = cd.goldTributePct,
                scienceTributePct     = cd.scienceTributePct,
                foodTributePct        = cd.foodTributePct,
                militaryObligationCount = cd.militaryObligationCount,
                autonomyLevel         = cd.autonomyLevel,
                religionRule          = System.Enum.TryParse<ReligionToleranceRule>(cd.religionRule, out var rule) ? rule : ReligionToleranceRule.FullTolerance,
                lastInterferenceTurn  = cd.lastInterferenceTurn,
                libertyDesire         = cd.libertyDesire,
                breakawayThreshold    = cd.breakawayThreshold,
                subjectOpinion        = cd.subjectOpinion,
                resentment            = cd.resentment,
                tributeExhaustion     = cd.tributeExhaustion,
                militaryConfidence    = cd.militaryConfidence,
                isCapitulated         = cd.isCapitulated,
                contractStartTurn     = cd.contractStartTurn,
            };
            _contracts.Add(contract);
        }

        Civilization FindCiv(string name) => allCivs.FirstOrDefault(c=>(c.civData?.civName ?? c.name)==name);
        foreach(var dd in payload.demands ?? new List<DemandSaveData>()) {
            var overlord=FindCiv(dd.overlordCivName); var subject=FindCiv(dd.subjectCivName);
            if(overlord==null||subject==null) continue;
            _independenceDemands.Add(new IndependenceDemand { overlord=overlord,subject=subject,overlordCivName=dd.overlordCivName,subjectCivName=dd.subjectCivName,turnIssued=dd.turnIssued,resolved=dd.resolved,demandType=System.Enum.TryParse(dd.demandType,out SubjectDemandType type)?type:SubjectDemandType.FullIndependence });
        }
        foreach(var od in payload.opinions ?? new List<OpinionSaveData>()) {
            var holder=FindCiv(od.holderCivName); var toward=FindCiv(od.towardCivName);
            if(holder!=null&&toward!=null) _diplomaticOpinionModifiers.Add(new DiplomaticOpinionModifier { holder=holder,toward=toward,holderCivName=od.holderCivName,towardCivName=od.towardCivName,reason=od.reason,value=od.value,expiresOnTurn=od.expiresOnTurn });
        }

        Debug.Log($"[SubjectManager] Restored {_contracts.Count} vassal contracts.");
    }
}
