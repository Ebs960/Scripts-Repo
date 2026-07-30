using System;
using System.Collections.Generic;

public readonly struct BattleOccupancyKey : IEquatable<BattleOccupancyKey>
{
    public readonly int CellIndex;
    public readonly BattleDomain Domain;
    public readonly int Band;

    public BattleOccupancyKey(int cellIndex, BattleDomain domain, int band = 0)
    { CellIndex = cellIndex; Domain = domain; Band = band; }

    public bool Equals(BattleOccupancyKey other) => CellIndex == other.CellIndex && Domain == other.Domain && Band == other.Band;
    public override bool Equals(object obj) => obj is BattleOccupancyKey other && Equals(other);
    public override int GetHashCode() => ((CellIndex * 397) ^ (int)Domain) * 397 ^ Band;
}

/// <summary>Layered occupancy: domains can share coordinates, slots within a domain cannot.</summary>
public sealed class BattleOccupancy
{
    private readonly Dictionary<BattleOccupancyKey, BattleUnitState> occupants = new();

    public bool IsOccupied(int cellIndex) => IsOccupied(cellIndex, BattleDomain.Land, 0);
    public bool IsOccupied(int cellIndex, BattleDomain domain, int band = 0) => occupants.ContainsKey(new BattleOccupancyKey(cellIndex, domain, band));
    public BattleUnitState GetOccupant(int cellIndex) => GetOccupant(cellIndex, BattleDomain.Land, 0);
    public BattleUnitState GetOccupant(int cellIndex, BattleDomain domain, int band = 0)
    { occupants.TryGetValue(new BattleOccupancyKey(cellIndex, domain, band), out var unit); return unit; }

    public bool CanEnter(BattleUnitState unit, int cellIndex, BattleMap map)
    {
        if (unit == null || map == null || unit.IsEmbarked) return false;
        var cell = map.GetCell(cellIndex);
        if (cell == null || !cell.Supports(unit.Domain)) return false;
        var key = new BattleOccupancyKey(cellIndex, unit.Domain, unit.OccupancyBand);
        return !occupants.TryGetValue(key, out var occupant) || occupant == unit;
    }

    public bool TryMove(BattleUnitState unit, int destination, BattleMap map)
    {
        if (!CanEnter(unit, destination, map)) return false;
        RemoveAtCurrentPosition(unit);
        unit.CellIndex = destination;
        occupants[new BattleOccupancyKey(destination, unit.Domain, unit.OccupancyBand)] = unit;
        return true;
    }

    public void Remove(BattleUnitState unit)
    { if (unit == null) return; RemoveAtCurrentPosition(unit); unit.CellIndex = -1; }

    private void RemoveAtCurrentPosition(BattleUnitState unit)
    {
        if (unit.CellIndex >= 0)
            occupants.Remove(new BattleOccupancyKey(unit.CellIndex, unit.Domain, unit.OccupancyBand));
    }
}
