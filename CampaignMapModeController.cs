using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public class CampaignMapModeController : MonoBehaviour
{
    public static CampaignMapModeController Instance { get; private set; }
    [SerializeField] private CampaignMapMode currentMode = CampaignMapMode.Normal;
    [SerializeField] private CampaignMapModePresentationData presentation;
    [SerializeField] private TerrainOverlayGPU terrainOverlay;
    [SerializeField] private CampaignMapBorderRenderer borderRenderer;

    public CampaignMapMode CurrentMode => currentMode;
    public Civilization ReferenceCivilization { get; private set; }
    public int ActivePlanetIndex { get; private set; } = -1;
    public IReadOnlyList<CampaignMapLegendEntry> Legend => legend;
    public event Action<CampaignMapMode> MapModeChanged;
    public event Action LegendChanged;
    public event Action<string> HoverInfoChanged;

    private CampaignMapModeDataService dataService;
    private TileSystem tileSystem;
    private Color[] mapColorByTile;
    private int[] categoryByTile;
    private readonly List<CampaignMapLegendEntry> legend = new List<CampaignMapLegendEntry>();
    private readonly Dictionary<int, int> legendIndex = new Dictionary<int, int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureRuntimeController()
    {
        if (FindAnyObjectByType<CampaignMapModeController>() == null)
            new GameObject("Campaign Map Mode Controller").AddComponent<CampaignMapModeController>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        if (presentation == null) presentation = ScriptableObject.CreateInstance<CampaignMapModePresentationData>();
        dataService = new CampaignMapModeDataService(presentation);
        if (terrainOverlay == null) terrainOverlay = FindAnyObjectByType<TerrainOverlayGPU>();
        if (borderRenderer == null) borderRenderer = GetComponent<CampaignMapBorderRenderer>();
        if (borderRenderer == null) borderRenderer = gameObject.AddComponent<CampaignMapBorderRenderer>();
    }

    private void Start()
    {
        SetReferenceCivilization(CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null);
        SwitchPlanet(GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0);
    }

    private void Update()
    {
        int active = GameManager.Instance != null ? GameManager.Instance.currentPlanetIndex : 0;
        if (active != ActivePlanetIndex) SwitchPlanet(active);
    }

    private void OnDestroy() { Unbind(); if (Instance == this) Instance = null; }

    public void SetMode(CampaignMapMode mode)
    {
        if (!Enum.IsDefined(typeof(CampaignMapMode), mode)) mode = CampaignMapMode.Normal;
        if (currentMode == mode && mapColorByTile != null) return;
        currentMode = mode;
        RefreshAll();
        MapModeChanged?.Invoke(currentMode);
    }

    public void SetReferenceCivilization(Civilization civ)
    {
        ReferenceCivilization = IsValidCivilization(civ) ? civ : CivilizationManager.Instance?.playerCiv;
        if (currentMode == CampaignMapMode.Diplomacy) RefreshAll();
    }

    public void SwitchPlanet(int planetIndex)
    {
        Unbind(); ActivePlanetIndex = planetIndex;
        tileSystem = TileSystem.GetForPlanet(planetIndex);
        if (tileSystem == null) { mapColorByTile = null; categoryByTile = null; return; }
        tileSystem.OnTileOwnerChanged += OnOwnerChanged;
        tileSystem.OnFogChanged += OnFogChanged;
        tileSystem.OnReligionPressureChanged += OnReligionPressureChanged;
        tileSystem.OnAdministrationChanged += OnAdministrationChanged;
        tileSystem.OnTileHovered += OnTileHovered;
        tileSystem.OnTileHoverExited += OnTileHoverExited;
        DiplomacyManager diplomacy = DiplomacyManager.Instance;
        if (diplomacy != null) diplomacy.OnDiplomacyChanged += OnDiplomacyChanged;
        Civilization.GovernorAssignmentChanged += OnGovernorAssignmentChanged;
        SubscribeCivilizations(true);
        RefreshAll();
    }

    public MapModeTileVisual GetTileVisual(int tileIndex)
    {
        byte[] fog = tileSystem?.GetMergedFogArray();
        byte state = fog != null && tileIndex >= 0 && tileIndex < fog.Length ? fog[tileIndex] : (byte)0;
        return dataService.GetVisual(tileSystem, tileIndex, currentMode, ResolveReference(), state);
    }

    public void RefreshAll()
    {
        if (tileSystem == null || !tileSystem.IsReady()) return;
        int count = tileSystem.GetOwnerArray()?.Length ?? 0;
        if (mapColorByTile == null || mapColorByTile.Length != count) mapColorByTile = new Color[count];
        if (categoryByTile == null || categoryByTile.Length != count) categoryByTile = new int[count];
        legend.Clear(); legendIndex.Clear();
        for (int i = 0; i < count; i++) EvaluateTile(i, true);
        terrainOverlay?.SetMapModeData(tileSystem, mapColorByTile, currentMode != CampaignMapMode.Normal);
        borderRenderer?.Rebuild(tileSystem, currentMode, categoryByTile, mapColorByTile, presentation);
        LegendChanged?.Invoke();
    }

    private void RefreshTiles(IEnumerable<int> indices)
    {
        if (indices == null || mapColorByTile == null) return;
        var changed = new List<int>();
        foreach (int tile in indices)
        {
            if (tile < 0 || tile >= mapColorByTile.Length) continue;
            EvaluateTile(tile, false); changed.Add(tile);
        }
        terrainOverlay?.SetMapModeData(tileSystem, mapColorByTile, currentMode != CampaignMapMode.Normal, changed);
        // Borders and aggregate legend counts depend on neighbours; event-driven rebuild, never per frame.
        RebuildLegend();
        borderRenderer?.Rebuild(tileSystem, currentMode, categoryByTile, mapColorByTile, presentation);
    }

    private void EvaluateTile(int tile, bool addLegend)
    {
        MapModeTileVisual visual = GetTileVisual(tile);
        Color c = visual.Color; c.a = visual.Strength;
        mapColorByTile[tile] = c; categoryByTile[tile] = visual.CategoryId;
        if (addLegend && visual.Strength > 0f) AddLegend(visual);
    }

    private void AddLegend(MapModeTileVisual visual)
    {
        if (legendIndex.TryGetValue(visual.CategoryId, out int index))
        { var e = legend[index]; e.tileCount++; legend[index] = e; return; }
        legendIndex[visual.CategoryId] = legend.Count;
        legend.Add(new CampaignMapLegendEntry { categoryId = visual.CategoryId, label = visual.CategoryName,
            color = visual.Color, tileCount = 1 });
    }

    private void RebuildLegend()
    {
        legend.Clear(); legendIndex.Clear();
        for (int i = 0; i < categoryByTile.Length; i++)
        { var visual = GetTileVisual(i); if (visual.Strength > 0f) AddLegend(visual); }
        LegendChanged?.Invoke();
    }

    private void OnOwnerChanged(int tile, int oldOwner, int newOwner)
    { if (currentMode == CampaignMapMode.PoliticalOwnership || currentMode == CampaignMapMode.GovernmentType || currentMode == CampaignMapMode.Administration || currentMode == CampaignMapMode.Diplomacy) RefreshTiles(new[] { tile }); }
    private void OnFogChanged(int civ, List<int> tiles) { RefreshTiles(tiles); }
    private void OnReligionPressureChanged(int planet, IReadOnlyList<int> tiles) { if (planet == ActivePlanetIndex && currentMode == CampaignMapMode.Religion) RefreshTiles(tiles); }
    private void OnAdministrationChanged(int planet, IReadOnlyList<int> tiles) { if (planet == ActivePlanetIndex && currentMode == CampaignMapMode.Administration) RefreshTiles(tiles); }
    private void OnGovernorAssignmentChanged(Civilization civ, City city) { if (currentMode == CampaignMapMode.Administration && city?.planetIndex == ActivePlanetIndex) RefreshTiles(city.GetTerritoryTiles(city.TerritoryRadius)); }
    private void OnDiplomacyChanged(Civilization a, Civilization b, DiplomaticState state)
    {
        if (currentMode != CampaignMapMode.Diplomacy) return;
        Civilization affected = a == ResolveReference() ? b : (b == ResolveReference() ? a : null);
        if (affected != null && affected.ownedTilesByPlanet.TryGetValue(ActivePlanetIndex, out var tiles)) RefreshTiles(tiles);
    }
    private void OnGovernmentChanged(Civilization civ, GovernmentData government)
    { if (currentMode == CampaignMapMode.GovernmentType && civ.ownedTilesByPlanet.TryGetValue(ActivePlanetIndex, out var tiles)) RefreshTiles(tiles); }
    private void OnTileHovered(int tile, Vector3 position)
    { byte[] fog = tileSystem.GetMergedFogArray(); byte f = fog != null && tile < fog.Length ? fog[tile] : (byte)0; HoverInfoChanged?.Invoke(dataService.GetHoverText(tileSystem, tile, currentMode, ResolveReference(), f)); }
    private void OnTileHoverExited() => HoverInfoChanged?.Invoke(string.Empty);

    private void SubscribeCivilizations(bool subscribe)
    {
        var civs = CivilizationManager.Instance?.GetAllCivs(); if (civs == null) return;
        foreach (var civ in civs) if (civ != null)
        { if (subscribe) civ.OnGovernmentChanged += OnGovernmentChanged; else civ.OnGovernmentChanged -= OnGovernmentChanged; }
    }
    private void Unbind()
    {
        if (tileSystem != null)
        {
            tileSystem.OnTileOwnerChanged -= OnOwnerChanged; tileSystem.OnFogChanged -= OnFogChanged;
            tileSystem.OnReligionPressureChanged -= OnReligionPressureChanged; tileSystem.OnAdministrationChanged -= OnAdministrationChanged;
            tileSystem.OnTileHovered -= OnTileHovered; tileSystem.OnTileHoverExited -= OnTileHoverExited;
        }
        if (DiplomacyManager.Instance != null) DiplomacyManager.Instance.OnDiplomacyChanged -= OnDiplomacyChanged;
        Civilization.GovernorAssignmentChanged -= OnGovernorAssignmentChanged;
        SubscribeCivilizations(false); tileSystem = null;
    }
    private Civilization ResolveReference()
    { if (!IsValidCivilization(ReferenceCivilization)) ReferenceCivilization = CivilizationManager.Instance?.playerCiv; return ReferenceCivilization; }
    private static bool IsValidCivilization(Civilization civ)
    { return civ != null && CivilizationManager.Instance != null && CivilizationManager.Instance.GetAllCivs().Contains(civ); }
}
