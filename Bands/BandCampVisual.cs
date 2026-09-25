using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Semantic attachment points supplied by every culture-specific camp prefab.</summary>
public enum BandStructureVisualSlot
{
    Generic = 0,
    ForagingTent = 1,
    StoryCircle = 2,
    BurialPit = 3,
    StonePile = 4,
    ToolMaker = 5,
    FishingTent = 6
}

[Serializable]
public sealed class BandStructureVisualSocket
{
    public BandStructureVisualSlot slot;
    public Transform anchor;
    [Tooltip("Lower values are used first when a camp has multiple sockets of the same type.")]
    public int priority;
}

/// <summary>
/// Prefab-facing contract for a Band's encamped visual. It contains presentation only;
/// completed structures remain owned by Band.builtStructures.
/// </summary>
public sealed class BandCampVisual : MonoBehaviour
{
    [SerializeField] private List<BandStructureVisualSocket> structureSockets = new List<BandStructureVisualSocket>();
    [Header("Technology visuals")]
    [SerializeField, Tooltip("The complete campfire hierarchy. It must include every fire-only prop and start inactive.")]
    private Transform fireRoot;

    private bool fireStateApplied;
    private bool fireUnlocked;
    private bool missingFireRootWarningLogged;

    /// <summary>Applies the research-derived fire state without affecting structure sockets.</summary>
    public void SetFireUnlocked(bool unlocked, bool playIgnition)
    {
        if (fireRoot == null)
        {
            if (!missingFireRootWarningLogged)
            {
                Debug.LogWarning($"[BandCampVisual] '{name}' has no Fire Root assigned; the Band campfire cannot be displayed.", this);
                missingFireRootWarningLogged = true;
            }
            return;
        }

        bool changed = !fireStateApplied || fireUnlocked != unlocked;
        if (!changed) return;

        fireStateApplied = true;
        fireUnlocked = unlocked;
        fireRoot.gameObject.SetActive(unlocked);
    }

    public bool TryGetSocket(BandStructureVisualSlot slot, ISet<Transform> occupied, out Transform anchor)
    {
        anchor = structureSockets
            .Where(x => x != null && x.slot == slot && x.anchor != null && (occupied == null || !occupied.Contains(x.anchor)))
            .OrderBy(x => x.priority)
            .Select(x => x.anchor)
            .FirstOrDefault();
        return anchor != null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (fireRoot == null)
            Debug.LogWarning($"[BandCampVisual] '{name}' has no Fire Root assigned. Assign the complete, initially-disabled campfire hierarchy.", this);
        else if (fireRoot.gameObject.activeSelf)
            Debug.LogWarning($"[BandCampVisual] Fire Root on '{name}' should start disabled; its state is derived from research at runtime.", this);

        foreach (var socket in structureSockets)
            if (socket != null && socket.anchor == null)
                Debug.LogWarning($"[BandCampVisual] A {socket.slot} socket on '{name}' has no anchor.", this);
    }
#endif
}
