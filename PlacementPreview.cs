using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using System;

/// <summary>
/// Shows a transparent ghost preview when placing improvements, cities, or units.
/// Snaps to hex tiles, tints green/red for valid/invalid, left-click confirms, right-click/Escape cancels.
/// Attach to an always-alive GameObject in the scene (e.g. GameManager).
/// </summary>
public class PlacementPreview : MonoBehaviour
{
    public static PlacementPreview Instance { get; private set; }

    public static PlacementPreview EnsureInstance()
    {
        if (Instance != null)
            return Instance;

        GameObject previewObject = new GameObject("PlacementPreview");
        DontDestroyOnLoad(previewObject);
        return previewObject.AddComponent<PlacementPreview>();
    }

    public enum PlacementType { None, Improvement, City, CombatUnit, WorkerUnit }

    [Header("Ghost Tinting")]
    [SerializeField] private Color validColor = new Color(0f, 1f, 0f, 0.4f);
    [SerializeField] private Color invalidColor = new Color(1f, 0f, 0f, 0.4f);
    [Header("Debug")]
    [SerializeField] private bool enableDebugLogs = true;

    // --- State ---
    private PlacementType currentType = PlacementType.None;
    private GameObject ghostInstance;
    private Renderer[] ghostRenderers;

    private WorkerUnit workerUnit;
    private ImprovementData pendingImprovement;
    private CombatUnitData pendingCombatUnit;
    private WorkerUnitData pendingWorkerUnit;

    private int hoveredTileIndex = -1;
    private Vector3 hoveredWorldPosition = Vector3.zero;
    private bool isValidPlacement;
    private bool suppressConfirmUntilMouseReleased;
    private int lastLoggedTileIndex = int.MinValue;
    private bool? lastLoggedValidity;
    private bool lastLoggedOverUi;

    private Action onConfirmCallback;
    private Action onCancelCallback;

    /// <summary>True while a placement preview is active.</summary>
    public bool IsActive => currentType != PlacementType.None;

    #region Lifecycle

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    #endregion

    #region Enter Placement Mode

    public void EnterImprovementMode(WorkerUnit worker, ImprovementData improvement, Action onConfirm = null, Action onCancel = null)
    {
        CancelInternal(false);
        currentType = PlacementType.Improvement;
        workerUnit = worker;
        pendingImprovement = improvement;
        onConfirmCallback = onConfirm;
        onCancelCallback = onCancel;
        suppressConfirmUntilMouseReleased = true;
        ResetDebugState();
        SpawnGhost(improvement != null ? (improvement.completePrefab != null ? improvement.completePrefab : improvement.constructionPrefab) : null);
        DebugLog($"Enter improvement mode | improvement={improvement?.improvementName ?? "null"} workerTile={worker?.currentTileIndex ?? -1} planet={worker?.planetIndex ?? -1}");
    }

    public void EnterCityMode(WorkerUnit worker, GameObject ghostPrefab = null, Action onConfirm = null, Action onCancel = null)
    {
        CancelInternal(false);
        currentType = PlacementType.City;
        workerUnit = worker;
        onConfirmCallback = onConfirm;
        onCancelCallback = onCancel;
        suppressConfirmUntilMouseReleased = true;
        ResetDebugState();
        SpawnGhost(ghostPrefab);
        DebugLog($"Enter city mode | workerTile={worker?.currentTileIndex ?? -1} planet={worker?.planetIndex ?? -1}");
    }

    public void EnterCombatUnitMode(WorkerUnit worker, CombatUnitData unitData, Action onConfirm = null, Action onCancel = null)
    {
        CancelInternal(false);
        currentType = PlacementType.CombatUnit;
        workerUnit = worker;
        pendingCombatUnit = unitData;
        onConfirmCallback = onConfirm;
        onCancelCallback = onCancel;
        suppressConfirmUntilMouseReleased = true;
        ResetDebugState();
        SpawnGhost(unitData != null ? unitData.GetPrefab(worker != null ? worker.owner : null) : null);
        DebugLog($"Enter combat unit mode | unit={unitData?.unitName ?? "null"} workerTile={worker?.currentTileIndex ?? -1} planet={worker?.planetIndex ?? -1}");
    }

    public void EnterWorkerUnitMode(WorkerUnit worker, WorkerUnitData workerData, Action onConfirm = null, Action onCancel = null)
    {
        CancelInternal(false);
        currentType = PlacementType.WorkerUnit;
        workerUnit = worker;
        pendingWorkerUnit = workerData;
        onConfirmCallback = onConfirm;
        onCancelCallback = onCancel;
        suppressConfirmUntilMouseReleased = true;
        ResetDebugState();
        SpawnGhost(workerData != null ? workerData.GetPrefab(worker != null ? worker.owner : null) : null);
        DebugLog($"Enter worker unit mode | unit={workerData?.unitName ?? "null"} workerTile={worker?.currentTileIndex ?? -1} planet={worker?.planetIndex ?? -1}");
    }

    #endregion

    #region Update Loop

    private void Update()
    {
        if (!IsActive) return;

        // Auto-cancel if worker was destroyed
        if (workerUnit == null)
        {
            Cancel();
            return;
        }

        // Cancel input
        bool escapePressed = Keyboard.current != null && Keyboard.current[Key.Escape].wasPressedThisFrame;
        bool rightMousePressed = Mouse.current != null && Mouse.current.rightButton.wasPressedThisFrame;
        if (escapePressed || rightMousePressed)
        {
            Cancel();
            return;
        }

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // Raycast to hex tile
        if (!overUI)
            UpdateHoveredTile();
        else
        {
            hoveredTileIndex = -1;
            hoveredWorldPosition = Vector3.zero;
        }

        // Position ghost on tile
        if (ghostInstance != null)
        {
            if (hoveredTileIndex >= 0)
            {
                PositionGhost(hoveredTileIndex);
                ghostInstance.SetActive(true);
            }
            else
            {
                ghostInstance.SetActive(false);
            }
        }

        // Validate and tint
        isValidPlacement = !overUI && ValidatePlacement(hoveredTileIndex);
        SetGhostTint(isValidPlacement);
        LogHoverState(overUI);

        if (suppressConfirmUntilMouseReleased)
        {
            if (Mouse.current == null || !Mouse.current.leftButton.isPressed)
                suppressConfirmUntilMouseReleased = false;
            return;
        }

        // Confirm on left-click
        bool leftMousePressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        if (leftMousePressed && !overUI && isValidPlacement && hoveredTileIndex >= 0)
        {
            ConfirmPlacement(hoveredTileIndex);
        }
    }

    #endregion

    #region Internal Helpers

    private TileSystem GetTileSystem()
    {
        if (workerUnit != null)
            return TileSystem.GetForPlanet(workerUnit.planetIndex) ?? TileSystem.Instance;
        return TileSystem.Instance;
    }

    private void UpdateHoveredTile()
    {
        var ts = GetTileSystem();
        if (ts == null) { hoveredTileIndex = -1; hoveredWorldPosition = Vector3.zero; return; }
        var (hit, tileIndex, worldPosition) = ts.GetMouseHitInfo();
        hoveredTileIndex = hit ? tileIndex : -1;
        hoveredWorldPosition = hit ? worldPosition : Vector3.zero;
    }

    private Vector3 GetPreviewWorldPosition(int tileIndex)
    {
        if (hoveredWorldPosition != Vector3.zero)
            return hoveredWorldPosition + Vector3.up * 0.03f;

        var ts = GetTileSystem();
        if (ts != null)
            return ts.GetTileSurfacePosition(tileIndex, 0.03f);

        return Vector3.zero;
    }

    private void PositionGhost(int tileIndex)
    {
        if (ghostInstance == null)
            return;

        Vector3 targetPosition = GetPreviewWorldPosition(tileIndex);
        ghostInstance.transform.position = targetPosition;

        var improvementInstance = ghostInstance.GetComponent<ImprovementInstance>();
        if (improvementInstance == null)
            return;

        Vector3 placementRootPosition = improvementInstance.GetPlacementRootWorldPosition();
        ghostInstance.transform.position += targetPosition - placementRootPosition;
    }

    private bool ValidatePlacement(int tileIndex)
    {
        if (tileIndex < 0 || workerUnit == null) return false;
        switch (currentType)
        {
            case PlacementType.Improvement:
                return workerUnit.CanBuildImprovementAt(pendingImprovement, tileIndex);
            case PlacementType.City:
                return workerUnit.CanFoundCityAt(tileIndex);
            case PlacementType.CombatUnit:
                return workerUnit.CanBuildUnit(pendingCombatUnit, tileIndex);
            case PlacementType.WorkerUnit:
                return workerUnit.CanBuildWorker(pendingWorkerUnit, tileIndex);
            default:
                return false;
        }
    }

    private void ConfirmPlacement(int tileIndex)
    {
        var cb = onConfirmCallback;
        Vector3 previewPosition = GetPreviewWorldPosition(tileIndex);
        DebugLog($"Confirm placement | type={currentType} tile={tileIndex} valid={isValidPlacement} hit={FormatVector3(hoveredWorldPosition)} preview={FormatVector3(previewPosition)} height={previewPosition.y:F3} planet={workerUnit?.planetIndex ?? -1}");

        switch (currentType)
        {
            case PlacementType.Improvement:
                workerUnit.ClearFortify();
                workerUnit.StartBuilding(pendingImprovement, tileIndex);
                break;
            case PlacementType.City:
                workerUnit.ClearFortify();
                workerUnit.FoundCity(tileIndex);
                break;
            case PlacementType.CombatUnit:
                workerUnit.ClearFortify();
                workerUnit.StartBuildingUnit(pendingCombatUnit, tileIndex);
                break;
            case PlacementType.WorkerUnit:
                workerUnit.ClearFortify();
                workerUnit.StartBuildingWorker(pendingWorkerUnit, tileIndex);
                break;
        }

        CancelInternal(false);
        cb?.Invoke();
    }

    /// <summary>Cancel placement mode (fires cancel callback).</summary>
    public void Cancel()
    {
        CancelInternal(true);
    }

    private void CancelInternal(bool fireCallback)
    {
        var cb = onCancelCallback;
        DebugLog($"Cancel placement | type={currentType} tile={hoveredTileIndex} valid={isValidPlacement} hit={FormatVector3(hoveredWorldPosition)} planet={workerUnit?.planetIndex ?? -1}");
        currentType = PlacementType.None;
        if (ghostInstance != null) { Destroy(ghostInstance); ghostInstance = null; }
        ghostRenderers = null;
        workerUnit = null;
        pendingImprovement = null;
        pendingCombatUnit = null;
        pendingWorkerUnit = null;
        hoveredTileIndex = -1;
        hoveredWorldPosition = Vector3.zero;
        suppressConfirmUntilMouseReleased = false;
        ResetDebugState();
        onConfirmCallback = null;
        onCancelCallback = null;
        if (fireCallback) cb?.Invoke();
    }

    #endregion

    #region Ghost Visual

    private void SpawnGhost(GameObject prefab)
    {
        ghostInstance = prefab != null ? Instantiate(prefab) : new GameObject("PlacementGhost");
        ghostInstance.name = "PlacementGhost";

        if (prefab != null)
        {
            // Disable functional components so the ghost is purely visual
            foreach (var c in ghostInstance.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            foreach (var rb in ghostInstance.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
            foreach (var mb in ghostInstance.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            foreach (var ps in ghostInstance.GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            foreach (var anim in ghostInstance.GetComponentsInChildren<Animator>(true)) anim.enabled = false;
            foreach (var src in ghostInstance.GetComponentsInChildren<AudioSource>(true)) src.enabled = false;
        }

        // Collect renderers and swap to ghost materials
        ghostRenderers = ghostInstance.GetComponentsInChildren<Renderer>(true);
        foreach (var r in ghostRenderers)
        {
            var mats = r.materials; // creates instanced copies
            for (int i = 0; i < mats.Length; i++)
                MakeGhostMaterial(mats[i], validColor);
            r.materials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        ghostInstance.SetActive(false);
    }

    private void MakeGhostMaterial(Material mat, Color color)
    {
        // Do NOT change Surface Type to Transparent — HDRP Lit materials break when
        // forced transparent at runtime. Instead, just tint the existing base color
        // with a low alpha to get the ghost effect while keeping the material opaque-ish.
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        else if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);
    }

    private void SetGhostTint(bool valid)
    {
        if (ghostRenderers == null) return;
        Color c = valid ? validColor : invalidColor;
        foreach (var r in ghostRenderers)
        {
            if (r == null) continue;
            var mats = r.sharedMaterials; // no copy — already instanced in SpawnGhost
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                if (mats[i].HasProperty("_BaseColor"))
                    mats[i].SetColor("_BaseColor", c);
                else if (mats[i].HasProperty("_Color"))
                    mats[i].SetColor("_Color", c);
            }
        }
    }

    private void LogHoverState(bool overUI)
    {
        if (!enableDebugLogs)
            return;

        bool shouldLog = hoveredTileIndex != lastLoggedTileIndex
            || lastLoggedValidity != isValidPlacement
            || lastLoggedOverUi != overUI;

        if (!shouldLog)
            return;

        lastLoggedTileIndex = hoveredTileIndex;
        lastLoggedValidity = isValidPlacement;
        lastLoggedOverUi = overUI;
    }

    private void ResetDebugState()
    {
        lastLoggedTileIndex = int.MinValue;
        lastLoggedValidity = null;
        lastLoggedOverUi = false;
    }

    private void DebugLog(string message) { }

    private static string FormatVector3(Vector3 value)
    {
        return $"({value.x:F3}, {value.y:F3}, {value.z:F3})";
    }

    #endregion
}
