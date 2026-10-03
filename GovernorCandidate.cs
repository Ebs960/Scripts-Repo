using System.Collections.Generic;

[System.Serializable]
public sealed class GovernorCandidate
{
    public int candidateId;
    public string name;
    public string portraitId;
    public Governor.Specialization specialization;
    public List<PersonalityTrait> personalityTraits = new List<PersonalityTrait>();
    public ReligionData personalReligion;
    public CultureData personalCulture;
    public int createdRound;

    public float StartingOpinion => Governor.CalculatePersonalityBaselineOpinion(personalityTraits);
}
