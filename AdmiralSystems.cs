using System;
using System.Collections.Generic;
using UnityEngine;

public enum AdmiralAbilityKind { Passive, Activated }
public enum AdmiralStatus { Active, Wounded, Captured, Killed }
public enum AdmiralFleetLossOutcome { Escaped, Wounded, Captured, Killed }

[CreateAssetMenu(fileName = "AdmiralData", menuName = "Data/Space/Admiral Data")]
public class AdmiralData : ScriptableObject
{
    public string admiralId;
    public string admiralName;
    public Sprite portrait;
    public int command = 3;
    public int tactics = 1;
    public int logistics = 1;
    public int engineering = 1;
    public int recon = 1;
    public List<string> defaultTraitIds = new List<string>();
    public List<AdmiralAbilityData> possibleAbilities = new List<AdmiralAbilityData>();
}

[CreateAssetMenu(fileName = "AdmiralAbilityData", menuName = "Data/Space/Admiral Ability")]
public class AdmiralAbilityData : ScriptableObject
{
    public string abilityId;
    public string displayName;
    [TextArea] public string description;
    public AdmiralAbilityKind abilityKind = AdmiralAbilityKind.Passive;
    [Range(0f, 0.5f)] public float attackBonusPercent;
    [Range(0f, 0.5f)] public float defenseBonusPercent;
    public int reconRangeBonus;
    public int repairAmountBonus;
    public int cooldownTurns;
}

[Serializable]
public class AdmiralInstance
{
    public int admiralId;
    public string admiralName;
    public int ownerCivilizationId;
    public int level = 1;
    public int experience;
    public int command;
    public int tactics;
    public int logistics;
    public int engineering;
    public int recon;
    public List<string> unlockedAbilityIds = new List<string>();
    public int assignedFleetId = -1;
    public AdmiralStatus status = AdmiralStatus.Active;
    public int woundedTurnsRemaining;
    public int capturedByCivilizationId = -1;
}

public class AdmiralManager : MonoBehaviour
{
    public static AdmiralManager Instance { get; private set; }
    public List<AdmiralInstance> admirals = new List<AdmiralInstance>();
    public int experiencePerLevel = 100;
    public int maximumStatBonusPercent = 25;
    [Header("Admiral Fate")]
    [Range(0f, 1f)] public float escapeChanceOnFleetDestroyed = 0.45f;
    [Range(0f, 1f)] public float woundedChanceOnFleetDestroyed = 0.25f;
    [Range(0f, 1f)] public float capturedChanceOnFleetDestroyed = 0.20f;
    public int woundedTurns = 3;
    public float woundedFleetAttackPenalty = 0.15f;
    private int nextAdmiralId = 1;
    private void Awake() { if (Instance != null && Instance != this) { Destroy(gameObject); return; } Instance = this; }
    private void OnDestroy() { if (Instance == this) Instance = null; }
    public AdmiralInstance CreateAdmiral(AdmiralData data, int ownerCivilizationId)
    {
        var admiral = new AdmiralInstance { admiralId = nextAdmiralId++, admiralName = data != null ? data.admiralName : "Admiral", ownerCivilizationId = ownerCivilizationId, command = data != null ? data.command : 3, tactics = data != null ? data.tactics : 1, logistics = data != null ? data.logistics : 1, engineering = data != null ? data.engineering : 1, recon = data != null ? data.recon : 1 };
        if (data != null) foreach (var ability in data.possibleAbilities) if (ability != null && !string.IsNullOrEmpty(ability.abilityId)) admiral.unlockedAbilityIds.Add(ability.abilityId);
        admirals.Add(admiral); return admiral;
    }
    public AdmiralInstance GetAdmiral(int admiralId) => admirals.Find(a => a.admiralId == admiralId);
    public void AwardExperience(int admiralId, int amount)
    {
        var admiral = GetAdmiral(admiralId); if (admiral == null || amount <= 0) return;
        admiral.experience += amount;
        while (admiral.experience >= experiencePerLevel * admiral.level) { admiral.experience -= experiencePerLevel * admiral.level; admiral.level++; admiral.command++; if (admiral.level % 2 == 0) admiral.tactics++; if (admiral.level % 3 == 0) admiral.logistics++; }
    }
    public int GetTacticsPercentBonus(int admiralId) { var a = GetAdmiral(admiralId); return a == null || a.status != AdmiralStatus.Active ? 0 : Mathf.Clamp(a.tactics * 2, 0, maximumStatBonusPercent); }

    public float GetFleetAttackMultiplier(int admiralId)
    {
        var a = GetAdmiral(admiralId);
        if (a == null) return 1f;
        return a.status == AdmiralStatus.Wounded ? Mathf.Max(0.1f, 1f - woundedFleetAttackPenalty) : a.status == AdmiralStatus.Active ? 1f : 0f;
    }

    public AdmiralFleetLossOutcome ResolveFleetDestroyed(int admiralId, int enemyCivilizationId = -1)
    {
        var a = GetAdmiral(admiralId); if (a == null || a.status == AdmiralStatus.Killed) return AdmiralFleetLossOutcome.Killed;
        a.assignedFleetId = -1;
        float roll = UnityEngine.Random.value;
        if (roll < escapeChanceOnFleetDestroyed) { a.status = AdmiralStatus.Active; return AdmiralFleetLossOutcome.Escaped; }
        roll -= escapeChanceOnFleetDestroyed;
        if (roll < woundedChanceOnFleetDestroyed) { a.status = AdmiralStatus.Wounded; a.woundedTurnsRemaining = woundedTurns; return AdmiralFleetLossOutcome.Wounded; }
        roll -= woundedChanceOnFleetDestroyed;
        if (roll < capturedChanceOnFleetDestroyed) { a.status = AdmiralStatus.Captured; a.capturedByCivilizationId = enemyCivilizationId; return AdmiralFleetLossOutcome.Captured; }
        a.status = AdmiralStatus.Killed; return AdmiralFleetLossOutcome.Killed;
    }

    public bool ExchangeCapturedAdmiral(int admiralId, int receivingCivilizationId)
    {
        var a = GetAdmiral(admiralId); if (a == null || a.status != AdmiralStatus.Captured) return false;
        a.ownerCivilizationId = receivingCivilizationId; a.capturedByCivilizationId = -1; a.status = AdmiralStatus.Active; return true;
    }

    public void TickRecovery()
    {
        foreach (var a in admirals) if (a.status == AdmiralStatus.Wounded && --a.woundedTurnsRemaining <= 0) a.status = AdmiralStatus.Active;
    }
}
