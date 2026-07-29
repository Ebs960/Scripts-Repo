public static class BattleCoverResolver
{
    public static void GetCover(BattleCell cell, out bool softCover, out bool hardCover)
    {
        softCover = cell != null && cell.HasSoftCover;
        hardCover = cell != null && cell.HasHardCover;
    }
}
