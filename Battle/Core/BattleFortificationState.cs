using System;

[Serializable]
public sealed class BattleFortificationState
{
    public int StructureId;
    public BattleFortificationKind Kind;
    public int CellIndex;
    /// <summary>For walls and gates, the two local cells separated by this structure.</summary>
    public int CellA = -1;
    public int CellB = -1;
    public int CurrentHitPoints;
    public int MaxHitPoints;
    public int Defense;
    public bool IsBreached;

    public bool BlocksMovement => !IsBreached && (Kind == BattleFortificationKind.Wall || Kind == BattleFortificationKind.Gate);

    public bool ProtectsEdge(int from, int to) => BlocksMovement &&
        ((CellA == from && CellB == to) || (CellA == to && CellB == from));

    public int ApplyDamage(int damage)
    {
        if (IsBreached || damage <= 0) return 0;
        int applied = Math.Min(CurrentHitPoints, damage);
        CurrentHitPoints -= applied;
        if (CurrentHitPoints <= 0) { CurrentHitPoints = 0; IsBreached = true; }
        return applied;
    }
}
