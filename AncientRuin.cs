using UnityEngine;
using System.Linq;

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

    // --- Wrap registration so ruins get ghost copies across the horizontal buffer ---
    private HexMapChunkManager _wrapMgr;
    private bool _registeredForWrap = false;

    private void Awake()
    {
        TryRegisterForWrap();
    }

    private void TryRegisterForWrap()
    {
        if (_registeredForWrap) return;
        try
        {
            var pg = GetComponentInParent<PlanetGenerator>();
            if (pg == null)
                pg = FindObjectsByType<PlanetGenerator>().FirstOrDefault();
            if (pg == null) return;
            var grid = pg.Grid;
            if (grid == null) return;
            int tile = grid.GetTileAtPosition(transform.position);
            if (tile < 0) return;
            var mgr = FindObjectsByType<HexMapChunkManager>().FirstOrDefault(m => m.PlanetGenerator == pg);
            if (mgr == null) return;
            mgr.RegisterObjectForWrapAtTile(tile, gameObject);
            _wrapMgr = mgr;
            _registeredForWrap = true;
        }
        catch { }
    }

    private void OnDestroy()
    {
        try { if (_wrapMgr != null) _wrapMgr.UnregisterObjectForWrap(gameObject); } catch { }
    }
}
