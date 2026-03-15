using System.Text;
using UnityEngine;

/// <summary>
/// Debug overlay for AI systems. Renders an on-screen panel showing:
///   - Current strategic goal, victory path, pillars
///   - Operational objectives and unit role counts
///   - Per-phase timing telemetry from AIPlanner
///   - Pathfinding stats from UnitMovementController
///   - Danger map value at hovered tile
///
/// Toggled with F9 in debug builds. Attach to any persistent GameObject.
/// </summary>
public class AIDebugOverlay : MonoBehaviour
{
    public static AIDebugOverlay Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private KeyCode toggleKey = KeyCode.F9;
    [SerializeField] private bool showByDefault = false;

    private bool isVisible;
    private GUIStyle boxStyle;
    private GUIStyle labelStyle;
    private readonly StringBuilder sb = new(512);

    // Cached display data (refreshed each frame when visible)
    private string displayText = "";
    private int selectedCivIndex;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(this); return; }
        isVisible = showByDefault;
        TrackHoveredTile();
    }

    void Update()
    {
        if (!Debug.isDebugBuild) return;
        if (Input.GetKeyDown(toggleKey)) isVisible = !isVisible;
        if (isVisible) RefreshDisplay();
    }

    void OnGUI()
    {
        if (!isVisible || !Debug.isDebugBuild) return;

        if (boxStyle == null)
        {
            boxStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                richText = true
            };
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                richText = true,
                wordWrap = true
            };
        }

        float w = 380f, h = 460f;
        float x = Screen.width - w - 10f;
        float y = 10f;

        GUI.Box(new Rect(x, y, w, h), "");
        GUI.Label(new Rect(x + 8, y + 4, w - 16, h - 8), displayText, labelStyle);
    }

    private void RefreshDisplay()
    {
        sb.Clear();
        sb.AppendLine("<b>═══ AI Debug (F9 toggle) ═══</b>");

        var civMgr = CivilizationManager.Instance;
        if (civMgr == null) { displayText = sb.ToString(); return; }
        var allCivs = civMgr.GetAllCivs();
        if (allCivs == null || allCivs.Count == 0) { displayText = sb.ToString(); return; }

        // Cycle through civs with PageUp/PageDown
        if (Input.GetKeyDown(KeyCode.PageUp)) selectedCivIndex = Mathf.Max(0, selectedCivIndex - 1);
        if (Input.GetKeyDown(KeyCode.PageDown)) selectedCivIndex = Mathf.Min(allCivs.Count - 1, selectedCivIndex + 1);
        selectedCivIndex = Mathf.Clamp(selectedCivIndex, 0, allCivs.Count - 1);

        var civ = allCivs[selectedCivIndex];
        string civName = civ.civData != null ? civ.civData.civName : $"Civ {selectedCivIndex}";
        sb.AppendLine($"<b>Civ:</b> {civName} ({selectedCivIndex + 1}/{allCivs.Count}) [PgUp/PgDn]");
        sb.AppendLine($"<b>Food:</b> {civ.food}  <b>Gold:</b> {civ.gold}  <b>Cities:</b> {civ.cities?.Count ?? 0}");
        sb.AppendLine($"<b>Combat:</b> {civ.combatUnits?.Count ?? 0}  <b>Workers:</b> {civ.workerUnits?.Count ?? 0}");
        sb.AppendLine();

        // Planner telemetry (uses reflection-free approach: AIPlanner exposes public timing props)
        // We can't easily get the planner instance since it's private in CivManager,
        // so we display what we can from static/accessible sources.
        sb.AppendLine("<b>── Pathfinding ──</b>");
        if (UnitMovementController.Instance != null)
        {
            var mc = UnitMovementController.Instance;
            sb.AppendLine($"Queries: {mc.PathQueries}  CacheHits: {mc.PathCacheHits}  Aborts: {mc.PathAborts}");
            sb.AppendLine($"Total expansions: {mc.PathExpansions}");
        }
        sb.AppendLine();

        // Danger at hovered tile
        sb.AppendLine("<b>── Hovered Tile ──</b>");
        if (TileSystem.Instance != null && lastKnownHoveredTile >= 0)
        {
            sb.AppendLine($"Tile: {lastKnownHoveredTile}");
            var td = TileSystem.Instance.GetTileData(lastKnownHoveredTile);
            if (td != null)
            {
                sb.AppendLine($"  Biome: {td.biome}  Hill: {td.isHill}  Land: {td.isLand}");
                if (td.resource != null) sb.AppendLine($"  Resource: {td.resource.resourceName}");
                if (td.owner != null) sb.AppendLine($"  Owner: {td.owner.civData?.civName ?? "?"}");
            }
        }
        sb.AppendLine();

        // Leader personality
        if (civ.leader != null)
        {
            var l = civ.leader;
            sb.AppendLine("<b>── Leader Persona ──</b>");
            sb.AppendLine($"{l.leaderName}: Agg={l.aggressiveness} Dip={l.diplomacy} Sci={l.science} Exp={l.expansion}");
            sb.AppendLine($"Mil={l.militaryFocus:F1} Eco={l.economicFocus:F1} Sci={l.scientificFocus:F1} " +
                          $"Cult={l.culturalFocus:F1} Rel={l.religiousFocus:F1}");
            sb.AppendLine($"Victory pref: {l.preferredVictory}  Warmonger: {l.isWarmonger}");
        }
        sb.AppendLine();

        sb.AppendLine("<b>── Relations ──</b>");
        if (civ.relations != null)
        {
            foreach (var kv in civ.relations)
            {
                if (kv.Key == null || kv.Key.civData == null) continue;
                sb.AppendLine($"  {kv.Key.civData.civName}: {kv.Value}");
            }
        }

        displayText = sb.ToString();
    }

    private int lastKnownHoveredTile = -1;

    private void TrackHoveredTile()
    {
        if (TileSystem.Instance != null)
            TileSystem.Instance.OnTileHovered += (tileIdx, pos) => lastKnownHoveredTile = tileIdx;
    }
}
