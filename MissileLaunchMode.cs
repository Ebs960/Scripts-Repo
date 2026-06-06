// Assets/Scripts Repo/MissileLaunchMode.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Singleton that manages the "launch mode" input state for missiles.
/// When active, tiles within missile range are highlighted with range overlays.
/// Right-clicking a valid in-range tile fires the selected missile.
/// Escape cancels. Opened by MissilePanelUI after the player selects a missile.
/// </summary>
public class MissileLaunchMode : MonoBehaviour
{
    public static MissileLaunchMode Instance { get; private set; }

    // ─── Inspector ───────────────────────────────────────────────────────────
    [Header("Overlays")]
    [Tooltip("Prefab placed on every tile within the missile's range to highlight it.")]
    public GameObject rangeOverlayPrefab;

    [Tooltip("Prefab placed on the currently hovered target tile (replaced each frame when tile changes).")]
    public GameObject targetOverlayPrefab;

    // ─── State ───────────────────────────────────────────────────────────────
    private bool _active = false;
    private MissileData _missile;
    private int _sourceTile;
    private int _planetIndex;

    private enum SourceType { City, Unit, Silo }
    private SourceType _sourceType;
    private City _city;
    private CombatUnit _unit;
    private int _siloTile;

    private Action _onCancelled;

    private readonly List<GameObject> _rangeOverlays = new List<GameObject>();
    private GameObject _targetOverlay;
    private int _lastHoveredTile = -1;

    public bool IsActive => _active;

    // ─── Lifecycle ───────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        if (!_active) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cancel();
            return;
        }

        RefreshTargetOverlay();

        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            TryFire();
    }

    // ─── Entry Points ─────────────────────────────────────────────────────────
    public void BeginLaunchFromCity(City city, MissileData missile, Action onCancelled = null)
    {
        _sourceType = SourceType.City;
        _city  = city;
        _unit  = null;
        _sourceTile   = city.centerTileIndex;
        _planetIndex  = city.planetIndex;
        Activate(missile, onCancelled);
    }

    public void BeginLaunchFromUnit(CombatUnit unit, MissileData missile, Action onCancelled = null)
    {
        _sourceType = SourceType.Unit;
        _unit  = unit;
        _city  = null;
        _sourceTile  = unit.currentTileIndex;
        _planetIndex = unit.planetIndex;
        Activate(missile, onCancelled);
    }

    public void BeginLaunchFromSilo(int siloTileIndex, int planetIndex, MissileData missile, Action onCancelled = null)
    {
        _sourceType  = SourceType.Silo;
        _siloTile    = siloTileIndex;
        _city  = null;
        _unit  = null;
        _sourceTile  = siloTileIndex;
        _planetIndex = planetIndex;
        Activate(missile, onCancelled);
    }

    // ─── Activate / Deactivate ────────────────────────────────────────────────
    private void Activate(MissileData missile, Action onCancelled)
    {
        _missile     = missile;
        _onCancelled = onCancelled;
        _active      = true;
        SpawnRangeOverlays();
    }

    public void Cancel()
    {
        Deactivate();
        _onCancelled?.Invoke();
    }

    private void Deactivate()
    {
        _active  = false;
        _missile = null;
        ClearAllOverlays();
    }

    // ─── Range Highlights ────────────────────────────────────────────────────
    private void SpawnRangeOverlays()
    {
        ClearAllOverlays();
        if (rangeOverlayPrefab == null || _missile == null) return;

        var ts = TileSystem.GetForPlanet(_planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;

        var tiles = MissileManager.GetTilesInMissileRange(ts, _sourceTile, _missile.range);
        foreach (int idx in tiles)
        {
            var go = Instantiate(rangeOverlayPrefab, ts.GetTileCenterFlat(idx), Quaternion.identity);
            _rangeOverlays.Add(go);
        }
    }

    private void RefreshTargetOverlay()
    {
        if (targetOverlayPrefab == null) return;
        var ts = TileSystem.GetForPlanet(_planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;

        int hovered = GetHoveredTile(ts);
        if (hovered == _lastHoveredTile) return;
        _lastHoveredTile = hovered;

        if (_targetOverlay != null) { Destroy(_targetOverlay); _targetOverlay = null; }

        if (hovered >= 0 && IsValidTarget(ts, hovered))
            _targetOverlay = Instantiate(targetOverlayPrefab, ts.GetTileCenterFlat(hovered), Quaternion.identity);
    }

    // ─── Fire ────────────────────────────────────────────────────────────────
    private void TryFire()
    {
        var ts = TileSystem.GetForPlanet(_planetIndex) ?? TileSystem.Instance;
        if (ts == null || _missile == null) return;

        int target = GetHoveredTile(ts);
        if (target < 0 || !IsValidTarget(ts, target))
        {
            Debug.Log("[MissileLaunchMode] No valid target under cursor.");
            return;
        }

        switch (_sourceType)
        {
            case SourceType.City:
                MissileManager.Instance.LaunchFromCity(_city, _missile, target);
                break;
            case SourceType.Unit:
                MissileManager.Instance.LaunchFromUnit(_unit, _missile, target);
                break;
            case SourceType.Silo:
                MissileManager.Instance.LaunchFromSilo(_siloTile, _planetIndex, _missile, target);
                break;
        }

        Deactivate();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────
    private bool IsValidTarget(TileSystem ts, int tileIndex) =>
        _missile != null && MissileManager.IsInMissileRange(ts, _sourceTile, tileIndex, _missile.range);

    /// <summary>
    /// Gets the tile index under the mouse cursor using TileSystem's picking system,
    /// matching the same approach used by UnitSelectionManager.
    /// </summary>
    private int GetHoveredTile(TileSystem ts)
    {
        if (ts == null) return -1;
        var result = ts.GetMouseHitInfo();
        return result.hit ? result.tileIndex : -1;
    }

    private void ClearAllOverlays()
    {
        foreach (var go in _rangeOverlays) if (go != null) Destroy(go);
        _rangeOverlays.Clear();
        if (_targetOverlay != null) { Destroy(_targetOverlay); _targetOverlay = null; }
        _lastHoveredTile = -1;
    }
}
