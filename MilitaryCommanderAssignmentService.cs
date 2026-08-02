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
        var assignment = GetAssignment(formationId);
        if (assignment == null) return false;
        assignment.IsActive = false;
        return true;
    }

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
                assignment.Status = BattleCommanderStatus.Active;
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
    {
        if (!formationDestroyed) return;
        var active = GetAssignments(formationId);
        for (int i = 0; i < active.Count; i++)
        {
            var assignment = active[i];
            if (assignment.CharacterKind == CommanderCharacterKind.Admiral && AdmiralManager.Instance != null)
            {
                var admiral = AdmiralManager.Instance.GetAdmiral(assignment.CharacterId);
                int roll = Mathf.Abs(deterministicRoll + assignment.CharacterId * 31) % 100;
                if (admiral != null)
                {
                    admiral.assignedFleetId = -1;
                    admiral.status = roll < 45 ? AdmiralStatus.Active : roll < 70 ? AdmiralStatus.Wounded : roll < 88 ? AdmiralStatus.Captured : AdmiralStatus.Killed;
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
                assignment.Status = roll < 45 ? BattleCommanderStatus.Active : roll < 70 ? BattleCommanderStatus.Wounded : roll < 88 ? BattleCommanderStatus.Captured : BattleCommanderStatus.Killed;
                if (assignment.Status == BattleCommanderStatus.Wounded)
                    assignment.WoundedUntilTurn = (GameManager.Instance != null ? GameManager.Instance.currentTurn : 0) + 3;
                if (assignment.Status == BattleCommanderStatus.Captured)
                    assignment.CapturedByCivilizationId = enemyCivilizationId;
            }
            if (assignment.Status == BattleCommanderStatus.Killed) assignment.IsActive = false;
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
}
