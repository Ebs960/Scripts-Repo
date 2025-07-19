using UnityEngine;

[CreateAssetMenu(menuName="CivGame/Religion/Pantheon")]
public class PantheonData : ScriptableObject
{
    [Header("Identity")]
    public string pantheonName;
    [TextArea] public string description;
    public Sprite icon;

    [Header("Cost")]
    [Tooltip("Faith required to found this Pantheon")]
    public int faithCost;

    [Header("Choices")]
    [Tooltip("Which founder beliefs you can pick as your Pantheon bonus")]
    public BeliefData[] possibleFounderBeliefs;
} 