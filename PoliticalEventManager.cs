using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DefaultExecutionOrder(-90)]
public class PoliticalEventManager : MonoBehaviour, ISaveGameParticipant
{
    public static PoliticalEventManager Instance { get; private set; }

    public string SaveKey => "PoliticalEventManager_v1";

    [SerializeField] private int maxActiveEventsPerCiv = 3;
    [SerializeField] private int governorPetitionCooldownTurns = 6;
    [SerializeField] private int vassalPetitionCooldownTurns = 8;

    private readonly List<PoliticalEventRecord> activeEvents = new List<PoliticalEventRecord>();
    private readonly Dictionary<string, int> actorCooldowns = new Dictionary<string, int>();
    private int nextEventId = 1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreate()
    {
        if (Instance != null) return;
        var existing = FindAnyObjectByType<PoliticalEventManager>();
        if (existing != null)
        {
            Instance = existing;
            return;
        }

        var go = new GameObject("PoliticalEventManager");
        DontDestroyOnLoad(go);
        go.AddComponent<PoliticalEventManager>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        SaveGameRegistry.Register(this);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        SaveGameRegistry.Unregister(this);
    }

    public void ProcessCivilization(Civilization civ, int currentTurn)
    {
        if (civ == null) return;

        ExpireEventsForCivilization(civ, currentTurn);
        SyncFactionDemandEvents(civ, currentTurn);
        ProcessGovernorPetitions(civ, currentTurn);
        ProcessCouncilSuccession(civ, currentTurn);
        ProcessVassalPetitions(civ, currentTurn);
        TryPresentPendingPlayerEvent(civ);
    }

    public IReadOnlyList<PoliticalEventRecord> GetActiveEventsForCiv(Civilization civ)
    {
        if (civ == null) return Array.Empty<PoliticalEventRecord>();
        string civName = civ.civData?.civName ?? civ.name;
        return activeEvents
            .Where(e => e.status == PoliticalEventStatus.Pending && e.targetCivName == civName)
            .OrderBy(e => e.expiryTurn)
            .ThenBy(e => e.createdTurn)
            .ToList();
    }

    private void ProcessGovernorPetitions(Civilization civ, int currentTurn)
    {
        if (!civ.isPlayerControlled || civ.governors == null) return;

        foreach (var governor in civ.governors)
        {
            if (governor == null || governor.Faction != null || governor.IsInRebellion) continue;

            bool upset = governor.Opinion <= -20f || governor.TotalGrievances() >= 2 || PoliticalDistanceUtility.GetGovernorDistancePenalty(governor) >= 8f;
            if (!upset) continue;

            string sourceKey = $"GovernorPetition|{civ.civData?.civName ?? civ.name}|{governor.Id}";
            if (HasActiveEvent(sourceKey)) continue;
            if (IsOnCooldown(sourceKey, currentTurn, governorPetitionCooldownTurns)) continue;

            var options = new List<PoliticalEventOption>
            {
                new PoliticalEventOption { optionId = "gift", label = "Send Gifts", summary = "Improve the governor's opinion with gold and favors.", targetGovernorId = governor.Id },
                new PoliticalEventOption { optionId = "refuse", label = "Refuse", summary = "Deny the petition and risk deeper resentment.", targetGovernorId = governor.Id },
            };

            string body = $"{governor.Name} petitions for recognition and relief. Their opinion is {Mathf.RoundToInt(governor.Opinion)} and grievances are mounting.";
            if (!governor.IsOnCouncil && governor.IsCouncilEligible && civ.royalCouncil.Count < civ.MaxCouncilSeats)
            {
                options.Insert(0, new PoliticalEventOption
                {
                    optionId = "grant_council_seat",
                    label = "Grant Council Seat",
                    summary = "Seat this lord on the royal council and clear council resentment.",
                    targetGovernorId = governor.Id,
                });
                body = $"{governor.Name} petitions for a place in government. Their influence is growing, and exclusion from the council is becoming dangerous.";
            }

            QueueEvent(new PoliticalEventRecord
            {
                id = nextEventId++,
                eventType = PoliticalEventType.GovernorPetition,
                sourceKey = sourceKey,
                targetCivName = civ.civData?.civName ?? civ.name,
                title = $"Petition from {governor.Name}",
                body = body,
                createdTurn = currentTurn,
                expiryTurn = currentTurn + 4,
                targetGovernorId = governor.Id,
                primaryActor = new PoliticalActorRef
                {
                    actorType = PoliticalActorType.Governor,
                    civName = civ.civData?.civName ?? civ.name,
                    governorId = governor.Id,
                    displayName = governor.Name,
                },
                options = options,
            });
        }
    }

    private void SyncFactionDemandEvents(Civilization civ, int currentTurn)
    {
        foreach (var bloc in civ.nobleFactions)
        {
            if (bloc == null) continue;
            foreach (var demand in bloc.ActiveDemands)
            {
                if (demand == null) continue;
                string sourceKey = $"FactionDemand|{civ.civData?.civName ?? civ.name}|{bloc.FactionName}|{demand.type}|{demand.issuedTurn}";
                if (HasActiveEvent(sourceKey)) continue;

                QueueEvent(new PoliticalEventRecord
                {
                    id = nextEventId++,
                    eventType = PoliticalEventType.FactionDemand,
                    sourceKey = sourceKey,
                    targetCivName = civ.civData?.civName ?? civ.name,
                    title = bloc.FactionName,
                    body = demand.description,
                    createdTurn = currentTurn,
                    expiryTurn = demand.issuedTurn + demand.expiryTurns,
                    factionName = bloc.FactionName,
                    factionDemandType = demand.type.ToString(),
                    issuedTurn = demand.issuedTurn,
                    targetGovernorId = demand.targetGovernor?.Id ?? -1,
                    primaryActor = new PoliticalActorRef
                    {
                        actorType = PoliticalActorType.Faction,
                        civName = civ.civData?.civName ?? civ.name,
                        factionName = bloc.FactionName,
                        displayName = bloc.FactionName,
                    },
                    options = new List<PoliticalEventOption>
                    {
                        new PoliticalEventOption { optionId = "accept", label = "Accept", summary = "Concede to the faction demand." },
                        new PoliticalEventOption { optionId = "refuse", label = "Refuse", summary = "Risk grievances or open rebellion." },
                    }
                });
            }
        }
    }

    private void ProcessCouncilSuccession(Civilization civ, int currentTurn)
    {
        if (civ.MaxCouncilSeats <= 0) return;

        var candidates = civ.GetUnseatedPowerfulLords()
            .Where(g => g != null)
            .OrderByDescending(g => g.PowerRank)
            .ThenByDescending(g => g.Opinion)
            .ToList();

        int emptySeats = Mathf.Max(0, civ.MaxCouncilSeats - civ.royalCouncil.Count);
        if (emptySeats <= 0 || candidates.Count == 0) return;

        if (!civ.isPlayerControlled || candidates.Count == 1)
        {
            for (int i = 0; i < emptySeats && i < candidates.Count; i++)
                civ.AddToCouncil(candidates[i]);
            return;
        }

        string sourceKey = $"CouncilElection|{civ.civData?.civName ?? civ.name}";
        if (HasActiveEvent(sourceKey)) return;

        var options = candidates
            .Take(4)
            .Select(candidate => new PoliticalEventOption
            {
                optionId = "seat_candidate",
                label = candidate.Name,
                summary = $"Power {candidate.PowerRank}, Opinion {Mathf.RoundToInt(candidate.Opinion)}",
                targetGovernorId = candidate.Id,
            })
            .ToList();

        QueueEvent(new PoliticalEventRecord
        {
            id = nextEventId++,
            eventType = PoliticalEventType.CouncilElection,
            sourceKey = sourceKey,
            targetCivName = civ.civData?.civName ?? civ.name,
            title = "Royal Council Vacancy",
            body = "A council seat stands open. Choose which powerful lord will be admitted to the royal council.",
            createdTurn = currentTurn,
            expiryTurn = currentTurn + 4,
            primaryActor = new PoliticalActorRef
            {
                actorType = PoliticalActorType.Council,
                civName = civ.civData?.civName ?? civ.name,
                displayName = "Royal Council",
            },
            options = options,
        });
    }

    private void ProcessVassalPetitions(Civilization civ, int currentTurn)
    {
        if (!civ.isPlayerControlled || SubjectManager.Instance == null) return;

        foreach (var contract in SubjectManager.Instance.GetSubjects(civ))
        {
            if (contract?.subject == null) continue;

            bool shouldPetition = contract.libertyDesire >= 50f
                               || contract.tributeExhaustion >= 30f
                               || contract.subjectOpinion <= -20f;
            if (!shouldPetition) continue;

            string subjectName = contract.subject.civData?.civName ?? contract.subject.name;
            string sourceKey = $"VassalPetition|{civ.civData?.civName ?? civ.name}|{subjectName}";
            if (HasActiveEvent(sourceKey)) continue;
            if (IsOnCooldown(sourceKey, currentTurn, vassalPetitionCooldownTurns)) continue;

            QueueEvent(new PoliticalEventRecord
            {
                id = nextEventId++,
                eventType = PoliticalEventType.VassalPetition,
                sourceKey = sourceKey,
                targetCivName = civ.civData?.civName ?? civ.name,
                title = $"Petition from {subjectName}",
                body = $"{subjectName} petitions for relief. Their tribute burden and unrest are rising.",
                createdTurn = currentTurn,
                expiryTurn = currentTurn + 5,
                subjectCivName = subjectName,
                primaryActor = new PoliticalActorRef
                {
                    actorType = PoliticalActorType.Vassal,
                    civName = civ.civData?.civName ?? civ.name,
                    subjectCivName = subjectName,
                    displayName = subjectName,
                },
                options = new List<PoliticalEventOption>
                {
                    new PoliticalEventOption { optionId = "reduce_tribute", label = "Reduce Tribute", summary = "Lower tribute and calm the subject." },
                    new PoliticalEventOption { optionId = "grant_autonomy", label = "Grant Autonomy", summary = "Increase self-rule to reduce unrest." },
                    new PoliticalEventOption { optionId = "refuse", label = "Refuse", summary = "Keep pressure high and risk rebellion." },
                }
            });
        }
    }

    private void QueueEvent(PoliticalEventRecord record)
    {
        if (record == null || string.IsNullOrEmpty(record.targetCivName)) return;
        int countForCiv = activeEvents.Count(e => e.status == PoliticalEventStatus.Pending && e.targetCivName == record.targetCivName);
        if (countForCiv >= maxActiveEventsPerCiv) return;
        activeEvents.Add(record);
    }

    private void TryPresentPendingPlayerEvent(Civilization civ)
    {
        if (civ == null || !civ.isPlayerControlled || UIManager.Instance == null) return;
        if (UIManager.Instance.IsBlockingModalVisible) return;

        var next = activeEvents
            .Where(e => e.status == PoliticalEventStatus.Pending
                     && e.targetCivName == (civ.civData?.civName ?? civ.name)
                     && !e.presentedToPlayer)
            .OrderBy(e => e.createdTurn)
            .FirstOrDefault();

        if (next == null) return;

        var options = next.options.Select(option => new MissionSelectionPopupUI.OptionData
        {
            title = option.label,
            body = option.summary,
            interactable = true,
        }).ToList();

        bool shown = UIManager.Instance.ShowPoliticalSelection(next.title, next.body, options, index => ResolveEvent(next.id, index));
        if (shown)
            next.presentedToPlayer = true;
    }

    public void ResolveEvent(int eventId, int optionIndex)
    {
        var record = activeEvents.FirstOrDefault(e => e.id == eventId && e.status == PoliticalEventStatus.Pending);
        if (record == null) return;
        if (optionIndex < 0 || optionIndex >= record.options.Count) return;

        var civ = FindCivilization(record.targetCivName);
        var option = record.options[optionIndex];
        int currentTurn = TurnManager.Instance?.round ?? record.createdTurn;

        switch (record.eventType)
        {
            case PoliticalEventType.FactionDemand:
                ResolveFactionEvent(record, civ, option, currentTurn);
                break;
            case PoliticalEventType.GovernorPetition:
                ResolveGovernorPetition(record, civ, option, currentTurn);
                break;
            case PoliticalEventType.CouncilElection:
                ResolveCouncilElection(record, civ, option);
                break;
            case PoliticalEventType.VassalPetition:
                ResolveVassalPetition(record, civ, option, currentTurn);
                break;
        }

        record.status = PoliticalEventStatus.Resolved;
        TryPresentPendingPlayerEvent(civ);
    }

    private void ResolveFactionEvent(PoliticalEventRecord record, Civilization civ, PoliticalEventOption option, int currentTurn)
    {
        if (civ == null) return;
        var bloc = civ.nobleFactions.FirstOrDefault(f => f != null && f.FactionName == record.factionName);
        if (bloc == null) return;

        if (!Enum.TryParse(record.factionDemandType, out FactionDemandType demandType))
            return;

        var demand = bloc.ActiveDemands.FirstOrDefault(d => d != null && d.type == demandType && d.issuedTurn == record.issuedTurn);
        if (demand == null) return;

        bool accepted = option.optionId == "accept";
        civ.ResolveFactionDemand(bloc, demand, accepted, currentTurn);
    }

    private void ResolveCouncilElection(PoliticalEventRecord record, Civilization civ, PoliticalEventOption option)
    {
        if (civ == null) return;
        var governor = civ.governors.FirstOrDefault(g => g != null && g.Id == option.targetGovernorId);
        if (governor == null) return;
        civ.AddToCouncil(governor);
        UIManager.Instance?.ShowNotification($"{governor.Name} takes a seat on the royal council.");
    }

    private void ResolveGovernorPetition(PoliticalEventRecord record, Civilization civ, PoliticalEventOption option, int currentTurn)
    {
        if (civ == null) return;
        var governor = civ.governors.FirstOrDefault(g => g != null && g.Id == record.targetGovernorId);
        if (governor == null) return;

        switch (option.optionId)
        {
            case "grant_council_seat":
                if (civ.AddToCouncil(governor))
                {
                    governor.AddOpinionModifier("Council Petition Granted", 12f, 20);
                    UIManager.Instance?.ShowNotification($"{governor.Name} has been seated on the royal council.");
                }
                break;
            case "gift":
                governor.AddOpinionModifier("Received Gift", 15f, 15);
                governor.ClearGrievance(GrievanceSource.PublicInsult);
                UIManager.Instance?.ShowNotification($"Gifts were sent to {governor.Name}.");
                break;
            default:
                governor.AddGrievance(GrievanceSource.OverruledDecision);
                governor.AddOpinionModifier("Petition Refused", -15f, 20);
                RememberCooldown(record.sourceKey, currentTurn);
                UIManager.Instance?.ShowNotification($"You refused the petition from {governor.Name}.");
                break;
        }
    }

    private void ResolveVassalPetition(PoliticalEventRecord record, Civilization civ, PoliticalEventOption option, int currentTurn)
    {
        if (civ == null || SubjectManager.Instance == null) return;
        var subject = FindCivilization(record.subjectCivName);
        if (subject == null) return;

        var contract = SubjectManager.Instance.GetContract(civ, subject);
        if (contract == null) return;

        switch (option.optionId)
        {
            case "reduce_tribute":
                contract.goldTributePct = Mathf.Max(0f, contract.goldTributePct - 0.05f);
                contract.subjectOpinion = Mathf.Clamp(contract.subjectOpinion + 12f, -100f, 100f);
                contract.libertyDesire = Mathf.Clamp(contract.libertyDesire - 8f, 0f, 100f);
                contract.resentment = Mathf.Max(0f, contract.resentment - 8f);
                UIManager.Instance?.ShowNotification($"Tribute reduced for {subject.civData?.civName ?? subject.name}.");
                break;
            case "grant_autonomy":
                contract.autonomyLevel = Mathf.Clamp(contract.autonomyLevel + 10, 0, 100);
                contract.subjectOpinion = Mathf.Clamp(contract.subjectOpinion + 18f, -100f, 100f);
                contract.libertyDesire = Mathf.Clamp(contract.libertyDesire - 12f, 0f, 100f);
                UIManager.Instance?.ShowNotification($"Autonomy granted to {subject.civData?.civName ?? subject.name}.");
                break;
            default:
                contract.subjectOpinion = Mathf.Clamp(contract.subjectOpinion - 15f, -100f, 100f);
                contract.resentment = Mathf.Min(100f, contract.resentment + 12f);
                contract.libertyDesire = Mathf.Clamp(contract.libertyDesire + 10f, 0f, 100f);
                RememberCooldown(record.sourceKey, currentTurn);
                UIManager.Instance?.ShowNotification($"You refused the petition from {subject.civData?.civName ?? subject.name}.");
                break;
        }
    }

    private void ExpireEventsForCivilization(Civilization civ, int currentTurn)
    {
        if (civ == null) return;
        string civName = civ.civData?.civName ?? civ.name;

        foreach (var record in activeEvents)
        {
            if (record.status != PoliticalEventStatus.Pending) continue;
            if (record.targetCivName != civName) continue;
            if (currentTurn <= record.expiryTurn) continue;

            record.status = PoliticalEventStatus.Expired;

            switch (record.eventType)
            {
                case PoliticalEventType.FactionDemand:
                    ResolveExpiredFactionDemand(record, civ, currentTurn);
                    break;
                case PoliticalEventType.GovernorPetition:
                    ResolveExpiredGovernorPetition(record, civ, currentTurn);
                    break;
                case PoliticalEventType.CouncilElection:
                    ResolveExpiredCouncilElection(civ);
                    break;
                case PoliticalEventType.VassalPetition:
                    ResolveExpiredVassalPetition(record, civ, currentTurn);
                    break;
            }
        }
    }

    private void ResolveExpiredFactionDemand(PoliticalEventRecord record, Civilization civ, int currentTurn)
    {
        var bloc = civ.nobleFactions.FirstOrDefault(f => f != null && f.FactionName == record.factionName);
        if (bloc == null) return;
        if (!Enum.TryParse(record.factionDemandType, out FactionDemandType demandType)) return;
        var demand = bloc.ActiveDemands.FirstOrDefault(d => d != null && d.type == demandType && d.issuedTurn == record.issuedTurn);
        if (demand == null) return;
        civ.ResolveFactionDemand(bloc, demand, false, currentTurn);
    }

    private void ResolveExpiredCouncilElection(Civilization civ)
    {
        var fallback = civ.GetUnseatedPowerfulLords().OrderByDescending(g => g.PowerRank).FirstOrDefault();
        if (fallback != null)
            civ.AddToCouncil(fallback);
    }

    private void ResolveExpiredGovernorPetition(PoliticalEventRecord record, Civilization civ, int currentTurn)
    {
        var refuseOption = new PoliticalEventOption { optionId = "refuse" };
        ResolveGovernorPetition(record, civ, refuseOption, currentTurn);
    }

    private void ResolveExpiredVassalPetition(PoliticalEventRecord record, Civilization civ, int currentTurn)
    {
        var refuseOption = new PoliticalEventOption { optionId = "refuse" };
        ResolveVassalPetition(record, civ, refuseOption, currentTurn);
    }

    private bool HasActiveEvent(string sourceKey)
        => activeEvents.Any(e => e.status == PoliticalEventStatus.Pending && e.sourceKey == sourceKey);

    private bool IsOnCooldown(string sourceKey, int currentTurn, int cooldownTurns)
        => actorCooldowns.TryGetValue(sourceKey, out int lastTurn) && (currentTurn - lastTurn) < cooldownTurns;

    private void RememberCooldown(string sourceKey, int currentTurn)
    {
        if (string.IsNullOrEmpty(sourceKey)) return;
        actorCooldowns[sourceKey] = currentTurn;
    }

    private static Civilization FindCivilization(string civName)
    {
        if (string.IsNullOrEmpty(civName) || CivilizationManager.Instance == null) return null;
        return CivilizationManager.Instance.GetAllCivs()
            .FirstOrDefault(c => (c.civData?.civName ?? c.name) == civName);
    }

    [Serializable]
    private class PoliticalFactionDemandState
    {
        public string type;
        public string description;
        public int issuedTurn;
        public int expiryTurns;
        public int targetGovernorId;
    }

    [Serializable]
    private class PoliticalFactionState
    {
        public string factionName;
        public string alignment;
        public bool isInRebellion;
        public List<int> memberGovernorIds = new List<int>();
        public List<PoliticalFactionDemandState> activeDemands = new List<PoliticalFactionDemandState>();
    }

    [Serializable]
    private class PoliticalCivState
    {
        public string civName;
        public List<int> councilGovernorIds = new List<int>();
        public List<PoliticalFactionState> factions = new List<PoliticalFactionState>();
    }

    [Serializable]
    private class CooldownState
    {
        public string key;
        public int lastTurn;
    }

    [Serializable]
    private class SavePayload
    {
        public int nextEventId;
        public List<PoliticalEventRecord> activeEvents = new List<PoliticalEventRecord>();
        public List<CooldownState> cooldowns = new List<CooldownState>();
        public List<PoliticalCivState> civStates = new List<PoliticalCivState>();
    }

    public string CaptureStateJson()
    {
        var payload = new SavePayload { nextEventId = nextEventId };
        payload.activeEvents.AddRange(activeEvents);

        foreach (var kv in actorCooldowns)
            payload.cooldowns.Add(new CooldownState { key = kv.Key, lastTurn = kv.Value });

        if (CivilizationManager.Instance != null)
        {
            foreach (var civ in CivilizationManager.Instance.GetAllCivs())
            {
                if (civ == null) continue;
                var civState = new PoliticalCivState { civName = civ.civData?.civName ?? civ.name };
                civState.councilGovernorIds.AddRange(civ.royalCouncil.Where(g => g != null).Select(g => g.Id));

                foreach (var bloc in civ.nobleFactions)
                {
                    if (bloc == null) continue;
                    var factionState = new PoliticalFactionState
                    {
                        factionName = bloc.FactionName,
                        alignment = bloc.Alignment.ToString(),
                        isInRebellion = bloc.IsInRebellion,
                    };
                    factionState.memberGovernorIds.AddRange(bloc.Members.Where(m => m != null).Select(m => m.Id));

                    foreach (var demand in bloc.ActiveDemands)
                    {
                        if (demand == null) continue;
                        factionState.activeDemands.Add(new PoliticalFactionDemandState
                        {
                            type = demand.type.ToString(),
                            description = demand.description,
                            issuedTurn = demand.issuedTurn,
                            expiryTurns = demand.expiryTurns,
                            targetGovernorId = demand.targetGovernor?.Id ?? -1,
                        });
                    }

                    civState.factions.Add(factionState);
                }

                payload.civStates.Add(civState);
            }
        }

        return JsonUtility.ToJson(payload);
    }

    public void RestoreStateJson(string json)
    {
        activeEvents.Clear();
        actorCooldowns.Clear();
        if (string.IsNullOrEmpty(json)) return;

        var payload = JsonUtility.FromJson<SavePayload>(json);
        if (payload == null) return;

        nextEventId = Mathf.Max(1, payload.nextEventId);
        if (payload.activeEvents != null)
            activeEvents.AddRange(payload.activeEvents);
        if (payload.cooldowns != null)
        {
            foreach (var cooldown in payload.cooldowns)
            {
                if (cooldown == null || string.IsNullOrEmpty(cooldown.key)) continue;
                actorCooldowns[cooldown.key] = cooldown.lastTurn;
            }
        }

        if (CivilizationManager.Instance == null || payload.civStates == null) return;

        foreach (var civ in CivilizationManager.Instance.GetAllCivs())
        {
            if (civ == null) continue;
            foreach (var gov in civ.governors)
            {
                if (gov == null) continue;
                gov.IsOnCouncil = false;
                gov.Faction = null;
                gov.IsInRebellion = false;
            }
            civ.royalCouncil.Clear();
            civ.nobleFactions.Clear();
        }

        foreach (var civState in payload.civStates)
        {
            var civ = FindCivilization(civState.civName);
            if (civ == null) continue;

            foreach (var governorId in civState.councilGovernorIds)
            {
                var gov = civ.governors.FirstOrDefault(g => g != null && g.Id == governorId);
                if (gov == null) continue;
                if (!civ.royalCouncil.Contains(gov))
                    civ.royalCouncil.Add(gov);
                gov.IsOnCouncil = true;
            }

            foreach (var factionState in civState.factions)
            {
                if (factionState == null || factionState.memberGovernorIds.Count == 0) continue;
                if (!Enum.TryParse(factionState.alignment, out FactionAlignment alignment))
                    alignment = FactionAlignment.Independent;

                var founder = civ.governors.FirstOrDefault(g => g != null && g.Id == factionState.memberGovernorIds[0]);
                if (founder == null) continue;

                var bloc = new FactionBloc(factionState.factionName, alignment, founder);
                for (int i = 1; i < factionState.memberGovernorIds.Count; i++)
                {
                    var member = civ.governors.FirstOrDefault(g => g != null && g.Id == factionState.memberGovernorIds[i]);
                    if (member != null)
                        bloc.AddMember(member);
                }

                foreach (var demandState in factionState.activeDemands)
                {
                    if (demandState == null || !Enum.TryParse(demandState.type, out FactionDemandType demandType)) continue;
                    var targetGov = civ.governors.FirstOrDefault(g => g != null && g.Id == demandState.targetGovernorId);
                    bloc.ActiveDemands.Add(new FactionDemand
                    {
                        type = demandType,
                        description = demandState.description,
                        issuedTurn = demandState.issuedTurn,
                        expiryTurns = demandState.expiryTurns,
                        targetGovernor = targetGov,
                    });
                }

                bloc.RestoreRebellionState(factionState.isInRebellion);
                civ.nobleFactions.Add(bloc);
            }
        }
    }
}