using System;

[Serializable]
public sealed class BattleFortificationState
{
    public int StructureId;
    public BattleFortificationKind Kind;
    public int CellIndex;
    public int CurrentHitPoints;
    public int MaxHitPoints;
    public int Defense;
    public bool IsBreached;

    public bool BlocksMovement => !IsBreached && (Kind == BattleFortificationKind.Wall || Kind == BattleFortificationKind.Gate);

    public int ApplyDamage(int damage)
    {
        if (IsBreached || damage <= 0) return 0;
        int applied = Math.Min(CurrentHitPoints, damage);
        CurrentHitPoints -= applied;
        if (CurrentHitPoints <= 0) { CurrentHitPoints = 0; IsBreached = true; }
        return applied;
    }
}
