using UnityEngine;
using UnityEngine.EventSystems;
using System;

/// <summary>
/// Shows a transparent ghost preview when placing improvements, cities, or units.
/// Snaps to hex tiles, tints green/red for valid/invalid, left-click confirms, right-click/Escape cancels.
/// Attach to an always-alive GameObject in the scene (e.g. GameManager).
/// </summary>
public class PlacementPreview : MonoBehaviour
{
    public static PlacementPreview Instance { get; private set; }

    public enum PlacementType { None, Improvement, City, CombatUnit, WorkerUnit }

    [Header("Ghost Tinting")]
    [SerializeField] private Color validColor = new Color(0f, 1f, 0f, 0.4f);
    [SerializeField] private Color invalidColor = new Color(1f, 0f, 0f, 0.4f);

    // --- State ---
    private PlacementType currentType = PlacementType.None;
    private GameObject ghostInstance;
    private Renderer[] ghostRenderers;

    private WorkerUnit workerUnit;
    private ImprovementData pendingImprovement;
    private CombatUnitData pendingCombatUnit;
    private WorkerUnitData pendingWorkerUnit;

    private int hoveredTileIndex = -1;
    private bool isValidPlacement;

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
        SpawnGhost(improvement != null ? (improvement.completePrefab != null ? improvement.completePrefab : improvement.constructionPrefab) : null);
    }

    public void EnterCityMode(WorkerUnit worker, GameObject ghostPrefab = null, Action onConfirm = null, Action onCancel = null)
    {
        CancelInternal(false);
        currentType = PlacementType.City;
        workerUnit = worker;
        onConfirmCallback = onConfirm;
        onCancelCallback = onCancel;
        SpawnGhost(ghostPrefab);
    }

    public void EnterCombatUnitMode(WorkerUnit worker, CombatUnitData unitData, Action onConfirm = null, Action onCancel = null)
    {
        CancelInternal(false);
        currentType = PlacementType.CombatUnit;
        workerUnit = worker;
        pendingCombatUnit = unitData;
        onConfirmCallback = onConfirm;
        onCancelCallback = onCancel;
        SpawnGhost(unitData != null ? unitData.GetPrefab(worker != null ? worker.owner : null) : null);
    }

    public void EnterWorkerUnitMode(WorkerUnit worker, WorkerUnitData workerData, Action onConfirm = null, Action onCancel = null)
    {
        CancelInternal(false);
        currentType = PlacementType.WorkerUnit;
        workerUnit = worker;
        pendingWorkerUnit = workerData;
        onConfirmCallback = onConfirm;
        onCancelCallback = onCancel;
        SpawnGhost(workerData != null ? workerData.GetPrefab(worker != null ? worker.owner : null) : null);
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
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
        {
            Cancel();
            return;
        }

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // Raycast to hex tile
        if (!overUI)
            UpdateHoveredTile();
        else
            hoveredTileIndex = -1;

        // Position ghost on tile
        if (ghostInstance != null)
        {
            if (hoveredTileIndex >= 0)
            {
                var ts = GetTileSystem();
                if (ts != null)
                {
                    ghostInstance.transform.position = ts.GetTileCenter(hoveredTileIndex);
                    ghostInstance.SetActive(true);
                }
            }
            else
            {
                ghostInstance.SetActive(false);
            }
        }

        // Validate and tint
        isValidPlacement = !overUI && ValidatePlacement(hoveredTileIndex);
        SetGhostTint(isValidPlacement);

        // Confirm on left-click
        if (Input.GetMouseButtonDown(0) && !overUI && isValidPlacement && hoveredTileIndex >= 0)
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
        if (ts == null) { hoveredTileIndex = -1; return; }
        var (hit, tileIndex, _) = ts.GetMouseHitInfo();
        hoveredTileIndex = hit ? tileIndex : -1;
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
        currentType = PlacementType.None;
        if (ghostInstance != null) { Destroy(ghostInstance); ghostInstance = null; }
        ghostRenderers = null;
        workerUnit = null;
        pendingImprovement = null;
        pendingCombatUnit = null;
        pendingWorkerUnit = null;
        hoveredTileIndex = -1;
        onConfirmCallback = null;
        onCancelCallback = null;
        if (fireCallback) cb?.Invoke();
    }

    #endregion

    #region Ghost Visual

    private void SpawnGhost(GameObject prefab)
    {
        if (prefab == null) return;

        ghostInstance = Instantiate(prefab);
        ghostInstance.name = "PlacementGhost";

        // Disable functional components so the ghost is purely visual
        foreach (var c in ghostInstance.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        foreach (var rb in ghostInstance.GetComponentsInChildren<Rigidbody>(true)) rb.isKinematic = true;
        foreach (var mb in ghostInstance.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
        foreach (var ps in ghostInstance.GetComponentsInChildren<ParticleSystem>(true)) ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        foreach (var anim in ghostInstance.GetComponentsInChildren<Animator>(true)) anim.enabled = false;
        foreach (var src in ghostInstance.GetComponentsInChildren<AudioSource>(true)) src.enabled = false;

        // Collect renderers and swap to ghost materials
        ghostRenderers = ghostInstance.GetComponentsInChildren<Renderer>(true);
        foreach (var r in ghostRenderers)
        {
            var mats = r.materials; // creates instanced copies
            for (int i = 0; i < mats.Length; i++)
                MakeGhostMaterial(mats[i], validColor);
            r.materials = mats;
        }

        ghostInstance.SetActive(false);
    }

    private void MakeGhostMaterial(Material mat, Color color)
    {
        // HDRP
        if (mat.HasProperty("_SurfaceType"))
        {
            mat.SetFloat("_SurfaceType", 1f); // Transparent
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.SetFloat("_AlphaDstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.renderQueue = 3000;
        }
        else
        {
            // Standard / URP fallback
            mat.SetFloat("_Mode", 3f);
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
        }

        // Set base color
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

    #endregion
}
