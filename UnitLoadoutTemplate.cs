using System;
using System.Collections.Generic;
using GameCombat;

[Flags]
public enum LoadoutSlotMask
{
    None = 0, Weapon = 1, Shield = 2, Body = 4, Head = 8, Tool = 16, Miscellaneous = 32
}

/// <summary>An archetype template. configuredSlots distinguishes untouched slots from explicit None.</summary>
[Serializable]
public sealed class UnitLoadoutTemplate
{
    public string archetypeId;
    public bool workerArchetype;
    public EquipmentData weapon;
    public EquipmentData shield;
    public EquipmentData body;
    public EquipmentData head;
    public EquipmentData tool;
    public EquipmentData miscellaneous;
    public ProjectileData projectile;
    public LoadoutSlotMask configuredSlots;
    public bool useForNewUnits;

    public bool IsConfigured(EquipmentType slot) => (configuredSlots & ToMask(slot)) != 0;
    public void Set(EquipmentType slot, EquipmentData item)
    {
        configuredSlots |= ToMask(slot);
        switch (slot)
        {
            case EquipmentType.Weapon: weapon = item; if (item == null || !item.usesProjectiles) projectile = null; break;
            case EquipmentType.Shield: shield = item; break;
            case EquipmentType.Body: body = item; break;
            case EquipmentType.Head: head = item; break;
            case EquipmentType.Tool: tool = item; break;
            case EquipmentType.Miscellaneous: miscellaneous = item; break;
        }
    }
    public EquipmentData Get(EquipmentType slot)
    {
        switch (slot)
        {
            case EquipmentType.Weapon: return weapon;
            case EquipmentType.Shield: return shield;
            case EquipmentType.Body: return body;
            case EquipmentType.Head: return head;
            case EquipmentType.Tool: return tool;
            case EquipmentType.Miscellaneous: return miscellaneous;
            default: return null;
        }
    }
    public static LoadoutSlotMask ToMask(EquipmentType slot)
    {
        switch (slot)
        {
            case EquipmentType.Weapon: return LoadoutSlotMask.Weapon;
            case EquipmentType.Shield: return LoadoutSlotMask.Shield;
            case EquipmentType.Body: return LoadoutSlotMask.Body;
            case EquipmentType.Head: return LoadoutSlotMask.Head;
            case EquipmentType.Tool: return LoadoutSlotMask.Tool;
            case EquipmentType.Miscellaneous: return LoadoutSlotMask.Miscellaneous;
            default: return LoadoutSlotMask.None;
        }
    }
    public static readonly EquipmentType[] Slots = { EquipmentType.Weapon, EquipmentType.Shield, EquipmentType.Body, EquipmentType.Head, EquipmentType.Tool, EquipmentType.Miscellaneous };
}

[Serializable]
public struct LoadoutRequirement
{
    public EquipmentData equipment;
    public int matchingUnits, alreadyEquipped, required, available, returned, shortage;
}
