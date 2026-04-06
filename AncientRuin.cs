using UnityEngine;

/// <summary>
/// Component placed on a ruin prefab. Carries the RuinData asset so the ruin is
/// self-describing in the scene hierarchy, and destroys itself when explored.
/// AncientRuinsManager sets ruinData at spawn time via AddComponent or GetComponent.
/// </summary>
public class AncientRuin : MonoBehaviour
{
    [Tooltip("The ScriptableObject describing this ruin's type and rewards. Set automatically by AncientRuinsManager at spawn time.")]
    public RuinData ruinData;

    /// <summary>
    /// Called by AncientRuinsManager.DiscoverRuin when a civilization explores this ruin.
    /// Destroys the GameObject so the ruin disappears from the map.
    /// </summary>
    public void OnExplored()
    {
        Destroy(gameObject);
    }
}
