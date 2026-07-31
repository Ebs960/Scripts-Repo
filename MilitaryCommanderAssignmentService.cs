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
}

public sealed class MilitaryCommanderAssignmentService : MonoBehaviour
{
    public static MilitaryCommanderAssignmentService Instance { get; private set; }
    [SerializeField] private List<MilitaryCommanderAssignment> assignments = new();

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
    }

    private void OnDestroy() { if (Instance == this) Instance = null; }

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

    public float GetAttackMultiplier(string formationId)
    {
        var assignment = GetAssignment(formationId);
        if (assignment == null) return 1f;
        if (assignment.CharacterKind == CommanderCharacterKind.Governor)
        {
            var governor = FindGovernor(assignment);
            return governor == null ? 1f : 1f + Mathf.Max(0, governor.GetTotalBonuses().combat) / 100f;
        }
        var admiral = AdmiralManager.Instance?.GetAdmiral(assignment.CharacterId);
        return admiral == null || admiral.status != AdmiralStatus.Active ? 1f : 1f + AdmiralManager.Instance.GetTacticsPercentBonus(admiral.admiralId) / 100f;
    }

    public float GetDefenseMultiplier(string formationId) => GetAttackMultiplier(formationId);

    public void AwardBattleExperience(string formationId, int amount)
    {
        if (amount <= 0) return;
        var assignment = GetAssignment(formationId);
        if (assignment == null) return;
        if (assignment.CharacterKind == CommanderCharacterKind.Governor)
            FindGovernor(assignment)?.GainExperience(amount);
        else
            AdmiralManager.Instance?.AwardExperience(assignment.CharacterId, amount);
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
            if (existing.FormationId == formationId)
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