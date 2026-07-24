// Assets/Scripts/Data/NaturalDisasterCityEffect.cs

/// <summary>
/// Tracks a single active natural-disaster city-wide effect (temporary yield penalty and
/// optional population loss) on a City. Mirrors the DiseaseInstance pattern.
/// </summary>
[System.Serializable]
public class NaturalDisasterCityEffect
{
    public NaturalDisasterData data;
    public int turnsRemaining;
    public float accumulatedPopulationLoss;

    public NaturalDisasterCityEffect(NaturalDisasterData data, int duration)
    {
        this.data = data;
        turnsRemaining = duration;
        accumulatedPopulationLoss = 0f;
    }

    public bool HasExpired => turnsRemaining == 0;

    /// <summary>Ticks the effect by one turn. Returns true if still active, false if it just expired.</summary>
    public bool TickDuration()
    {
        if (turnsRemaining < 0) return true; // permanent
        turnsRemaining--;
        return turnsRemaining > 0;
    }
}
