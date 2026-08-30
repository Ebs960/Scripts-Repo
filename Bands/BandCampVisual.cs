using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Semantic attachment points supplied by every culture-specific camp prefab.</summary>
public enum BandStructureVisualSlot
{
    Generic,
    ForagingTent,
    StoryCircle,
    BurialPit,
    StonePile,
    ToolMaker,
    FishingTent
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
        foreach (var socket in structureSockets)
            if (socket != null && socket.anchor == null)
                Debug.LogWarning($"[BandCampVisual] A {socket.slot} socket on '{name}' has no anchor.", this);
    }
#endif
}
