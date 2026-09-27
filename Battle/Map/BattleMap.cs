using System.Collections.Generic;

public sealed class BattleMap
{
    public readonly List<BattleCell> Cells = new();

    private readonly Dictionary<(int q, int r), int> axialToBattle = new();

    public int CellCount => Cells.Count;

    public void AddCell(BattleCell cell)
    {
        Cells.Add(cell);
        axialToBattle[(cell.LocalQ, cell.LocalR)] = cell.BattleIndex;
    }

    public bool TryGetBattleIndex(int localQ, int localR, out int battleIndex) =>
        axialToBattle.TryGetValue((localQ, localR), out battleIndex);

    public static int AxialDistance(BattleCell a, BattleCell b) =>
        a == null || b == null ? int.MaxValue :
        (System.Math.Abs(a.LocalQ - b.LocalQ) + System.Math.Abs(a.LocalR - b.LocalR) + System.Math.Abs(a.LocalS - b.LocalS)) / 2;

    public BattleCell GetCell(int index)
    {
        if (index < 0 || index >= Cells.Count)
            return null;

        return Cells[index];
    }
}
