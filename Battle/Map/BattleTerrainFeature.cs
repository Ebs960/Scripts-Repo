using System;

[Flags]
public enum BattleTerrainFeature
{
    None = 0,
    Forest = 1 << 0,
    RoughGround = 1 << 1,
    Marsh = 1 << 2,
    Road = 1 << 3,
    River = 1 << 4,
    Bridge = 1 << 5,
    Ford = 1 << 6,
    Beach = 1 << 7,
    ShallowWater = 1 << 8,
    DeepWater = 1 << 9,
    Farmland = 1 << 10,
    Urban = 1 << 11,
    HardCover = 1 << 12,
    SoftCover = 1 << 13,
}
