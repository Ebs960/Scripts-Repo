using System;
using System.Collections.Generic;
using UnityEngine;

public enum CommandRole
{
    OverallCommander,
    LandCommander,
    NavalCommander,
    UnderwaterCommander,
    AirCommander,
    OrbitalCommander,
    SpaceCommander,
}

public enum CommanderCharacterKind
{
    Governor,
    Admiral,
}

[Serializable]
public enum BattleCommanderStatus { Active, Wounded, Captured, Killed }

[Serializable]
public sealed class MilitaryCommanderAssignment
{
    public string AssignmentId;
    public string FormationId;
    public CommandRole Role;
    public CommanderCharacterKind CharacterKind;
    public int CharacterId;
    public int OwnerCivilizationId;
    public int AssignedTurn;
    public bool IsActive = true;
    public BattleCommanderStatus Status;
    public int WoundedUntilTurn;
    public int CapturedByCivilizationId = -1;
}

public sealed class MilitaryCommanderAssignmentService : MonoBehaviour, ISaveGameParticipant
{
    [Serializable] private sealed class SaveData { public List<MilitaryCommanderAssignment> assignments = new(); }
    public static MilitaryCommanderAssignmentService Instance { get; private set; }
    [SerializeField] private List<MilitaryCommanderAssignment> assignments = new();
    public string SaveKey => "MilitaryCommanderAssignments_v1";

    public static MilitaryCommanderAssignmentService GetOrCreate()
    {
        if (Instance != null) return Instance;
        var existing = FindAnyObjectByType<MilitaryCommanderAssignmentService>();
        if (existing != null) return existing;
        return new GameObject("MilitaryCommanderAssignmentService").AddComponent<MilitaryCommanderAssignmentService>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SaveGameRegistry.Register(this);
    }

    private void OnDestroy() { if (Instance == this) Instance = null; SaveGameRegistry.Unregister(this); }

    public IReadOnlyList<MilitaryCommanderAssignment> Assignments => assignments;

    public bool TryAssignGovernor(Civilization owner, Governor governor, string formationId, CommandRole role, out string reason)
    {
        reason = string.Empty;
        if (owner == null || governor == null || string.IsNullOrEmpty(formationId) || !owner.governors.Contains(governor))
        {
            reason = "invalid governor assignment";
            return false;
        }
        return TryAssign(owner, CommanderCharacterKind.Governor, governor.Id, formationId, role, out reason);
    }

    public bool TryAssignAdmiral(Civilization owner, AdmiralInstance admiral, string formationId, CommandRole role, out string reason)
    {
        reason = string.Empty;
        if (owner == null || admiral == null || admiral.status != AdmiralStatus.Active || string.IsNullOrEmpty(formationId))
        {
            reason = "invalid admiral assignment";
            return false;
        }
        return TryAssign(owner, CommanderCharacterKind.Admiral, admiral.admiralId, formationId, role, out reason);
    }

    public bool RemoveAssignment(string formationId)
    {
        bool removed=false;
        for (int i=0;i<assignments.Count;i++) if (assignments[i].IsActive && assignments[i].FormationId==formationId)
        { assignments[i].IsActive=false; removed=true; SynchronizeCharacter(assignments[i]); }
        return removed;
    }

    public bool RemoveAssignment(string formationId, CommandRole role)
    {
        bool removed=false;
        for (int i=0;i<assignments.Count;i++) if (assignments[i].IsActive && assignments[i].FormationId==formationId && assignments[i].Role==role)
        { assignments[i].IsActive=false; removed=true; SynchronizeCharacter(assignments[i]); }
        return removed;
    }

    public bool TryReassign(string assignmentId, string newFormationId, out string reason)
    {
        reason=string.Empty;
        if (string.IsNullOrEmpty(newFormationId)) { reason="formation identity is required"; return false; }
        var assignment=assignments.Find(a=>a.AssignmentId==assignmentId && a.IsActive);
        if (assignment==null) { reason="active commander assignment not found"; return false; }
        for (int i=0;i<assignments.Count;i++) if (assignments[i].IsActive && assignments[i]!=assignment
            && assignments[i].FormationId==newFormationId && assignments[i].Role==assignment.Role)
        { reason="command role is already filled on the destination formation"; return false; }
        assignment.FormationId=newFormationId; assignment.AssignedTurn=GameManager.Instance!=null?GameManager.Instance.currentTurn:0; return true;
    }

    public bool ReleaseOrRescue(int characterId, CommanderCharacterKind kind)
    {
        bool changed=false;
        for (int i=0;i<assignments.Count;i++)
        {
            var a=assignments[i]; if (a.CharacterId!=characterId || a.CharacterKind!=kind || a.Status!=BattleCommanderStatus.Captured) continue;
            a.Status=BattleCommanderStatus.Active; a.CapturedByCivilizationId=-1; changed=true; SynchronizeCharacter(a);
        }
        return changed;
    }

    public void EndFormation(string formationId)
    { for (int i=0;i<assignments.Count;i++) if (assignments[i].IsActive && assignments[i].FormationId==formationId) { assignments[i].IsActive=false; SynchronizeCharacter(assignments[i]); } }

    public MilitaryCommanderAssignment GetAssignment(string formationId)
    {
        for (int i = 0; i < assignments.Count; i++)
            if (assignments[i].IsActive && assignments[i].FormationId == formationId)
                return assignments[i];
        return null;
    }

    public IReadOnlyList<MilitaryCommanderAssignment> GetAssignments(string formationId)
    {
        var result = new List<MilitaryCommanderAssignment>();
        int turn = GameManager.Instance != null ? GameManager.Instance.currentTurn : 0;
        for (int i = 0; i < assignments.Count; i++)
        {
            var assignment = assignments[i];
            if (assignment.Status == BattleCommanderStatus.Wounded && assignment.WoundedUntilTurn <= turn)
            { assignment.Status = BattleCommanderStatus.Active; SynchronizeCharacter(assignment); }
            if (assignment.IsActive && !HasValidSourceCharacter(assignment))
            { assignment.IsActive=false; SynchronizeCharacter(assignment); }
            if (assignment.IsActive && assignment.FormationId == formationId)
                result.Add(assignment);
        }
        return result;
    }

    public float GetAttackMultiplier(string formationId, BattleDomain? domain = null)
    {
        float multiplier = 1f;
        var formationAssignments = GetAssignments(formationId);
        for (int i = 0; i < formationAssignments.Count; i++)
        {
            var assignment = formationAssignments[i];
            if (assignment.Status != BattleCommanderStatus.Active) continue;
            if (domain.HasValue && !RoleApplies(assignment.Role, domain.Value)) continue;
            if (assignment.CharacterKind == CommanderCharacterKind.Governor)
            {
                var governor = FindGovernor(assignment);
                if (governor != null) multiplier *= 1f + Mathf.Max(0, governor.GetTotalBonuses().combat) / 100f;
            }
            else
            {
                var admiral = AdmiralManager.Instance?.GetAdmiral(assignment.CharacterId);
                if (admiral != null && admiral.status == AdmiralStatus.Active)
                    multiplier *= 1f + AdmiralManager.Instance.GetTacticsPercentBonus(admiral.admiralId) / 100f;
            }
        }
        return multiplier;
    }

    public float GetDefenseMultiplier(string formationId, BattleDomain? domain = null) => GetAttackMultiplier(formationId, domain);

    private static bool RoleApplies(CommandRole role, BattleDomain domain) => role == CommandRole.OverallCommander || (role switch
    {
        CommandRole.LandCommander => domain == BattleDomain.Land,
        CommandRole.NavalCommander => domain == BattleDomain.NavalSurface,
        CommandRole.UnderwaterCommander => domain == BattleDomain.Underwater,
        CommandRole.AirCommander => domain == BattleDomain.Air,
        CommandRole.OrbitalCommander => domain == BattleDomain.Orbit,
        CommandRole.SpaceCommander => domain == BattleDomain.Space,
        _ => false,
    });

    public void AwardBattleExperience(string formationId, int amount)
    {
        if (amount <= 0) return;
        var formationAssignments = GetAssignments(formationId);
        for (int i = 0; i < formationAssignments.Count; i++)
            if (formationAssignments[i].Status == BattleCommanderStatus.Active)
            {
                if (formationAssignments[i].CharacterKind == CommanderCharacterKind.Governor)
                    FindGovernor(formationAssignments[i])?.GainExperience(amount);
                else AdmiralManager.Instance?.AwardExperience(formationAssignments[i].CharacterId, amount);
            }
    }

    private bool TryAssign(Civilization owner, CommanderCharacterKind kind, int characterId, string formationId, CommandRole role, out string reason)
    {
        for (int i = 0; i < assignments.Count; i++)
        {
            var existing = assignments[i];
            if (!existing.IsActive) continue;
            if (existing.CharacterKind == kind && existing.CharacterId == characterId)
            {
                reason = "character already commands an active formation";
                return false;
            }
            if (existing.FormationId == formationId && existing.Role == role)
                existing.IsActive = false;
        }

        assignments.Add(new MilitaryCommanderAssignment
        {
            AssignmentId = Guid.NewGuid().ToString("N"),
            FormationId = formationId,
            Role = role,
            CharacterKind = kind,
            CharacterId = characterId,
            OwnerCivilizationId = CivilizationManager.Instance != null ? CivilizationManager.Instance.GetCivIndex(owner) : -1,
            AssignedTurn = GameManager.Instance != null ? GameManager.Instance.currentTurn : 0,
        });
        reason = string.Empty;
        return true;
    }

    public void ResolveBattleFate(string formationId, bool formationDestroyed, int enemyCivilizationId, int deterministicRoll)
        => ResolveBattleFate(formationId, formationDestroyed ? 1f : 0f, false, formationDestroyed, true, enemyCivilizationId, deterministicRoll);

    public void ResolveBattleFate(string formationId, float casualtyRate, bool retreated, bool losingSide, bool participated, int enemyCivilizationId, int deterministicRoll)
    {
        if (!participated) return;
        var active = GetAssignments(formationId);
        for (int i = 0; i < active.Count; i++)
        {
            var assignment = active[i];
            if (assignment.CharacterKind == CommanderCharacterKind.Admiral && AdmiralManager.Instance != null)
            {
                var admiral = AdmiralManager.Instance.GetAdmiral(assignment.CharacterId);
                int roll = Mathf.Abs(deterministicRoll + assignment.CharacterId * 31) % 100;
                int danger = Mathf.Clamp(Mathf.RoundToInt(casualtyRate*55f)+(losingSide?15:0)+(retreated?8:0),0,85);
                if (admiral != null)
                {
                    admiral.assignedFleetId = -1;
                    admiral.status = roll >= danger ? AdmiralStatus.Active : roll < danger*20/100 ? AdmiralStatus.Killed : roll < danger*55/100 ? AdmiralStatus.Captured : AdmiralStatus.Wounded;
                    if (admiral.status == AdmiralStatus.Wounded) admiral.woundedTurnsRemaining = AdmiralManager.Instance.woundedTurns;
                    if (admiral.status == AdmiralStatus.Captured) admiral.capturedByCivilizationId = enemyCivilizationId;
                }
                assignment.Status = admiral == null ? BattleCommanderStatus.Killed : admiral.status switch
                {
                    AdmiralStatus.Wounded => BattleCommanderStatus.Wounded,
                    AdmiralStatus.Captured => BattleCommanderStatus.Captured,
                    AdmiralStatus.Killed => BattleCommanderStatus.Killed,
                    _ => BattleCommanderStatus.Active,
                };
            }
            else
            {
                int roll = Mathf.Abs(deterministicRoll + assignment.CharacterId * 31) % 100;
                int danger = Mathf.Clamp(Mathf.RoundToInt(casualtyRate*55f)+(losingSide?15:0)+(retreated?8:0),0,85);
                assignment.Status = roll >= danger ? BattleCommanderStatus.Active : roll < danger*20/100 ? BattleCommanderStatus.Killed : roll < danger*55/100 ? BattleCommanderStatus.Captured : BattleCommanderStatus.Wounded;
                if (assignment.Status == BattleCommanderStatus.Wounded)
                    assignment.WoundedUntilTurn = (GameManager.Instance != null ? GameManager.Instance.currentTurn : 0) + 3;
                if (assignment.Status == BattleCommanderStatus.Captured)
                    assignment.CapturedByCivilizationId = enemyCivilizationId;
            }
            if (assignment.Status == BattleCommanderStatus.Killed) assignment.IsActive = false;
            SynchronizeCharacter(assignment);
        }
    }

    public string CaptureStateJson() => JsonUtility.ToJson(new SaveData { assignments = new List<MilitaryCommanderAssignment>(assignments) });
    public void RestoreStateJson(string json)
    {
        var data = string.IsNullOrEmpty(json) ? null : JsonUtility.FromJson<SaveData>(json);
        assignments = data?.assignments ?? new List<MilitaryCommanderAssignment>();
    }

    private static Governor FindGovernor(MilitaryCommanderAssignment assignment)
    {
        if (CivilizationManager.Instance == null) return null;
        foreach (var civ in CivilizationManager.Instance.GetAllCivs())
            if (civ != null && civ.governors != null)
                for (int i = 0; i < civ.governors.Count; i++)
                    if (civ.governors[i]?.Id == assignment.CharacterId)
                        return civ.governors[i];
        return null;
    }

    private static bool HasValidSourceCharacter(MilitaryCommanderAssignment assignment)
    {
        if (assignment.CharacterKind==CommanderCharacterKind.Admiral)
        {
            var admiral=AdmiralManager.Instance?.GetAdmiral(assignment.CharacterId);
            return admiral!=null && admiral.ownerCivilizationId==assignment.OwnerCivilizationId && admiral.status!=AdmiralStatus.Killed;
        }
        if (CivilizationManager.Instance==null) return false;
        foreach (var civ in CivilizationManager.Instance.GetAllCivs())
            if (civ!=null && CivilizationManager.Instance.GetCivIndex(civ)==assignment.OwnerCivilizationId && civ.governors!=null)
                for(int i=0;i<civ.governors.Count;i++) if(civ.governors[i]?.Id==assignment.CharacterId) return true;
        return false;
    }

    private static void SynchronizeCharacter(MilitaryCommanderAssignment assignment)
    {
        if (assignment.CharacterKind != CommanderCharacterKind.Admiral || AdmiralManager.Instance == null) return;
        var admiral=AdmiralManager.Instance.GetAdmiral(assignment.CharacterId); if (admiral==null) { assignment.IsActive=false; return; }
        admiral.assignedFleetId=assignment.IsActive?StableFormationNumber(assignment.FormationId):-1;
        admiral.status=assignment.Status switch { BattleCommanderStatus.Wounded=>AdmiralStatus.Wounded,
            BattleCommanderStatus.Captured=>AdmiralStatus.Captured, BattleCommanderStatus.Killed=>AdmiralStatus.Killed, _=>AdmiralStatus.Active };
        admiral.capturedByCivilizationId=assignment.CapturedByCivilizationId;
    }

    private static int StableFormationNumber(string formationId)
    { unchecked { int hash=17; if (formationId!=null) for(int i=0;i<formationId.Length;i++) hash=hash*31+formationId[i]; return hash; } }
}
