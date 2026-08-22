using System;
using UnityEngine;

/// <summary>Central non-combat resolution for hostile contact with civilian workers.</summary>
public static class CivilianCaptureService
{
    public static event Action<WorkerUnit, Civilization, Civilization> WorkerCaptured;
    public static event Action<WorkerUnit, CombatUnit> WorkerKilledByAnimal;

    public static bool ResolveAttack(CombatUnit attacker, WorkerUnit civilian)
    {
        if (attacker == null || civilian == null || attacker.owner == civilian.owner) return false;
        if (!CivilianAttachmentService.TryDetachForHostileContact(civilian, out string placementReason))
        {
            Debug.LogWarning($"[CivilianCapture] Capture rejected: {placementReason}");
            return false;
        }
        if (attacker.data != null && attacker.data.unitType == CombatCategory.Animal)
        {
            WorkerKilledByAnimal?.Invoke(civilian, attacker);
            civilian.KillCivilian();
        }
        else
        {
            var oldOwner = civilian.owner;
            civilian.TransferCivilianOwnership(attacker.owner);
            WorkerCaptured?.Invoke(civilian, oldOwner, attacker.owner);
        }
        return true;
    }
}
