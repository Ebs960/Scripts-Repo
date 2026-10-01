using System;
using System.Collections.Generic;

[Serializable]
public struct UnitStatPreview
{
    public float attack, defense, health, movement, range, workPoints;
}

public static class EquipmentStatPreviewService
{
    public static UnitStatPreview Calculate(CombatUnitData data, UnitLoadoutTemplate loadout)
    {
        var value = new UnitStatPreview { attack = data != null ? data.baseAttack : 0, defense = data != null ? data.baseDefense : 0, health = data != null ? data.baseHealth : 0, movement = data != null ? data.baseMovePoints : 0, range = data != null ? data.baseRange : 0 };
        AddEquipment(ref value, loadout); return value;
    }
    public static UnitStatPreview Calculate(WorkerUnitData data, UnitLoadoutTemplate loadout)
    {
        var value = new UnitStatPreview { health = data != null ? data.baseHealth : 0, movement = data != null ? data.baseMovePoints : 0, workPoints = data != null ? data.baseWorkPoints : 0, range = 1 };
        AddEquipment(ref value, loadout); return value;
    }
    private static void AddEquipment(ref UnitStatPreview value, UnitLoadoutTemplate loadout)
    {
        if (loadout == null) return;
        foreach (var slot in UnitLoadoutTemplate.Slots)
        {
            var item = loadout.Get(slot); if (item == null) continue;
            value.attack += item.attackBonus; value.defense += item.defenseBonus; value.health += item.healthBonus;
            value.movement += item.movementBonus; value.range += item.rangeBonus; value.workPoints += item.workPointsBonus;
        }
    }
}
