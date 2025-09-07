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

    [Header("Type & Upgrades")]
    [Tooltip("Whether this pantheon is a spirit (early/weak version). First pantheons are spirits.")]
    public bool isSpirit = true;
    [Tooltip("Whether this pantheon (if a spirit) can be upgraded into a God-level pantheon")]
    public bool canUpgradeToGod = false;
    [Tooltip("Optional reference to the upgraded pantheon (God) this spirit becomes when upgraded")]
    public PantheonData upgradedPantheon;
} 