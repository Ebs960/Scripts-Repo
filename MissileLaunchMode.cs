// Assets/Scripts Repo/MissileLaunchMode.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

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
    private Civilization _sourceOwner;
    private Civilization _siloImprovementOwner;
    private bool _confirmationPending;
    private int _inputConsumedFrame = -1;
    private GameObject _hud;
    private Image _hudIcon;
    private TMP_Text _hudName;
    private TMP_Text _hudInfo;
    private Material _rangeMaterial;
    private Material _targetMaterial;

    private readonly List<GameObject> _rangeOverlays = new List<GameObject>();
    private GameObject _targetOverlay;
    private int _lastHoveredTile = -1;

    public bool IsActive => _active;
    public bool BlocksMapInput => _active || _inputConsumedFrame == Time.frameCount;

    public void ConfigureHud(GameObject hud, Image icon, TMP_Text name, TMP_Text info)
    {
        _hud = hud;
        _hudIcon = icon;
        _hudName = name;
        _hudInfo = info;
    }

    // ─── Lifecycle ───────────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void Update()
    {
        if (!_active) return;
        if (_confirmationPending || ConfirmationDialogService.IsVisible || _inputConsumedFrame == Time.frameCount) return;

        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            Cancel();
            return;
        }

        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
        if (InputManager.Instance != null && !InputManager.Instance.IsInputEnabled()) return;

        RefreshTargetOverlay();

        if (Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame)
            TryFire();
    }

    // ─── Entry Points ─────────────────────────────────────────────────────────
    public void BeginLaunchFromCity(City city, MissileData missile, Action onCancelled = null)
    {
        if (city == null || missile == null) { Debug.LogWarning("[MissileLaunchMode] Invalid city launch source."); return; }
        if (_active) Cancel();
        _sourceType = SourceType.City;
        _city  = city;
        _unit  = null;
        _sourceTile   = city.centerTileIndex;
        _planetIndex  = city.planetIndex;
        _sourceOwner = city.owner;
        Activate(missile, onCancelled);
    }

    public void BeginLaunchFromUnit(CombatUnit unit, MissileData missile, Action onCancelled = null)
    {
        if (unit == null || missile == null) { Debug.LogWarning("[MissileLaunchMode] Invalid unit launch source."); return; }
        if (_active) Cancel();
        _sourceType = SourceType.Unit;
        _unit  = unit;
        _city  = null;
        _sourceTile  = unit.currentTileIndex;
        _planetIndex = unit.planetIndex;
        _sourceOwner = unit.owner;
        Activate(missile, onCancelled);
    }

    public void BeginLaunchFromSilo(int siloTileIndex, int planetIndex, MissileData missile, Action onCancelled = null)
    {
        if (missile == null) { Debug.LogWarning("[MissileLaunchMode] Invalid silo missile."); return; }
        if (_active) Cancel();
        _sourceType  = SourceType.Silo;
        _siloTile    = siloTileIndex;
        _city  = null;
        _unit  = null;
        _sourceTile  = siloTileIndex;
        _planetIndex = planetIndex;
        var tile = TileSystem.GetForPlanet(planetIndex)?.GetTileData(siloTileIndex);
        _sourceOwner = tile?.owner;
        _siloImprovementOwner = tile?.improvementOwner;
        Activate(missile, onCancelled);
    }

    // ─── Activate / Deactivate ────────────────────────────────────────────────
    private void Activate(MissileData missile, Action onCancelled)
    {
        _missile     = missile;
        _onCancelled = onCancelled;
        _active      = true;
        var ts = TileSystem.GetForPlanet(_planetIndex);
        if (!IsLaunchStillValid(ts, _sourceTile))
        {
            Debug.LogWarning("[MissileLaunchMode] The launch source or missile is no longer valid.");
            UIManager.Instance?.ShowNotification("The missile launch source is no longer available.");
            Cancel();
            return;
        }
        if (_hud == null || _hudIcon == null || _hudName == null || _hudInfo == null)
        {
            Debug.LogError("[MissileLaunchMode] Targeting HUD is not configured; launch mode cannot start.", this);
            UIManager.Instance?.ShowNotification("Missile targeting is unavailable.");
            Cancel();
            return;
        }
        _hudIcon.sprite = missile.icon;
        _hudIcon.enabled = missile.icon != null;
        _hudName.text = missile.missileName;
        _hudInfo.text = $"Range: {missile.range} tiles\nRight-click a valid tile to fire. Escape to cancel.";
        _hud.SetActive(true);
        SpawnRangeOverlays();
    }

    public void Cancel()
    {
        if (!_active) return;
        if (_confirmationPending)
        {
            _confirmationPending = false;
            ConfirmationDialogService.DismissActive();
        }
        var callback = _onCancelled;
        Deactivate();
        callback?.Invoke();
    }

    private void Deactivate()
    {
        _active  = false;
        _missile = null;
        _city = null;
        _unit = null;
        _sourceOwner = null;
        _siloImprovementOwner = null;
        _onCancelled = null;
        _inputConsumedFrame = Time.frameCount;
        if (_hud != null) _hud.SetActive(false);
        ClearAllOverlays();
    }

    private void OnDestroy()
    {
        if (Instance != this) return;
        if (_active) Cancel();
        if (_rangeMaterial != null) Destroy(_rangeMaterial);
        if (_targetMaterial != null) Destroy(_targetMaterial);
        Instance = null;
    }

    private void OnDisable()
    {
        if (Instance == this && _active) Cancel();
    }

    // ─── Range Highlights ────────────────────────────────────────────────────
    private void SpawnRangeOverlays()
    {
        ClearAllOverlays();
        if (_missile == null) return;

        var ts = TileSystem.GetForPlanet(_planetIndex);
        if (ts == null) return;

        var tiles = MissileManager.GetTilesInMissileRange(ts, _sourceTile, _missile.range);
        foreach (int idx in tiles)
        {
            if (ts.GetTileData(idx) == null) continue;
            var go = CreateOverlay(ts, idx, rangeOverlayPrefab, false);
            if (go == null) break;
            _rangeOverlays.Add(go);
        }
    }

    private void RefreshTargetOverlay()
    {
        var ts = TileSystem.GetForPlanet(_planetIndex);
        if (ts == null) return;

        int hovered = GetHoveredTile(ts);
        if (hovered == _lastHoveredTile) return;
        _lastHoveredTile = hovered;

        if (_targetOverlay != null) { Destroy(_targetOverlay); _targetOverlay = null; }

        if (hovered >= 0 && ts.GetTileData(hovered) != null && IsValidTarget(ts, hovered))
            _targetOverlay = CreateOverlay(ts, hovered, targetOverlayPrefab, true);
    }

    private GameObject CreateOverlay(TileSystem ts, int tile, GameObject prefab, bool target)
    {
        Vector3 position = ts.GetTileSurfacePosition(tile, 0.02f);
        if (prefab != null) return Instantiate(prefab, position, Quaternion.identity);

        var material = target ? _targetMaterial : _rangeMaterial;
        if (material == null)
        {
            var shader = Shader.Find("HDRP/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Debug.LogError("[MissileLaunchMode] No unlit shader is available for targeting highlights.", this);
                return null;
            }
            material = new Material(shader);
            var color = target ? new Color(1f, 0.75f, 0.1f) : new Color(0.1f, 0.75f, 0.9f);
            if (material.HasProperty("_UnlitColor")) material.SetColor("_UnlitColor", color);
            else material.color = color;
            if (target) _targetMaterial = material;
            else _rangeMaterial = material;
        }
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = target ? "Missile Target" : "Missile Range";
        go.transform.position = position;
        go.transform.localScale = Vector3.one * (target ? 0.35f : 0.18f);
        var collider = go.GetComponent<Collider>();
        if (collider != null) collider.enabled = false;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    // ─── Fire ────────────────────────────────────────────────────────────────
    private void TryFire()
    {
        var ts = TileSystem.GetForPlanet(_planetIndex);
        if (ts == null || _missile == null) return;

        int target = GetHoveredTile(ts);
        if (target < 0 || !IsValidTarget(ts, target))
        {
            Debug.Log("[MissileLaunchMode] No valid target under cursor.");
            return;
        }

        if (!IsLaunchStillValid(ts, target))
        {
            Debug.LogWarning("[MissileLaunchMode] The launch source or missile is no longer valid.");
            Cancel();
            return;
        }

        if (_missile.isNuclear)
        {
            _confirmationPending = true;
            if (!ConfirmationDialogService.ShowConfirmation(
                "Confirm Nuclear Launch",
                "You are about to launch a nuclear missile. The impact may cause widespread destruction and persistent radiation. Proceed?",
                () => ConfirmNuclearLaunch(target),
                () => { _confirmationPending = false; _inputConsumedFrame = Time.frameCount; },
                "Confirm Launch", "Cancel", destructive: true, owner: this))
            {
                _confirmationPending = false;
                Debug.LogWarning("[MissileLaunchMode] Nuclear launch confirmation could not be opened; launch cancelled.");
                UIManager.Instance?.ShowNotification("Nuclear launch confirmation is unavailable.");
            }
            return;
        }

        Fire(target);
    }

    private void ConfirmNuclearLaunch(int target)
    {
        if (!_confirmationPending) return;
        _confirmationPending = false;
        _inputConsumedFrame = Time.frameCount;
        var ts = TileSystem.GetForPlanet(_planetIndex);
        if (!IsLaunchStillValid(ts, target))
        {
            Debug.LogWarning("[MissileLaunchMode] Nuclear launch cancelled because the source, missile, or target changed.");
            UIManager.Instance?.ShowNotification("The nuclear launch source or target is no longer valid.");
            Cancel();
            return;
        }
        Fire(target);
    }

    private bool IsLaunchStillValid(TileSystem ts, int target)
    {
        if (!_active || _missile == null || MissileManager.Instance == null || ts == null ||
            (GameManager.Instance != null && GameManager.Instance.currentPlanetIndex != _planetIndex) ||
            ts.GetTileData(_sourceTile) == null || ts.GetTileData(target) == null || !IsValidTarget(ts, target))
            return false;

        switch (_sourceType)
        {
            case SourceType.City:
                return _city != null && _city.planetIndex == _planetIndex && _city.centerTileIndex == _sourceTile &&
                    _city.owner == _sourceOwner && _city.storedMissiles != null && _city.storedMissiles.Contains(_missile);
            case SourceType.Unit:
                return _unit != null && _unit.planetIndex == _planetIndex && _unit.currentTileIndex == _sourceTile &&
                    _unit.owner == _sourceOwner && _unit.storedMissiles != null && _unit.storedMissiles.Contains(_missile);
            case SourceType.Silo:
                var tile = ts.GetTileData(_siloTile);
                return tile != null && tile.improvement != null && tile.improvement.isMissileSilo &&
                    tile.owner == _sourceOwner && tile.improvementOwner == _siloImprovementOwner &&
                    MissileManager.Instance.GetSiloMissiles(_planetIndex, _siloTile).Contains(_missile);
            default:
                return false;
        }
    }

    private void Fire(int target)
    {
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
