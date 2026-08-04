public sealed class BattleDeterministicRandom
{
    private uint state;

    public BattleDeterministicRandom(int seed)
    {
        state = unchecked((uint)seed);
        if (state == 0)
            state = 0x6D2B79F5u;
    }

    public float NextUnitFloat()
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0x00FFFFFFu) / 16777216f;
    }

    public uint CaptureState() => state;
    public void RestoreState(uint value) => state = value == 0 ? 0x6D2B79F5u : value;
}
