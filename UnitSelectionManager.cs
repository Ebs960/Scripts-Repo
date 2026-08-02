using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;

/// <summary>
/// Manages unit selection and movement commands.
/// Handles right-click movement orders and prevents conflicts with other input.
/// </summary>
public class UnitSelectionManager : MonoBehaviour
{
    public static UnitSelectionManager Instance { get; private set; }
    
    [Header("Selection Settings")]
    [SerializeField] private Color selectedUnitHighlightColor = Color.yellow;
    [SerializeField] private GameObject selectionIndicatorPrefab; // Optional visual indicator
    
    [Header("Path Preview")]
    [Tooltip("Optional prefab to use for path markers. If null, a simple sphere will be used.")]
    [SerializeField] private GameObject pathMarkerPrefab;
    [SerializeField] private Color pathMarkerColor = Color.cyan;
    
    [Tooltip("Prefab used for per-tile path icons (Image/Sprite + Text child). If assigned, these prefabs will be pooled and used for each tile icon; preferred for numbered icons.)")]
    [SerializeField] private GameObject pathTilePrefab;
    
    
    [Header("Attack Hover")]
    [Tooltip("Optional prefab to use as the attack-hover icon. If null, assign a Sprite to `attackHoverIcon` to create a simple SpriteRenderer at runtime.")]
    [SerializeField] private GameObject attackHoverPrefab;
    [Tooltip("Sprite used for the attack-hover indicator if no prefab is provided.")]
    [SerializeField] private Sprite attackHoverIcon;
    [Tooltip("Vertical offset above the unit to place the attack icon.")]
    [SerializeField] private float attackHoverYOffset = 0.8f;
    
    // Currently selected unit - now uses BaseUnit as common type
    private BaseUnit selectedUnit; // Can be CombatUnit or WorkerUnit (both inherit from BaseUnit)
    // Selected herd (packed/mobile)
    private Herd selectedHerd;
    private GameObject selectionIndicator;
    // Frame guard: prevent OnTileClickedTileSystem from deselecting a unit
    // that was just selected by OnMouseDown in the same frame.
    private int lastSelectionFrame = -1;
    // Cached highlight/selection materials to avoid allocations
    private static Material s_selectionIndicatorMaterial;
    private static UnityEngine.MaterialPropertyBlock s_selectionMPB;
    
    // References
    private Camera mainCamera;
    private Camera cachedMainCamera; // Cached reference to avoid repeated FindAnyObjectByType calls
    // Cached hover info provided by TileSystem events
    private int cachedHoveredTileIndex = -1;
    private Vector3 cachedHoveredWorldPos = Vector3.zero;
    private bool isHoveringTile = false;
    // Pending attack target for move-then-attack orders (for the currently selected unit)
    private BaseUnit pendingAttackTarget = null;

    // Path preview state
    private bool isPreviewing = false;
    private int previewTargetTile = -1;
    private GameObject previewParent;
    [Header("Debug & Safety")]
    [Tooltip("Enable verbose preview/debug logs for selection and path preview (dev only)")]
    [SerializeField] private bool previewDebug = true;
    [Tooltip("Maximum number of per-tile preview icons to instantiate (safety cap)")]
    [SerializeField] private int previewMaxIcons = 20;
    private readonly System.Collections.Generic.List<GameObject> previewMarkers = new System.Collections.Generic.List<GameObject>();
    private readonly System.Collections.Generic.List<UnityEngine.Component> previewMarkerLabels = new System.Collections.Generic.List<UnityEngine.Component>();
    // Pooled per-tile prefab instances (used when `pathTilePrefab` is assigned)
    private readonly System.Collections.Generic.List<GameObject> pooledPathTiles = new System.Collections.Generic.List<GameObject>();

    // Preview update guards (coalesce multiple events per-frame and avoid full rebuilds)
    private int _lastPreviewUpdateFrame = -1;
    private int _lastQueuedPreviewFrame = -1;
    

    // Runtime attack hover instance (single pooled instance)
    private GameObject attackHoverInstance;
    private SpriteRenderer attackHoverSpriteRenderer;

    // Multi-planet: subscribe to the active planet's TileSystem events
    private TileSystem eventTileSystem;
    private int eventPlanetIndex = int.MinValue;
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        // Find references
        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            if (cachedMainCamera == null)
                cachedMainCamera = FindAnyObjectByType<Camera>();
            mainCamera = cachedMainCamera;
        }

        // Cache selection material once
        if (s_selectionIndicatorMaterial == null)
        {
            var shader = Shader.Find("Standard");
            if (shader != null)
            {
                s_selectionIndicatorMaterial = new Material(shader);
                s_selectionIndicatorMaterial.SetFloat("_Mode", 3);
                s_selectionIndicatorMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                s_selectionIndicatorMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                s_selectionIndicatorMaterial.SetInt("_ZWrite", 0);
                s_selectionIndicatorMaterial.DisableKeyword("_ALPHATEST_ON");
                s_selectionIndicatorMaterial.EnableKeyword("_ALPHABLEND_ON");
                s_selectionIndicatorMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                s_selectionIndicatorMaterial.renderQueue = 3000;
            }
            s_selectionMPB = new UnityEngine.MaterialPropertyBlock();
        }
        // Subscribe to global movement-completed events so the preview/UI can refresh
        if (GameEventManager.Instance != null)
        {
            GameEventManager.Instance.OnMovementCompleted += OnUnitMovementCompleted;
        }
    }
    
    void Update()
    {
        // Keep event subscription aligned with the active planet.
        int desiredPlanet = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        if (eventTileSystem == null || eventPlanetIndex != desiredPlanet)
        {
            // Unsubscribe from previous
            if (eventTileSystem != null)
            {
                eventTileSystem.OnTileHovered -= OnTileHoveredTileSystem;
                eventTileSystem.OnTileHoverExited -= OnTileExitedTileSystem;
                eventTileSystem.OnTileClicked -= OnTileClickedTileSystem;
            }

            eventPlanetIndex = desiredPlanet;
            eventTileSystem = TileSystem.GetForPlanet(desiredPlanet) ?? TileSystem.Instance;

            // Subscribe to new (if ready)
            if (eventTileSystem != null)
            {
                eventTileSystem.OnTileHovered += OnTileHoveredTileSystem;
                eventTileSystem.OnTileHoverExited += OnTileExitedTileSystem;
                eventTileSystem.OnTileClicked += OnTileClickedTileSystem;
            }
        }

        HandleInput();
    }

    private void OnEnable()
    {
        // Ensure Update() resubscribes immediately on enable
        eventTileSystem = null;
        eventPlanetIndex = int.MinValue;
    }

    private void OnDisable()
    {
        if (eventTileSystem != null)
        {
            eventTileSystem.OnTileHovered -= OnTileHoveredTileSystem;
            eventTileSystem.OnTileHoverExited -= OnTileExitedTileSystem;
            eventTileSystem.OnTileClicked -= OnTileClickedTileSystem;
        }
        eventTileSystem = null;
        if (GameEventManager.Instance != null)
        {
            GameEventManager.Instance.OnMovementCompleted -= OnUnitMovementCompleted;
        }
    }

    private void OnDestroy()
    {
        if (GameEventManager.Instance != null)
        {
            GameEventManager.Instance.OnMovementCompleted -= OnUnitMovementCompleted;
        }
    }

    private void OnTileHoveredTileSystem(int tileIndex, Vector3 worldPos)
    {
        cachedHoveredTileIndex = tileIndex;
        cachedHoveredWorldPos = worldPos;
        isHoveringTile = true;
        UpdateAttackHover(tileIndex, worldPos);
    }

    private void OnTileExitedTileSystem()
    {
        cachedHoveredTileIndex = -1;
        cachedHoveredWorldPos = Vector3.zero;
        isHoveringTile = false;
        ClearAttackHover();
    }

    private bool OnTileClickedTileSystem(int tileIndex, Vector3 worldPos)
    {
        // Left-click selection is routed via TileSystem; select/deselect units here.
        // Return true to consume the click (prevents improvements from also handling it).
        // IMPORTANT (Flat map compatibility):
        // In flat equirectangular view, worldPos is on the flat plane, not near the unit's 3D position.
        // The authoritative identity is tileIndex. Use occupantId -> UnitRegistry first, then fall back
        // to a worldPos proximity lookup for legacy meshes.
        var clickedUnit = GetUnitOnTile(tileIndex);
        if (clickedUnit == null)
            clickedUnit = GetUnitAtPosition(worldPos);

        if (clickedUnit != null)
        {
            // If this click selects a different unit (or selects when nothing was selected),
            // consume the click so improvements don't open. If the same unit is already selected,
            // cycle through stacked units if any; otherwise allow other handlers.
            if (selectedUnit == null || selectedUnit != clickedUnit)
            {
                SelectUnit(clickedUnit);
                return true; // consumed
            }
            // Same unit already selected — cycle stack if more than one unit here
            var companions = selectedUnit.GetStackedUnits();
            if (companions.Count > 0)
            {
                CycleStackedUnit();
                return true; // consumed
            }
            return false; // allow other subscribers to handle (e.g., improvement)
        }
        else
        {
            // Guard: if a unit was selected this very frame (e.g. via OnMouseDown on the
            // unit's collider), do NOT deselect it here. Let that click be authoritative.
            // Try herd selection: if a Herd is occupying this tile, open the Herd panel
            try
            {
                var herd = GetHerdOnTile(tileIndex);
                if (herd == null) herd = GetHerdAtPosition(worldPos);
                if (herd != null)
                {
                    if (herd.isPacked)
                    {
                        SelectHerd(herd);
                    }
                    else
                    {
                        if (UIManager.Instance != null) UIManager.Instance.ShowHerdPanelForHerd(herd);
                    }
                    return true; // consume click when selecting herd
                }
            }
            catch { }

            if (lastSelectionFrame == Time.frameCount)
                return false;

            DeselectUnit();
            PlayResourceClickSound(tileIndex);
            return false; // don't consume; allow improvements to open when no unit present
        }
        // Note: Right-click movement remains handled in Update() to detect mouse button 1
    }

    /// <summary>
    /// Get a unit occupying the given tile index.
    /// Stack-aware: returns the front unit (slot 0) of a stack, or the currently
    /// selected unit if it is already on this tile. Use CycleStackedUnit to switch.
    /// </summary>
    private BaseUnit GetUnitOnTile(int tileIndex)
    {
        int pIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        try
        {
            var occ = TileOccupancyManager.GetForPlanet(pIndex) ?? TileOccupancyManager.Instance;
            if (occ == null) return null;

            // If current selection is already on this tile, return it (preserves cycling state)
            if (selectedUnit != null && selectedUnit.currentTileIndex == tileIndex)
                return selectedUnit;

            // Return front unit (slot 0) as default selection target
            int frontId = occ.GetOccupantIdAtSlot(tileIndex, TileLayer.Surface, 0);
            if (frontId == 0) frontId = occ.GetOccupantIdAtSlot(tileIndex, TileLayer.Orbit, 0);
            if (frontId == 0) frontId = occ.GetOccupantIdAtSlot(tileIndex, TileLayer.Underwater, 0);
            if (frontId == 0) frontId = occ.GetOccupantIdAtSlot(tileIndex, TileLayer.Atmosphere, 0);
            if (frontId == 0)
            {
                // Fallback to legacy single-occupant lookup
                var obj = occ.TryGetAnyOccupantObject(tileIndex);
                if (obj != null) return obj.GetComponent<BaseUnit>();
                return null;
            }

            var frontObj = UnitRegistry.GetObject(frontId);
            if (frontObj != null)
            {
                var bu = frontObj.GetComponent<BaseUnit>();
                if (previewDebug) Debug.Log($"[USM] GetUnitOnTile({tileIndex}) found front unit={frontObj.name} slot=0");
                return bu;
            }
        }
        catch (System.Exception ex) { Debug.LogWarning($"[UnitSelectionManager] GetUnitOnTile({tileIndex}) failed: {ex.Message}"); }
        return null;
    }

    /// <summary>
    /// Get a herd occupying the given tile index (if any).
    /// </summary>
    private Herd GetHerdOnTile(int tileIndex)
    {
        int pIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        try
        {
            var occ = TileOccupancyManager.GetForPlanet(pIndex) ?? TileOccupancyManager.Instance;
            var obj = occ != null ? occ.TryGetAnyOccupantObject(tileIndex) : null;
            if (obj != null)
            {
                var herd = obj.GetComponent<Herd>();
                return herd;
            }
        }
        catch (System.Exception ex) { Debug.LogWarning($"[UnitSelectionManager] GetHerdOnTile({tileIndex}) failed: {ex.Message}"); }
        return null;
    }
    
    /// <summary>
    /// Handle mouse input for unit selection and movement
    /// </summary>
    private void HandleInput()
    {
        if (GameInteractionStateService.GetOrCreate().Mode != GameInteractionMode.Campaign)
            return;

        if (IsPointerOverUIBlockingGameplay())
            return;

        // MIGRATED: Use InputManager for UI blocking check
        if (InputManager.Instance != null && InputManager.Instance.IsPointerOverUI())
            return;
        
        // MIGRATED: Check if we can process gameplay input
        if (InputManager.Instance != null && !InputManager.Instance.CanProcessInput(InputManager.InputPriority.Gameplay))
            return;
        
        // Right click: start/continue/commit path preview and move on release
        if (Mouse.current != null)
        {
            if (Mouse.current.rightButton.wasPressedThisFrame)
            {
                StartPathPreview();
            }
            else if (Mouse.current.rightButton.isPressed)
            {
                UpdatePathPreviewWhileDragging();
            }
            else if (Mouse.current.rightButton.wasReleasedThisFrame)
            {
                CommitOrCancelPathPreview();
            }
        }
        
        // Left click on void (no tile hit): deselect the current unit.
        // OnTileClicked only fires for valid tile hits, so we must handle the
        // "clicked on nothing" case here.
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && selectedUnit != null)
        {
            // Only deselect if TileSystem did NOT already handle this click
            // (i.e., the click didn't land on a valid tile).
            if (!isHoveringTile || cachedHoveredTileIndex < 0)
            {
                DeselectUnit();
            }
        }

        // Tab key: Cycle through stacked units on the same tile
        if (Keyboard.current != null && Keyboard.current[Key.Tab].wasPressedThisFrame && selectedUnit != null)
        {
            CycleStackedUnit();
        }

        // R key: Show space travel UI for selected unit (changed from Space to avoid conflicts)
        if (Keyboard.current != null && Keyboard.current[Key.R].wasPressedThisFrame && HasSelectedUnit())
        {
            HandleSpaceTravelKey();
        }
        
        // M key: Open space map for solar system overview
        if (Keyboard.current != null && Keyboard.current[Key.M].wasPressedThisFrame)
        {
            HandleSpaceMapKey();
        }
    }

    private static bool IsPointerOverUIBlockingGameplay()
    {
        if (EventSystem.current == null)
            return false;

        if (Mouse.current != null && EventSystem.current.IsPointerOverGameObject())
            return true;

        var touch = Touchscreen.current;
        if (touch != null)
        {
            foreach (var t in touch.touches)
            {
                if (!t.press.isPressed) continue;
                if (EventSystem.current.IsPointerOverGameObject(t.touchId.ReadValue()))
                    return true;
            }
        }

        return false;
    }
    
    /// <summary>
    /// Handle left-click for unit selection
    /// </summary>
    private void HandleLeftClick()
    {
        // Deprecated: Selection via TileSystem.OnTileClicked
    }
    
    /// <summary>
    /// Handle right-click for unit movement
    /// </summary>
    private void HandleRightClick()
    {
        // Deprecated: movement is now handled via preview lifecycle
    }

    private void StartPathPreview()
    {
        if (selectedUnit == null && selectedHerd == null) return;
        isPreviewing = true;
        previewTargetTile = ResolvePreviewTargetTile();
        EnsurePreviewObjects();
        UpdatePreviewVisuals();
    }

    private void UpdatePathPreviewWhileDragging()
    {
        if (!isPreviewing || (selectedUnit == null && selectedHerd == null)) return;

        int target = ResolvePreviewTargetTile();

        if (target != previewTargetTile)
        {
            previewTargetTile = target;
            UpdatePreviewVisuals();
        }
    }

    private void CommitOrCancelPathPreview()
    {
        if (!isPreviewing) return;
        isPreviewing = false;
        previewTargetTile = ResolvePreviewTargetTile();

        if (previewTargetTile >= 0)
        {
            if (selectedHerd != null) MoveSelectedHerdToTile(previewTargetTile); else MoveSelectedUnitToTile(previewTargetTile);
        }

        // Clear transient preview visuals, then immediately show persistent queued path if still selected
        ClearPreviewVisuals();
        if (HasSelectedUnit()) ShowQueuedPathPreviewIfAny();
    }

    private int ResolvePreviewTargetTile()
    {
        var hit = GetMouseHitInfo();
        return hit.hit ? hit.tileIndex : -1;
    }

    private void EnsurePreviewObjects()
    {
        if (previewParent == null)
        {
            previewParent = new GameObject("PathPreview");
            previewParent.transform.SetParent(transform);
            previewParent.transform.localPosition = Vector3.zero;
        }
    }

    private void UpdatePreviewVisuals()
    {
        if (previewTargetTile < 0)
        {
            ClearPreviewVisuals();
            return;
        }
            // Coalesce multiple preview update calls in the same frame
            if (_lastPreviewUpdateFrame == Time.frameCount) return;
            _lastPreviewUpdateFrame = Time.frameCount;

        // Do not blanket-disable all markers/labels here; deactivate only extras after incremental update

        var umc = UnitMovementController.Instance;
        // Fallback: Instance may not be assigned yet due to initialization order.
        if (umc == null)
        {
            umc = FindAnyObjectByType<UnitMovementController>();
            if (umc == null)
            {
                if (previewDebug) Debug.LogWarning("[USM] UnitMovementController not found yet; skipping preview.");
                return;
            }
        }
        int start = selectedUnit != null ? selectedUnit.currentTileIndex : (selectedHerd != null ? selectedHerd.currentTileIndex : -1);
        var ts = (selectedUnit != null) ? (TileSystem.GetForPlanet(selectedUnit.planetIndex) ?? TileSystem.Instance) : (selectedHerd != null ? (TileSystem.GetForPlanet(selectedHerd.planetIndex) ?? TileSystem.Instance) : TileSystem.Instance);
        if (ts == null)
        {
            return;
        }

        System.Collections.Generic.List<int> previewPath = null;
        System.Collections.Generic.List<System.Collections.Generic.List<int>> segments = null;
        if (selectedHerd != null)
        {
            previewPath = umc.FindPath(start, previewTargetTile, null);
            segments = umc.GetPathSegmentsForHerd(selectedHerd, previewPath);
        }
        else
        {
            previewPath = umc.FindPath(start, previewTargetTile, selectedUnit);
            segments = umc.GetPathSegmentsForDisplay(selectedUnit, previewPath);
        }
        if (segments == null || segments.Count == 0)
        {
            foreach (var p in pooledPathTiles) if (p != null) p.SetActive(false);
            ShowDestinationMarker(previewTargetTile, ts, 0);
            if (previewDebug) Debug.Log($"[USM] UpdatePreviewVisuals: target={previewTargetTile} segments=0");
            return;
        }

        if (previewDebug)
        {
            Debug.Log($"[USM] UpdatePreviewVisuals: target={previewTargetTile} segments={segments.Count}");
            var unitOnPreview = GetUnitOnTile(previewTargetTile);
            if (unitOnPreview != null) Debug.Log($"[USM] Preview target tile has unit: {unitOnPreview.name}");
        }

        // Build a per-tile list of world positions (one icon per tile along the path).
        // Also build a parallel list `breakpointNumbers` where 0 = not a turn breakpoint,
        // and >0 = turn index to display inside the icon.
        var positions = new System.Collections.Generic.List<Vector3>();
        var breakpointNumbers = new System.Collections.Generic.List<int>();
        for (int s = 0; s < segments.Count; s++)
        {
            var seg = segments[s];
            for (int i = 0; i < seg.Count; i++)
            {
                int tile = seg[i];
                Vector3 pos = ts.GetTileSurfacePosition(tile) + Vector3.up * 0.2f;
                positions.Add(pos);
                // If this is the last tile in the segment, mark it as a breakpoint and store the turn number (1-based)
                bool isBreakpoint = (i == seg.Count - 1);
                breakpointNumbers.Add(isBreakpoint ? (s + 1) : 0);
            }
        }

        // Debug and safety: log counts and cap excessive previews to avoid memory spikes
        if (previewDebug) Debug.Log($"[USM] UpdatePreviewVisuals segments={segments.Count} positions={positions.Count} pooled={pooledPathTiles.Count}");
        if (positions.Count > previewMaxIcons)
        {
            Debug.LogWarning($"[USM] Preview positions ({positions.Count}) exceed previewMaxIcons ({previewMaxIcons}) - truncating to cap.");
            positions.RemoveRange(previewMaxIcons, positions.Count - previewMaxIcons);
            breakpointNumbers.RemoveRange(previewMaxIcons, breakpointNumbers.Count - previewMaxIcons);
        }

        // Quick sampling to check whether spawned icons are above the actual mesh surface
        if (previewDebug && positions.Count > 0)
        {
            int samples = Mathf.Min(5, positions.Count);
            for (int si = 0; si < samples; si++)
            {
                Vector3 testPos = positions[si] + Vector3.up * 2f;
                if (Physics.Raycast(testPos, Vector3.down, out var hit, 5f))
                {
                    float delta = positions[si].y - hit.point.y;
                    Debug.Log($"[USM] Preview sample {si}: posY={positions[si].y:F3} surfaceY={hit.point.y:F3} delta={delta:F3}");
                    if (delta < -0.05f || delta > 1.5f)
                        Debug.LogWarning($"[USM] Preview sample {si} seems far from surface (delta={delta:F3})");
                }
            }
        }

        // Render preview: use pooled per-tile prefabs only (no lines)
        if (pathTilePrefab != null)
        {
            // Hard cap: do not instantiate more than previewMaxIcons prefabs
            int maxToShow = Mathf.Min(positions.Count, previewMaxIcons);

            // Ensure pool size up to capped count
            for (int i = pooledPathTiles.Count; i < maxToShow; i++)
            {
                var p = Instantiate(pathTilePrefab, previewParent.transform);
                p.name = $"PathTile_{pooledPathTiles.Count}";
                p.SetActive(false);
                pooledPathTiles.Add(p);
            }

            // Position pooled prefabs (only up to maxToShow) and ensure label (if any) is hidden
            for (int i = 0; i < pooledPathTiles.Count; i++)
            {
                var go = pooledPathTiles[i];
                if (i < maxToShow)
                {
                    // If this path tile is a turn breakpoint, do not show a path icon here;
                    // the per-turn marker will be shown instead.
                    if (breakpointNumbers != null && i < breakpointNumbers.Count && breakpointNumbers[i] > 0)
                    {
                        go.SetActive(false);
                        continue;
                    }

                    Vector3 targetPos = positions[i];

                    // Attempt to align to actual mesh surface via a short downward raycast.
                    // This fixes cases where the mesh is displaced by a shader or heightmap that
                    // doesn't match the tile elevation stored in TileData.
                    Vector3 rayStart = targetPos + Vector3.up * 4f;
                    if (Physics.Raycast(rayStart, Vector3.down, out var hit, 6f))
                    {
                        float surfaceY = hit.point.y;
                        float delta = targetPos.y - surfaceY;
                        // If the delta is significant, snap to the physics surface
                        if (Mathf.Abs(delta) > 0.05f)
                        {
                            if (previewDebug) Debug.Log($"[USM] Adjusting preview tile {i} Y from {targetPos.y:F3} to surface {surfaceY:F3} (delta {delta:F3})");
                            targetPos.y = surfaceY + 0.02f; // small offset above surface
                        }
                    }

                    go.SetActive(true);
                    go.transform.position = targetPos;
                    // Ensure the icon's face points toward the sky (flat on the ground).
                    // Many mesh/Text prefabs face along their local forward (+Z), so set forward->up.
                    go.transform.forward = Vector3.up;
                    // Support both TextMeshPro (3D) and TextMeshProUGUI (UI) labels, including inactive children.
                    var tmp3d = go.GetComponentInChildren<TextMeshPro>(true);
                    if (tmp3d != null)
                    {
                        tmp3d.text = "";
                        tmp3d.gameObject.SetActive(false);
                    }
                    else
                    {
                        var tmpUI = go.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                        if (tmpUI != null)
                        {
                            tmpUI.text = "";
                            tmpUI.gameObject.SetActive(false);
                        }
                    }
                }
                else
                {
                    go.SetActive(false);
                }
            }

            // If there were more positions than the cap, warn once
            if (positions.Count > previewMaxIcons)
            {
                Debug.LogWarning($"[USM] Preview truncated: showing {previewMaxIcons}/{positions.Count} icons to avoid spike.");
            }
        }
        else
        {
            foreach (var p in pooledPathTiles) if (p != null) p.SetActive(false);
        }
        

        // Place per-turn markers at the last tile of each segment
        for (int m = 0; m < segments.Count; m++)
        {
            var seg = segments[m];
            if (seg == null || seg.Count == 0) continue;
            int markerTile = seg[seg.Count - 1];
            Vector3 mpos = ts.GetTileSurfacePosition(markerTile) + Vector3.up * 0.25f;
            GameObject marker = GetOrCreateMarker(m);
            marker.transform.position = mpos;
            // Orient marker so its visible face points upward (flat on ground)
            marker.transform.forward = Vector3.up;
            marker.SetActive(true);
            if (m < previewMarkerLabels.Count && previewMarkerLabels[m] != null)
            {
                var lblComp = previewMarkerLabels[m];
                if (lblComp is TextMeshPro lbl3d)
                {
                    lbl3d.text = (m + 1).ToString();
                    lbl3d.gameObject.SetActive(true);
                }
                else if (lblComp is TMPro.TextMeshProUGUI lblUI)
                {
                    lblUI.text = (m + 1).ToString();
                    lblUI.gameObject.SetActive(true);
                }
            }
        }

        int lastSegmentEndTile = segments.Count > 0 && segments[segments.Count - 1] != null && segments[segments.Count - 1].Count > 0
            ? segments[segments.Count - 1][segments[segments.Count - 1].Count - 1]
            : -1;
        if (lastSegmentEndTile != previewTargetTile)
        {
            ShowDestinationMarker(previewTargetTile, ts, segments.Count);
        }

        // Deactivate any extra markers/labels that are no longer used (incremental update)
        int usedMarkers = segments != null ? segments.Count : 0;
        for (int i = usedMarkers; i < previewMarkers.Count; i++)
        {
            var m = previewMarkers[i];
            if (m != null) m.SetActive(false);
            if (i < previewMarkerLabels.Count && previewMarkerLabels[i] != null)
                previewMarkerLabels[i].gameObject.SetActive(false);
        }
    }

    private void ShowDestinationMarker(int tileIndex, TileSystem ts, int markerIndex)
    {
        if (tileIndex < 0 || ts == null) return;

        Vector3 pos = ts.GetTileSurfacePosition(tileIndex) + Vector3.up * 0.25f;
        GameObject marker = GetOrCreateMarker(markerIndex);
        marker.transform.position = pos;
        marker.transform.forward = Vector3.up;
        marker.SetActive(true);

        // If the marker has a label component, show a numeric label for positive turn indices.
        if (markerIndex < previewMarkerLabels.Count && previewMarkerLabels[markerIndex] != null)
        {
            var lblComp = previewMarkerLabels[markerIndex];
            // For markerIndex == 0 we intentionally hide the numeric label (legacy behavior)
            if (markerIndex > 0)
            {
                if (lblComp is TextMeshPro lbl3d)
                {
                    lbl3d.text = markerIndex.ToString();
                    lbl3d.gameObject.SetActive(true);
                }
                else if (lblComp is TMPro.TextMeshProUGUI lblUI)
                {
                    lblUI.text = markerIndex.ToString();
                    lblUI.gameObject.SetActive(true);
                }
            }
            else
            {
                // keep legacy behaviour: no numeric label for a markerIndex of 0
                lblComp.gameObject.SetActive(false);
            }
        }
    }

    private GameObject GetOrCreateMarker(int idx)
    {
        if (idx < previewMarkers.Count)
        {
            return previewMarkers[idx];
        }

        GameObject go;
        if (pathMarkerPrefab != null)
        {
            go = Instantiate(pathMarkerPrefab, previewParent.transform);
        }
        else
        {
            // Avoid instantiating `pathTilePrefab` as a marker because pooled path-tile prefabs
            // are already placed for each tile; instantiating the tile prefab again for markers
            // can produce duplicate icons on the same tile. Use a simple sphere marker instead.
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var c = go.GetComponent<Collider>(); if (c != null) Destroy(c);
            var rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                var mat = new Material(Shader.Find("Unlit/Color"));
                mat.SetColor("_Color", pathMarkerColor);
                rend.material = mat;
            }
        }

        if (string.IsNullOrEmpty(go.name)) go.name = $"PathMarker_{previewMarkers.Count}";
        go.transform.SetParent(previewParent.transform);
        // Make markers larger so the icon is clearly visible
        go.transform.localScale = Vector3.one * 0.6f;

        // Ensure any world-space Canvas on the prefab has the scene camera assigned so
        // UI labels (TextMeshProUGUI) render and receive events correctly.
        var canvas = go.GetComponentInChildren<Canvas>();
        if (canvas != null && canvas.renderMode == RenderMode.WorldSpace && canvas.worldCamera == null)
        {
            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera != null)
            {
                canvas.worldCamera = mainCamera;
                if (previewDebug) Debug.Log($"[USM] Assigned Canvas.worldCamera for marker prefab {go.name} to {mainCamera.name}");
            }
        }

        // Prefer using a label baked into the prefab. Do NOT auto-create labels
        // so numbering only appears when the prefab provides a text child.
        UnityEngine.Component labelComp = null;
        var label3D = go.GetComponentInChildren<TextMeshPro>(true);
        if (label3D != null)
        {
            labelComp = label3D;
            label3D.transform.localPosition = Vector3.zero;
            var lblRenderer = label3D.GetComponent<MeshRenderer>();
            if (lblRenderer != null) lblRenderer.sortingOrder = 1002;
            label3D.gameObject.SetActive(false);
        }
        else
        {
            var labelUI = go.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
            if (labelUI != null)
            {
                labelComp = labelUI;
                labelUI.rectTransform.localPosition = Vector3.zero;
                labelUI.gameObject.SetActive(false);
                // Check for a parent Canvas and whether it's world-space
                var parentCanvas = labelUI.GetComponentInParent<UnityEngine.Canvas>();
                if (parentCanvas == null)
                {
                    if (previewDebug) Debug.LogWarning($"[USM] Marker prefab's TextMeshProUGUI has no Canvas parent; label may not render: {go.name}");
                }
                else if (parentCanvas.renderMode != UnityEngine.RenderMode.WorldSpace)
                {
                    if (previewDebug) Debug.LogWarning($"[USM] Marker prefab's Canvas is not WorldSpace; TextMeshProUGUI may not appear in world: {go.name}");
                }
            }
            else
            {
                if (previewDebug) Debug.LogWarning($"[USM] Marker prefab lacks TextMeshPro (3D) or TextMeshProUGUI child — numeric labels will be skipped for marker {go.name}");
            }
        }

        previewMarkers.Add(go);
        previewMarkerLabels.Add(labelComp); // may be null when prefab doesn't provide one
        go.SetActive(false);
        return go;
    }

    private void ClearPreviewVisuals()
    {
        // previewLineDots was removed during declutter; ensure pooled tiles are hidden instead
        foreach (var p in pooledPathTiles) if (p != null) p.SetActive(false);
        foreach (var m in previewMarkers) if (m != null) m.SetActive(false);
        foreach (var lbl in previewMarkerLabels) if (lbl != null) lbl.gameObject.SetActive(false);
        previewTargetTile = -1;
    }

    /// <summary>
    /// If the currently selected unit has queued multi-turn movement segments,
    /// render a persistent path preview showing those segments until the unit is deselected.
    /// </summary>
    private void ShowQueuedPathPreviewIfAny()
    {
        // Coalesce multiple queued-preview updates in the same frame
        if (_lastQueuedPreviewFrame == Time.frameCount) return;
        _lastQueuedPreviewFrame = Time.frameCount;
        if (selectedUnit == null) return;
        var umc = UnitMovementController.Instance;
        if (umc == null) return;
        var segments = umc.GetPathSegmentsForPreview(selectedUnit);
        if (segments == null || segments.Count == 0) return;

        EnsurePreviewObjects();
        var ts = TileSystem.GetForPlanet(selectedUnit.planetIndex) ?? TileSystem.Instance;
        if (ts == null) return;

        var positions = new System.Collections.Generic.List<Vector3>();
        var breakpointNumbers = new System.Collections.Generic.List<int>();
        int lastTile = -1;
        for (int segIndex = 0; segIndex < segments.Count; segIndex++)
        {
            var seg = segments[segIndex];
            if (seg == null || seg.Count == 0) continue;
            for (int i = 0; i < seg.Count; i++)
            {
                int tile = seg[i];
                Vector3 pos = ts.GetTileSurfacePosition(tile) + Vector3.up * 0.2f;
                positions.Add(pos);
                bool isBreakpoint = (i == seg.Count - 1);
                breakpointNumbers.Add(isBreakpoint ? (segIndex + 1) : 0);
                lastTile = tile;
            }
        }

        if (positions.Count == 0)
        {
            foreach (var p in pooledPathTiles) if (p != null) p.SetActive(false);
            return;
        }

        // Cap positions to avoid spikes
        if (positions.Count > previewMaxIcons)
        {
            if (previewDebug) Debug.LogWarning($"[USM] Queued preview positions ({positions.Count}) exceed cap ({previewMaxIcons}); truncating.");
            positions.RemoveRange(previewMaxIcons, positions.Count - previewMaxIcons);
            breakpointNumbers.RemoveRange(previewMaxIcons, breakpointNumbers.Count - previewMaxIcons);
        }

        // Position pooled prefabs for tile icons
        if (pathTilePrefab != null)
        {
            int maxToShow = Mathf.Min(positions.Count, previewMaxIcons);
            for (int i = pooledPathTiles.Count; i < maxToShow; i++)
            {
                var p = Instantiate(pathTilePrefab, previewParent.transform);
                p.name = $"PathTile_{pooledPathTiles.Count}";
                p.SetActive(false);
                pooledPathTiles.Add(p);
            }

            for (int i = 0; i < pooledPathTiles.Count; i++)
            {
                var go = pooledPathTiles[i];
                if (i < maxToShow)
                {
                    if (breakpointNumbers != null && i < breakpointNumbers.Count && breakpointNumbers[i] > 0)
                    {
                        go.SetActive(false);
                        continue;
                    }

                    Vector3 targetPos = positions[i];
                    Vector3 rayStart = targetPos + Vector3.up * 4f;
                    if (Physics.Raycast(rayStart, Vector3.down, out var hit, 6f))
                    {
                        float surfaceY = hit.point.y;
                        float delta = targetPos.y - surfaceY;
                        if (Mathf.Abs(delta) > 0.05f)
                        {
                            if (previewDebug) Debug.Log($"[USM] Adjusting queued preview tile {i} Y from {targetPos.y:F3} to surface {surfaceY:F3} (delta {delta:F3})");
                            targetPos.y = surfaceY + 0.02f;
                        }
                    }

                    go.SetActive(true);
                    go.transform.position = targetPos;
                    go.transform.forward = Vector3.up;
                    var tmp3d = go.GetComponentInChildren<TextMeshPro>(true);
                    if (tmp3d != null) { tmp3d.text = ""; tmp3d.gameObject.SetActive(false); }
                    else
                    {
                        var tmpUI = go.GetComponentInChildren<TMPro.TextMeshProUGUI>(true);
                        if (tmpUI != null) { tmpUI.text = ""; tmpUI.gameObject.SetActive(false); }
                    }
                }
                else
                {
                    go.SetActive(false);
                }
            }
        }
        else
        {
            foreach (var p in pooledPathTiles) if (p != null) p.SetActive(false);
        }

        // Place per-turn markers at the last tile of each segment
        for (int mi = 0; mi < segments.Count; mi++)
        {
            var seg = segments[mi];
            if (seg == null || seg.Count == 0) continue;
            int markerTile = seg[seg.Count - 1];
            Vector3 mpos = ts.GetTileSurfacePosition(markerTile) + Vector3.up * 0.25f;
            GameObject marker = GetOrCreateMarker(mi);
            marker.transform.position = mpos;
            marker.transform.forward = Vector3.up;
            marker.SetActive(true);
            if (mi < previewMarkerLabels.Count && previewMarkerLabels[mi] != null)
            {
                var lblComp = previewMarkerLabels[mi];
                if (lblComp is TextMeshPro lbl3d) { lbl3d.text = (mi + 1).ToString(); lbl3d.gameObject.SetActive(true); }
                else if (lblComp is TMPro.TextMeshProUGUI lblUI) { lblUI.text = (mi + 1).ToString(); lblUI.gameObject.SetActive(true); }
            }
        }

        // Destination marker at the final queued tile
        if (lastTile >= 0)
        {
            ShowDestinationMarker(lastTile, ts, segments.Count);
            previewTargetTile = lastTile;
        }
    }

    // -------------------------
    // Attack hover indicator
    // -------------------------
    private void EnsureAttackHover()
    {
        if (attackHoverInstance != null) return;

        if (attackHoverPrefab != null)
        {
            attackHoverInstance = Instantiate(attackHoverPrefab, transform);
            attackHoverSpriteRenderer = attackHoverInstance.GetComponentInChildren<SpriteRenderer>();
            // Make prefab-based attack icon smaller for less visual weight
            if (attackHoverInstance != null) attackHoverInstance.transform.localScale = Vector3.one * 0.22f;
        }
        else
        {
            attackHoverInstance = new GameObject("AttackHover");
            attackHoverInstance.transform.SetParent(transform);
            attackHoverSpriteRenderer = attackHoverInstance.AddComponent<SpriteRenderer>();
            if (attackHoverIcon != null)
            {
                attackHoverSpriteRenderer.sprite = attackHoverIcon;
            }
            // Use an unlit transparent shader when available
            var shader = Shader.Find("Unlit/Transparent");
            if (shader != null)
            {
                var mat = new Material(shader);
                attackHoverSpriteRenderer.material = mat;
            }
            // Smaller by default so it doesn't overpower the scene
            attackHoverInstance.transform.localScale = Vector3.one * 0.22f;
        }

        attackHoverInstance.SetActive(false);
    }

    private void UpdateAttackHover(int tileIndex, Vector3 worldPos)
    {
        BaseUnit hovered = null;
        if (tileIndex >= 0) hovered = GetUnitOnTile(tileIndex);
        if (hovered == null) hovered = GetUnitAtPosition(worldPos);

        if (hovered == null) { ClearAttackHover(); return; }

        // Show the icon for any unit not owned by the player (always indicate potential attackable targets)
        var civMgr = CivilizationManager.Instance;
        if (civMgr != null && hovered.owner == civMgr.playerCiv) { ClearAttackHover(); return; }

        EnsureAttackHover();
        if (attackHoverInstance == null) return;

        attackHoverInstance.SetActive(true);

        // Prefer placing the hover icon above the unit's world-space label (if present)
        var ul = hovered.GetComponentInChildren<UnitLabel>();
        if (ul != null)
        {
            // Do not parent to UnitLabel (avoids inheriting the label's scale).
            // Position the hover icon in world space above the label instead.
            attackHoverInstance.transform.SetParent(transform, true);
            attackHoverInstance.transform.position = ul.transform.position + Vector3.up * 0.25f;
            attackHoverInstance.transform.rotation = Quaternion.identity;
            // Force a consistent world-scale so different unit/label scales don't change size
            attackHoverInstance.transform.localScale = Vector3.one * 0.22f;
        }
        else
        {
            // Ensure it's parented to manager root so it isn't stuck under previous label
            attackHoverInstance.transform.SetParent(transform, true);
            attackHoverInstance.transform.position = hovered.transform.position + Vector3.up * attackHoverYOffset;
            attackHoverInstance.transform.localScale = Vector3.one * 0.22f;
        }

        // If prefab provided and it uses a SpriteRenderer child, try to set sprite as override
        if (attackHoverSpriteRenderer != null)
        {
            if (attackHoverIcon != null && attackHoverSpriteRenderer.sprite == null)
                attackHoverSpriteRenderer.sprite = attackHoverIcon;
            // Ensure the hover sprite renders above most UI/world elements
            try { attackHoverSpriteRenderer.sortingOrder = 5000; } catch { }
        }
    }

    private void ClearAttackHover()
    {
        if (attackHoverInstance != null)
        {
            // Unparent to avoid remaining attached to a destroyed label
            try { attackHoverInstance.transform.SetParent(transform, true); } catch { }
            attackHoverInstance.SetActive(false);
        }
    }

    private bool CanSelectedUnitAttack(BaseUnit target)
    {
        if (selectedUnit == null || target == null) return false;
        if (selectedUnit is CombatUnit attackerCombat)
        {
            if (target is CombatUnit targetCombat) return attackerCombat.CanAttack(targetCombat);
            if (target is WorkerUnit targetWorker) return attackerCombat.CanAttack(targetWorker);
        }
        else if (selectedUnit is WorkerUnit attackerWorker)
        {
            return attackerWorker.CanAttack(target);
        }
        return false;
    }

    private bool CanPlayerAttack(BaseUnit target)
    {
        if (target == null) return false;
        var civMgr = CivilizationManager.Instance;
        if (civMgr == null || civMgr.playerCiv == null) return false;

        // Check all player's combat units
        foreach (var cu in UnitRegistry.GetCombatUnits())
        {
            if (cu == null || cu.owner != civMgr.playerCiv) continue;
            if (target is CombatUnit tc && cu.CanAttack(tc)) return true;
            if (target is WorkerUnit tw && cu.CanAttack(tw)) return true;
        }

        // Check player's workers (if they can attack)
        foreach (var wu in UnitRegistry.GetWorkerUnits())
        {
            if (wu == null || wu.owner != civMgr.playerCiv) continue;
            if (wu.CanAttack(target)) return true;
        }

        return false;
    }
    
    /// <summary>
    /// Get mouse raycast hit information using the new texture-based picking system
    /// </summary>
    private (bool hit, Vector3 worldPosition, int tileIndex) GetMouseHitInfo()
    {
        // Use TileSystem's new texture-based picking system (replaces old TileIndexHolder approach)
        int pIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        var ts = TileSystem.GetForPlanet(pIndex) ?? TileSystem.Instance;
        if (ts != null)
        {
            var result = ts.GetMouseHitInfo();
            return (result.hit, result.worldPosition, result.tileIndex);
        }

        return (false, Vector3.zero, -1);
    }
    
    
    /// <summary>
    /// Find a unit at the given world position
    /// </summary>
    private BaseUnit GetUnitAtPosition(Vector3 worldPosition)
    {
        // Use a sphere to detect units near the click position
        Collider[] colliders = Physics.OverlapSphere(worldPosition, 1.5f);
        
        foreach (var collider in colliders)
        {
            // Try to get BaseUnit (covers both CombatUnit and WorkerUnit)
            var baseUnit = collider.GetComponentInParent<BaseUnit>();
            if (baseUnit != null)
            {
                if (previewDebug) Debug.Log($"[USM] GetUnitAtPosition hit collider={collider.name} unit={baseUnit.name}");
                return baseUnit;
            }
        }
        
        // Also check orbit height for orbit-layer units
        Vector3 orbitPosition = worldPosition + Vector3.up * PlanetGenerator.GetOrbitHeight();
        Collider[] orbitColliders = Physics.OverlapSphere(orbitPosition, 1f);
        
        foreach (var collider in orbitColliders)
        {
            var baseUnit = collider.GetComponentInParent<BaseUnit>();
            if (baseUnit != null && baseUnit.currentLayer == TileLayer.Orbit)
            {
                if (previewDebug) Debug.Log($"[USM] GetUnitAtPosition orbit hit collider={collider.name} unit={baseUnit.name}");
                return baseUnit;
            }
        }
        
        return null;
    }

    /// <summary>
    /// Find a Herd near the given world position.
    /// </summary>
    private Herd GetHerdAtPosition(Vector3 worldPosition)
    {
        Collider[] colliders = Physics.OverlapSphere(worldPosition, 1.5f);
        foreach (var collider in colliders)
        {
            var herd = collider.GetComponentInParent<Herd>();
            if (herd != null)
            {
                if (previewDebug) Debug.Log($"[USM] GetHerdAtPosition hit collider={collider.name} herd={herd.name}");
                return herd;
            }
        }

        // orbit layer check (if herds are represented in orbit - unlikely but keep parity)
        Vector3 orbitPosition = worldPosition + Vector3.up * PlanetGenerator.GetOrbitHeight();
        Collider[] orbitColliders = Physics.OverlapSphere(orbitPosition, 1f);
        foreach (var collider in orbitColliders)
        {
            var herd = collider.GetComponentInParent<Herd>();
            if (herd != null)
            {
                if (previewDebug) Debug.Log($"[USM] GetHerdAtPosition orbit hit collider={collider.name} herd={herd.name}");
                return herd;
            }
        }

        return null;
    }
    
    /// <summary>
    /// Select a unit
    /// </summary>
    public void SelectUnit(BaseUnit unit)
    {
        if (unit == null)
            return;
        
        // Deselect previous unit
        DeselectUnit();
        
        // Select new unit
        selectedUnit = unit;
        lastSelectionFrame = Time.frameCount;
        
        // Play selection sound from the unit's data
        PlayUnitSelectSound(unit);

        // Create visual indicator
        CreateSelectionIndicator();
        
        // Show unit info panel
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowUnitInfoPanelForUnit(unit);
        }
        // Clear any transient preview state and show persistent queued-path (if any)
        ClearPreviewVisuals();
        ShowQueuedPathPreviewIfAny();
        // Subscribe to move-points changes for the selected unit so preview/UI updates live
        try { if (GameEventManager.Instance != null) GameEventManager.Instance.OnMovePointsChanged += OnMovePointsChanged; } catch { }
        if (previewDebug) Debug.Log($"[USM] SelectUnit -> {unit.name} (tile {unit.currentTileIndex})");
    }

    /// <summary>
    /// Select a packed herd (acts like a movable unit)
    /// </summary>
    public void SelectHerd(Herd herd)
    {
        if (herd == null) return;
        // Deselect previous selections
        DeselectUnit();
        DeselectHerd();

        selectedHerd = herd;
        lastSelectionFrame = Time.frameCount;

        // Create visual indicator
        CreateSelectionIndicator();

        if (previewDebug) Debug.Log($"[USM] SelectHerd -> {herd.name} (tile {herd.currentTileIndex})");
    }

    /// <summary>
    /// Called when any unit finishes movement. If it's the currently selected unit,
    /// refresh the persistent queued-path preview and the unit info panel so move
    /// points and markers update immediately.
    /// </summary>
    private void OnUnitMovementCompleted(GameEventManager.UnitMovementEventArgs args)
    {
        if (args == null || args.Unit == null) return;
        if (selectedUnit == null) return;

        // args.Unit is a MonoBehaviour; selectedUnit inherits MonoBehaviour
        if (args.Unit == (MonoBehaviour)selectedUnit)
        {
            if (previewDebug) { }
            // Rebuild persistent preview from moveOrderPath
            ClearPreviewVisuals();
            ShowQueuedPathPreviewIfAny();
            // Refresh the unit info panel so it reflects new movement points immediately
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowUnitInfoPanelForUnit(selectedUnit);
            }
            // If we had a pending attack target for this unit, try to execute it now
            if (pendingAttackTarget != null && args.Unit == (MonoBehaviour)selectedUnit)
            {
                try
                {
                    var tgt = pendingAttackTarget;
                    // Check range again using tile-steps
                    var ts = TileSystem.GetForPlanet(selectedUnit.planetIndex) ?? TileSystem.Instance;
                    if (ts != null && selectedUnit.currentTileIndex >= 0 && tgt.currentTileIndex >= 0)
                    {
                        int tileSteps = ts.GetWrappedHexDistance(selectedUnit.currentTileIndex, tgt.currentTileIndex);
                        int maxSteps = Mathf.FloorToInt((selectedUnit is CombatUnit sc) ? sc.CurrentRange : (selectedUnit is WorkerUnit sw ? sw.BaseRange : 0f));
                        if (tileSteps <= maxSteps)
                        {
                            // In range now — route through tactical engagement if eligible
                            if (!TryRouteTacticalEngagement(selectedUnit, tgt))
                                selectedUnit.Attack(tgt);
                            pendingAttackTarget = null;
                        }
                    }
                }
                catch { pendingAttackTarget = null; }
            }
        }
    }
    
    /// <summary>
    /// Deselect the current unit
    /// </summary>
    public void DeselectUnit()
    {
        if (selectedUnit == null)
            return;
        selectedUnit = null;

        // Remove visual indicator
        if (selectionIndicator != null)
        {
            Destroy(selectionIndicator);
            selectionIndicator = null;
        }

        // Hide the unit info panel when nothing is selected
        if (UIManager.Instance != null)
            UIManager.Instance.HideUnitInfoPanel();

        // Clear any attack hover indicator
        ClearAttackHover();
        // Clear any preview visuals when deselecting
        ClearPreviewVisuals();
        // Unsubscribe from move-points changes
        try { if (GameEventManager.Instance != null) GameEventManager.Instance.OnMovePointsChanged -= OnMovePointsChanged; } catch { }
        // Also deselect any herd selection
        DeselectHerd();
    }

    /// <summary>
    /// Deselect any selected herd.
    /// </summary>
    public void DeselectHerd()
    {
        if (selectedHerd == null) return;
        if (previewDebug) Debug.Log($"[USM] DeselectHerd -> {selectedHerd.name}");
        selectedHerd = null;
        if (selectionIndicator != null)
        {
            Destroy(selectionIndicator);
            selectionIndicator = null;
        }
        // Clear preview visuals as well
        ClearPreviewVisuals();
    }

    private void OnMovePointsChanged(GameEventManager.MovePointsChangedEventArgs args)
    {
        if (args == null || args.Unit == null) return;
        if (selectedUnit == null) return;
        if (args.Unit != (MonoBehaviour)selectedUnit) return;

        // Rebuild preview and refresh UI when the selected unit's MP changed
        if (previewDebug) Debug.Log($"[USM] OnMovePointsChanged for selected unit {selectedUnit.name} - refreshing preview/UI");
        ClearPreviewVisuals();
        ShowQueuedPathPreviewIfAny();
        UpdatePreviewVisuals();
        if (UIManager.Instance != null) UIManager.Instance.ShowUnitInfoPanelForUnit(selectedUnit);
    }
    
    /// <summary>
    /// Move the selected unit to the target tile, or attack an enemy on that tile.
    /// </summary>
    private void MoveSelectedUnitToTile(int targetTileIndex)
    {
        if (selectedUnit == null)
            return;

        // Check if there is an enemy unit on the target tile
        BaseUnit targetUnit = GetUnitOnTile(targetTileIndex);
        bool isEnemy = targetUnit != null && targetUnit.owner != selectedUnit.owner;

        if (previewDebug)
        {
            string tgtName = targetUnit != null ? targetUnit.name : "<none>";
            Debug.Log($"[USM] MoveSelectedUnitToTile called. sel={selectedUnit.name} selTile={selectedUnit.currentTileIndex} tgtTile={targetTileIndex} target={tgtName} isEnemy={isEnemy}");
            if (targetUnit != null)
            {
                // Prefer tile-based distance (same metric as movement/path preview)
                float dist = -1f;
                int tileSteps = -1;
                try
                {
                    var ts = TileSystem.GetForPlanet(selectedUnit.planetIndex) ?? TileSystem.Instance;
                    if (ts != null && selectedUnit.currentTileIndex >= 0 && targetUnit.currentTileIndex >= 0)
                    {
                        tileSteps = ts.GetWrappedHexDistance(selectedUnit.currentTileIndex, targetUnit.currentTileIndex);
                        dist = tileSteps >= 0 ? tileSteps : -1f;
                    }
                    else
                    {
                        dist = Vector3.Distance(selectedUnit.transform.position, targetUnit.transform.position);
                    }
                }
                catch (System.Exception)
                {
                    dist = Vector3.Distance(selectedUnit.transform.position, targetUnit.transform.position);
                }
                float range = (selectedUnit is CombatUnit sc) ? sc.CurrentRange : (selectedUnit is WorkerUnit sw ? sw.BaseRange : 0f);
                bool canAttack = (selectedUnit is CombatUnit cc) ? cc.CanAttack(targetUnit as CombatUnit) : (selectedUnit is WorkerUnit ww ? ww.CanAttack(targetUnit) : false);
                Debug.Log($"[USM] Target tiles: selTile={selectedUnit.currentTileIndex} tgtTile={targetUnit.currentTileIndex} tileSteps={tileSteps} distanceMetric={dist} range={range:F2} canAttack={canAttack}");
            }
        }

        // --- Attack path ---
        if (isEnemy)
        {
            bool canAttack = (selectedUnit is CombatUnit cc) ? (targetUnit is CombatUnit tc ? cc.CanAttack(tc) : (targetUnit is WorkerUnit tw ? cc.CanAttack(tw) : false)) : (selectedUnit is WorkerUnit ww ? ww.CanAttack(targetUnit) : false);
            if (canAttack)
            {
                selectedUnit.moveOrderPath = null;
                selectedUnit.moveOrderNextStep = 0;
                pendingAttackTarget = null;
                ClearPreviewVisuals();
                if (!TryRouteTacticalEngagement(selectedUnit, targetUnit))
                    selectedUnit.Attack(targetUnit);
                ShowQueuedPathPreviewIfAny();
                return;
            }
        }

        // --- Movement path ---
        bool canMove = false;
        string unitName = "";
        // If enemy and not currently attackable, try to compute an approach tile that gets us into range and move there, then attack
        if (isEnemy && targetUnit != null)
        {
            // If not attackable now, compute path and find first tile along path that puts us within attack range
            bool alreadyInRange = (selectedUnit is CombatUnit sc0) ? sc0.CanAttack(targetUnit as CombatUnit) : (selectedUnit is WorkerUnit sw0 ? sw0.CanAttack(targetUnit) : false);
            if (!alreadyInRange && UnitMovementController.Instance != null)
            {
                var ts = TileSystem.GetForPlanet(selectedUnit.planetIndex) ?? TileSystem.Instance;
                var fullPath = UnitMovementController.Instance.FindPath(selectedUnit.currentTileIndex, targetUnit.currentTileIndex, selectedUnit);
                if (fullPath != null && fullPath.Count > 0 && ts != null)
                {
                    float rangeF = (selectedUnit is CombatUnit sc) ? sc.CurrentRange : (selectedUnit is WorkerUnit sw ? sw.BaseRange : 0f);
                    int maxRangeSteps = Mathf.FloorToInt(rangeF);
                    int approachTile = -1;
                    // iterate path from start to end and find the first tile where distance to target <= range
                    for (int i = 0; i < fullPath.Count; i++)
                    {
                        int pathTile = fullPath[i];
                        int steps = ts.GetWrappedHexDistance(pathTile, targetUnit.currentTileIndex);
                        if (steps <= maxRangeSteps)
                        {
                            approachTile = pathTile;
                            break;
                        }
                    }
                    if (approachTile >= 0)
                    {
                        pendingAttackTarget = targetUnit;
                        if (selectedUnit is CombatUnit approachCombat)
                        {
                            if (approachCombat.CanReachTile(approachTile)) approachCombat.MoveTo(approachTile);
                            else pendingAttackTarget = null;
                        }
                        else if (selectedUnit is WorkerUnit workerUnit)
                        {
                            if (workerUnit.CanReachTile(approachTile)) workerUnit.MoveTo(approachTile);
                            else pendingAttackTarget = null;
                        }
                        return;
                    }
                }
            }
        }
        
        if (selectedUnit is CombatUnit combatUnit)
        {
            canMove = combatUnit.CanMoveTo(targetTileIndex);
            unitName = combatUnit.data.unitName;
            
            if (canMove)
            {
                combatUnit.MoveTo(targetTileIndex);
            }
        }
        else if (selectedUnit is WorkerUnit workerUnit)
        {
            canMove = workerUnit.CanMoveTo(targetTileIndex);
            unitName = workerUnit.data.unitName;
            
            if (canMove)
            {
                workerUnit.MoveTo(targetTileIndex);
            }
        }
        
        if (!canMove)
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.ShowNotification($"{unitName} cannot move there!");
            }
        }
    }

    /// <summary>
    /// Move the selected herd (packed) to the target tile using UnitMovementController's herd flow.
    /// </summary>
    private void MoveSelectedHerdToTile(int targetTileIndex)
    {
        if (selectedHerd == null) return;
        if (!selectedHerd.isPacked)
        {
            if (UIManager.Instance != null) UIManager.Instance.ShowNotification("Herd must be packed to move.");
            return;
        }

        // Simple check: ensure a path exists then issue herd move
        if (UnitMovementController.Instance != null)
        {
            UnitMovementController.Instance.IssueHerdMove(selectedHerd, targetTileIndex);
        }
    }
    
    /// <summary>
    /// Create a visual selection indicator for the selected unit
    /// </summary>
    private void CreateSelectionIndicator()
    {
        // Support selection indicator for either a unit or a selected herd
        if (selectedUnit == null && selectedHerd == null) return;
        Transform parentTransform = selectedUnit != null ? selectedUnit.transform : (selectedHerd != null ? (selectedHerd.visualInstance != null ? selectedHerd.visualInstance.transform : selectedHerd.transform) : null);
        if (parentTransform == null) return;
        
        // Simple approach: create a colored sphere as selection indicator
        if (selectionIndicatorPrefab != null)
        {
            selectionIndicator = Instantiate(selectionIndicatorPrefab, parentTransform);
        }
        else
        {
            // Fallback: create a simple colored sphere and reuse a shared material to avoid allocations
            selectionIndicator = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            selectionIndicator.name = "SelectionIndicator";
            selectionIndicator.transform.SetParent(parentTransform);
            selectionIndicator.transform.localPosition = Vector3.up * 0.5f;
            selectionIndicator.transform.localScale = Vector3.one * 0.3f;

            var renderer = selectionIndicator.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (s_selectionIndicatorMaterial != null)
                    renderer.sharedMaterial = s_selectionIndicatorMaterial;

                // Set color using MaterialPropertyBlock to avoid creating instance materials
                if (selectedUnit != null) s_selectionMPB.SetColor("_Color", selectedUnitHighlightColor);
                else s_selectionMPB.SetColor("_Color", selectedUnitHighlightColor);
                renderer.SetPropertyBlock(s_selectionMPB);
            }

            // Remove collider so it doesn't interfere with clicking
            var collider = selectionIndicator.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
        }
    }
    
    /// <summary>
    /// Get the name of a unit (works for both CombatUnit and WorkerUnit via BaseUnit)
    /// </summary>
    private string GetUnitName(BaseUnit unit)
    {
        if (unit != null)
            return unit.UnitName;
        return "Unknown";
    }
    
    /// <summary>
    /// Get the currently selected unit
    /// </summary>
    public BaseUnit GetSelectedUnit()
    {
        return selectedUnit;
    }
    
    /// <summary>
    /// Check if a unit is currently selected
    /// </summary>
    public bool HasSelectedUnit()
    {
        return selectedUnit != null;
    }

    /// <summary>
    /// Handle space key press to show space travel UI
    /// </summary>
    private void HandleSpaceTravelKey()
    {
        if (selectedUnit == null)
            return;

        // Get current planet index
        int currentPlanetIndex = GameManager.Instance?.currentPlanetIndex ?? 0;

        // Show embark UI
        SpaceEmbarkUI.ShowEmbarkUIForUnit(selectedUnit.gameObject, currentPlanetIndex);
    }

    /// <summary>
    /// Cycle through stacked units on the same tile as the currently selected unit.
    /// Pressing Tab selects the next unit in the stack; wraps around to the first.
    /// </summary>
    private void CycleStackedUnit()
    {
        if (selectedUnit == null) return;

        var companions = selectedUnit.GetStackedUnits();
        if (companions.Count == 0) return; // no other units in this stack

        // Build ordered list: all units on this tile including self
        var allStacked = new System.Collections.Generic.List<BaseUnit> { selectedUnit };
        allStacked.AddRange(companions);
        // Sort by stackSlot so cycling is consistent
        allStacked.Sort((a, b) => a.stackSlot.CompareTo(b.stackSlot));

        int currentIdx = allStacked.IndexOf(selectedUnit);
        int nextIdx = (currentIdx + 1) % allStacked.Count;
        var nextUnit = allStacked[nextIdx];

        SelectUnit(nextUnit);
        Debug.Log($"[USM] CycleStackedUnit: switched from {selectedUnit?.name} to {nextUnit.name} (slot {nextUnit.stackSlot})");
    }

    /// <summary>
    /// Handle M key press to show space map
    /// </summary>
    private void HandleSpaceMapKey()
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowSpaceMap();
}
        else
        {
            Debug.LogWarning("[UnitSelectionManager] UIManager.Instance is null - cannot open space map");
        }
    }

    // ================================================================
    //  Selection audio helpers
    // ================================================================

    /// <summary>
    /// Play the select sound defined on a unit's data ScriptableObject (CombatUnitData or WorkerUnitData).
    /// Uses PlayClipAtPoint so no persistent AudioSource is needed on the unit.
    /// Pitch is slightly randomised for variety.
    /// </summary>
    private void PlayUnitSelectSound(BaseUnit unit)
    {
        AudioClip clip = null;
        float pitchVar = 0.08f;

        if (unit is CombatUnit combatUnit && combatUnit.data != null)
        {
            clip = combatUnit.data.selectSound;
            pitchVar = combatUnit.data.selectPitchVariation;
        }
        else if (unit is WorkerUnit workerUnit && workerUnit.data != null)
        {
            clip = workerUnit.data.selectSound;
            pitchVar = workerUnit.data.selectPitchVariation;
        }

        if (clip != null)
            PlayClipWithPitchVariation(clip, unit.transform.position, pitchVar);
    }

    /// <summary>
    /// When clicking a tile that has no unit, check for a resource and play its sound.
    /// </summary>
    private void PlayResourceClickSound(int tileIndex)
    {
        var ts = TileSystem.Instance;
        if (ts == null) return;
        var td = ts.GetTileData(tileIndex);
        if (td == null || td.resource == null || td.resource.selectSound == null) return;

        // Use the tile's world position for spatial placement of the sound.
        Vector3 pos = ts.GetTileCenter(tileIndex);
        PlayClipWithPitchVariation(td.resource.selectSound, pos, td.resource.selectPitchVariation);
    }

    /// <summary>
    /// Play a one-shot clip at a world position with slight random pitch variation.
    /// Creates a temporary GameObject with an AudioSource (Unity's PlayClipAtPoint doesn't support pitch).
    /// </summary>
    private static void PlayClipWithPitchVariation(AudioClip clip, Vector3 position, float pitchVar)
    {
        var go = new GameObject("SelectSound");
        go.transform.position = position;
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.spatialBlend = 1f;        // 3D — panned toward entity
        src.minDistance = 5f;
        src.maxDistance = 120f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.pitch = 1f + Random.Range(-pitchVar, pitchVar);
        src.Play();
        Destroy(go, clip.length + 0.1f);
    }

    private bool TryRouteTacticalEngagement(BaseUnit attacker, BaseUnit defender)
    {
        if (attacker is not CombatUnit atkCombat || defender is not CombatUnit defCombat)
            return false;

        var mode = EngagementModeResolver.ResolveEngagementMode(attacker, defender);
        if (mode != EngagementMode.TacticalBattle)
            return false;

        var manager = BattleManager.GetOrCreate();
        var preview = manager.RequestEngagement(atkCombat, defCombat);
        if (preview == null || !preview.IsValid)
            return false;

        pendingAttackTarget = null;
        return true;
    }
}
