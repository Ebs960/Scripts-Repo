using UnityEngine;
using System.Collections.Generic;
using System.Linq;

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

    // ── ISaveGameParticipant ──────────────────────────────────────────────────
    public string SaveKey => "SubjectManager_v1";

    // ── State ─────────────────────────────────────────────────────────────────
    private List<VassalContract> _contracts = new List<VassalContract>();

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

            int goldTransfer    = Mathf.FloorToInt(c.subject.cachedGoldPerTurn * c.goldTributePct);
            int sciTransfer     = Mathf.FloorToInt(c.subject.cachedSciencePerTurn * c.scienceTributePct);
            int foodTransfer    = Mathf.FloorToInt(c.subject.cachedFoodPerTurn * c.foodTributePct);

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
        for (int i = _contracts.Count - 1; i >= 0; i--)
        {
            var c = _contracts[i];
            if (c.overlord == null || c.subject == null) continue;

            c.TickLibertyDesire(currentTurn);

            // Update military confidence (rough proxy: subject unit count vs overlord)
            int subjectMilitary  = c.subject.combatUnits?.Count ?? 0;
            int overlordMilitary = c.overlord.combatUnits?.Count ?? 0;
            if (overlordMilitary > 0)
                c.militaryConfidence = Mathf.Clamp((float)subjectMilitary / overlordMilitary * 100f, 0f, 100f);

            if (c.WantsIndependence())
                AttemptIndependence(c, currentTurn);
        }
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

    /// <summary>
    /// Overlord replaces a local governor in the subject civ.
    /// Angers both the replaced governor and the subject civ's lords.
    /// </summary>
    public void InterfereReplaceGovernor(Civilization overlord, Civilization subject, Governor newGov, int currentTurn)
    {
        var contract = GetContract(overlord, subject);
        if (contract == null || contract.IsInterferenceOnCooldown(currentTurn)) return;

        contract.resentment = Mathf.Min(100f, contract.resentment + 20f);
        contract.lastInterferenceTurn = currentTurn;

        // Anger all subject governors
        foreach (var gov in subject.governors)
            gov.AddOpinionModifier("Overlord Replaced Local Governor", -12f, 20);

        Debug.Log($"[SubjectManager] {overlord.civData?.civName} replaced a governor in {subject.civData?.civName}.");
    }

    /// <summary>
    /// Overlord forces a religion conversion on the subject civ.
    /// </summary>
    public void InterfereForceReligion(Civilization overlord, Civilization subject, int currentTurn)
    {
        var contract = GetContract(overlord, subject);
        if (contract == null || contract.IsInterferenceOnCooldown(currentTurn)) return;

        contract.religionRule = ReligionToleranceRule.ForcedConversion;
        contract.resentment = Mathf.Min(100f, contract.resentment + 30f);
        contract.lastInterferenceTurn = currentTurn;

        // Anger zealous subject governors hardest
        foreach (var gov in subject.governors)
        {
            float anger = gov.HasPersonality(PersonalityTrait.Zealous) ? -25f : -12f;
            gov.AddGrievance(GrievanceSource.ReligionForced);
            gov.AddOpinionModifier("State Religion Imposed by Overlord", anger, 30);
        }

        Debug.Log($"[SubjectManager] {overlord.civData?.civName} forced religion on {subject.civData?.civName}.");
    }

    /// <summary>
    /// Overlord alters tribute terms (raises them). Angers subject governors.
    /// </summary>
    public void InterfereAlterTribute(Civilization overlord, Civilization subject, float newGoldPct, int currentTurn)
    {
        var contract = GetContract(overlord, subject);
        if (contract == null || contract.IsInterferenceOnCooldown(currentTurn)) return;

        float delta = newGoldPct - contract.goldTributePct;
        contract.goldTributePct = Mathf.Clamp(newGoldPct, 0f, 0.50f);
        contract.resentment = Mathf.Min(100f, contract.resentment + Mathf.Abs(delta) * 100f);
        contract.lastInterferenceTurn = currentTurn;

        if (delta > 0f)
        {
            foreach (var gov in subject.governors)
            {
                gov.AddGrievance(GrievanceSource.TaxIncreased);
                gov.AddOpinionModifier("Tribute Increased by Overlord", -10f, 20);
            }
        }

        Debug.Log($"[SubjectManager] {overlord.civData?.civName} altered tribute from {subject.civData?.civName} to {newGoldPct:P0}.");
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

        // Pick strongest available units (by maxHp as a proxy for combat power)
        var candidates = subject.combatUnits?
            .Where(u => u != null && !u.isGarrisoned)
            .OrderByDescending(u => u.maxHp)
            .Take(contract.militaryObligationCount)
            .ToList();

        if (candidates == null || candidates.Count == 0) return;

        foreach (var unit in candidates)
        {
            // Transfer unit to overlord
            subject.combatUnits.Remove(unit);
            unit.owner = overlord;
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

    // ── Independence ──────────────────────────────────────────────────────────    private void AttemptIndependence(VassalContract contract, int currentTurn)
    {
        // Simple probability gate: confident, resentful subjects break away
        float breakawayChance = (contract.libertyDesire - contract.breakawayThreshold) * 0.02f
                              + contract.militaryConfidence * 0.005f;

        if (Random.value < breakawayChance)
        {
            Debug.Log($"[SubjectManager] {contract.subjectCivName} declares independence from {contract.overlordCivName}!");

            // Notify both civs
            UIManager.Instance?.ShowNotification(
                $"{contract.subjectCivName} has declared independence from {contract.overlordCivName}!");

            // Set to war
            contract.overlord.SetRelation(contract.subject, DiplomaticState.War);
            contract.subject.SetRelation(contract.overlord, DiplomaticState.War);

            _contracts.Remove(contract);
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

    [System.Serializable]
    private class SavePayload { public List<ContractSaveData> contracts = new(); }

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
        return JsonUtility.ToJson(payload);
    }

    public void RestoreStateJson(string json)
    {
        _contracts.Clear();
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

        Debug.Log($"[SubjectManager] Restored {_contracts.Count} vassal contracts.");
    }
}
