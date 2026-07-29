using System.Collections.Generic;

public sealed class BattleOccupancy
{
    private readonly Dictionary<int, BattleUnitState> byCell = new();

    public bool IsOccupied(int cellIndex)
    {
        return byCell.ContainsKey(cellIndex);
    }

    public BattleUnitState GetOccupant(int cellIndex)
    {
        byCell.TryGetValue(cellIndex, out var unit);
        return unit;
    }

    public bool CanEnter(BattleUnitState unit, int cellIndex, BattleMap map)
    {
        if (unit == null || map == null)
            return false;

        var cell = map.GetCell(cellIndex);
        if (cell == null || !cell.IsPassable)
            return false;

        if (cell.IsWater)
            return false;

        if (byCell.TryGetValue(cellIndex, out var occupant) && occupant != unit)
            return false;

        return true;
    }

    public bool TryMove(BattleUnitState unit, int destination, BattleMap map)
    {
        if (!CanEnter(unit, destination, map))
            return false;

        if (unit.CellIndex >= 0)
            byCell.Remove(unit.CellIndex);

        unit.CellIndex = destination;
        byCell[destination] = unit;
        return true;
    }

    public void Remove(BattleUnitState unit)
    {
        if (unit == null)
            return;

        if (unit.CellIndex >= 0)
            byCell.Remove(unit.CellIndex);

        unit.CellIndex = -1;
    }
}
