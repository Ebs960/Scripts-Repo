using System.Collections.Generic;

public sealed class BattleMap
{
    public readonly List<BattleCell> Cells = new();

    private readonly Dictionary<int, int> campaignToBattle = new();

    public int CellCount => Cells.Count;

    public void AddCell(BattleCell cell)
    {
        Cells.Add(cell);
        campaignToBattle[cell.CampaignTileIndex] = cell.BattleIndex;
    }

    public bool TryGetBattleIndexForCampaignTile(int campaignTile, out int battleIndex)
    {
        return campaignToBattle.TryGetValue(campaignTile, out battleIndex);
    }

    public BattleCell GetCell(int index)
    {
        if (index < 0 || index >= Cells.Count)
            return null;

        return Cells[index];
    }
}
