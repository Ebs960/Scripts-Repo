using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime helper attached to instantiated improvement GameObjects to track applied upgrades
/// and any attached child parts spawned by upgrades.
/// </summary>
public class ImprovementInstance : MonoBehaviour
{
    public int tileIndex = -1;
    public ImprovementData data;
    // Track applied upgrades by id/name
    public HashSet<string> appliedUpgrades = new HashSet<string>();
    // Track instantiated child parts so we don't duplicate them
    public List<GameObject> attachedParts = new List<GameObject>();

    public bool HasApplied(string idOrName)
    {
        if (string.IsNullOrEmpty(idOrName)) return false;
        return appliedUpgrades.Contains(idOrName);
    }

    public void MarkApplied(string idOrName)
    {
        if (string.IsNullOrEmpty(idOrName)) return;
        appliedUpgrades.Add(idOrName);
    }
}
