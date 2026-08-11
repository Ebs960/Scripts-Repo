using System;
using UnityEngine;

public enum WorldViewMode
{
    Planet,
    StarSystem
}

public readonly struct WorldViewState
{
    public WorldViewMode Mode { get; }
    public int StarSystemId { get; }
    public string StarSystemName { get; }
    public int? PlanetIndex { get; }
    public GameManager.PlanetLayerType? PlanetLayer { get; }

    public WorldViewState(WorldViewMode mode, int starSystemId, string starSystemName, int? planetIndex, GameManager.PlanetLayerType? planetLayer)
    {
        Mode = mode;
        StarSystemId = starSystemId;
        StarSystemName = starSystemName;
        PlanetIndex = planetIndex;
        PlanetLayer = planetLayer;
    }
}

/// <summary>
/// Single authority for player world-view context (mode/system/planet/layer).
/// UI and gameplay systems can query this instead of hardcoded planet indices
/// or repeated scene lookups.
/// </summary>
public sealed class WorldViewContext : MonoBehaviour
{
    public static WorldViewContext Instance { get; private set; }

    [SerializeField] private int fallbackStarSystemId = 0;
    [SerializeField] private string fallbackStarSystemName = "Sol";

    private bool starSystemViewForced;
    private bool isStarSystemViewForced;

    private WorldViewState current;
    public WorldViewState Current => current;

    public event Action<WorldViewState> OnViewChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        ForceRefresh();
    }

    // Event-driven, not polled: every view-changing action already calls one of ForceRefresh(),
    // SetStarSystemViewActive(), ClearForcedStarSystemView(), or NotifyPlanetLayerChanged()
    // (see GameManager planet switches, SpaceMapUI.Show/Hide, LayerManager, PlanetaryCameraManager).
    // A per-frame Update() poll of BuildState() was previously run unconditionally, doing a
    // FindAnyObjectByType/activeInHierarchy scan every frame even though nothing had changed.

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void ForceRefresh()
    {
        RefreshContext(force: true);
    }

    public void SetStarSystemViewActive(bool isActive)
    {
        starSystemViewForced = isActive;
        isStarSystemViewForced = true;
        RefreshContext(force: true);
    }

    public void ClearForcedStarSystemView()
    {
        if (!isStarSystemViewForced)
            return;

        isStarSystemViewForced = false;
        RefreshContext(force: true);
    }

    public void NotifyPlanetLayerChanged(int planetIndex, GameManager.PlanetLayerType layer)
    {
        if (current.Mode == WorldViewMode.Planet && current.PlanetIndex.HasValue && current.PlanetIndex.Value == planetIndex && current.PlanetLayer == layer)
            return;

        if (current.Mode == WorldViewMode.Planet && current.PlanetIndex.HasValue && current.PlanetIndex.Value == planetIndex)
        {
            current = new WorldViewState(current.Mode, current.StarSystemId, current.StarSystemName, current.PlanetIndex, layer);
            OnViewChanged?.Invoke(current);
            return;
        }

        RefreshContext(force: false);
    }

    public static WorldViewContext GetOrCreate()
    {
        if (Instance != null)
            return Instance;

        var existing = FindAnyObjectByType<WorldViewContext>();
        if (existing != null)
            return existing;

        var go = new GameObject("WorldViewContext");
        return go.AddComponent<WorldViewContext>();
    }

    private void RefreshContext(bool force)
    {
        var next = BuildState();
        if (!force && StatesEqual(current, next))
            return;

        current = next;
        OnViewChanged?.Invoke(current);
    }

    private WorldViewState BuildState()
    {
        int starSystemId = ResolveStarSystemId();
        string starSystemName = ResolveStarSystemName(starSystemId);

        bool isStarSystemView = isStarSystemViewForced
            ? starSystemViewForced
            : IsStarSystemViewActive();

        if (isStarSystemView)
            return new WorldViewState(WorldViewMode.StarSystem, starSystemId, starSystemName, null, null);

        int planetIndex = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        var layer = ResolvePlanetLayer(planetIndex);
        return new WorldViewState(WorldViewMode.Planet, starSystemId, starSystemName, planetIndex, layer);
    }

    private int ResolveStarSystemId()
    {
        if (SpaceWorldManager.Instance != null && SpaceWorldManager.Instance.CurrentSystem != null)
            return SpaceWorldManager.Instance.CurrentSystem.starSystemId;

        return fallbackStarSystemId;
    }

    private string ResolveStarSystemName(int starSystemId)
    {
        if (!string.IsNullOrWhiteSpace(fallbackStarSystemName) && starSystemId == fallbackStarSystemId)
            return fallbackStarSystemName;

        return $"System {starSystemId}";
    }

    private static GameManager.PlanetLayerType ResolvePlanetLayer(int planetIndex)
    {
        LayerManager layerManager = null;
        var gameManager = GameManager.Instance;
        var generator = gameManager != null ? gameManager.GetPlanetGenerator(planetIndex) : null;
        if (generator != null)
            layerManager = generator.GetComponent<LayerManager>();

        layerManager ??= FindAnyObjectByType<LayerManager>();
        return layerManager != null ? layerManager.ActiveViewLayer : GameManager.PlanetLayerType.Surface;
    }

    private static bool IsStarSystemViewActive()
    {
        var spaceMapUi = UIManager.Instance != null
            ? UIManager.Instance.spaceMapUI
            : FindAnyObjectByType<SpaceMapUI>(FindObjectsInactive.Include);

        if (spaceMapUi == null)
            return false;

        if (spaceMapUi.spaceMapCanvas != null)
            return spaceMapUi.spaceMapCanvas.gameObject.activeInHierarchy;

        if (spaceMapUi.spaceMapPanel != null)
            return spaceMapUi.spaceMapPanel.activeInHierarchy;

        return spaceMapUi.gameObject.activeInHierarchy;
    }

    private static bool StatesEqual(WorldViewState a, WorldViewState b)
    {
        return a.Mode == b.Mode
            && a.StarSystemId == b.StarSystemId
            && a.StarSystemName == b.StarSystemName
            && a.PlanetIndex == b.PlanetIndex
            && a.PlanetLayer == b.PlanetLayer;
    }
}