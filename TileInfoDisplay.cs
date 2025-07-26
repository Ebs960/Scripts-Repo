using UnityEngine;
using TMPro;
using System.Text;

/// <summary>
/// Shows per-tile yields when the mouse hovers the planet mesh.
/// It now stays dormant until GameSceneInitializer calls SetReady().
/// </summary>
public class TileInfoDisplay : MonoBehaviour
{
    public static TileInfoDisplay Instance { get; private set; }

    [Header("UI References")]
    [Tooltip("Text element that prints the tile's data.")]
    public TextMeshProUGUI infoText;

    [Header("UI Prefab")]
    [Tooltip("Prefab that contains the TextMeshProUGUI. Instantiated if one isn't assigned in the scene.")]
    public GameObject tileInfoUIPrefab;
    [Tooltip("Optional parent for the prefab (e.g. your Canvas).")]
    public Transform uiParent;

    [Header("Highlight Marker")]
    [Tooltip("Prefab for a ring / disc that marks the hovered tile.")]
    public GameObject highlightMarkerPrefab;

    [Header("Interaction Settings")]
    [Tooltip("Layer mask for raycasting to find tiles. Should include 'Tiles' and exclude 'Atmosphere'.")]
    public LayerMask tileLayerMask;

    // ─────────────────────────────────────────────────────────────
    // Private fields
    // ─────────────────────────────────────────────────────────────
    GameObject uiRoot;              // top-level object that holds the text
    GameObject highlightMarker;     // ring / disc instance
    readonly StringBuilder sb = new StringBuilder();
    bool isReady = false;           // stays false until map is finished
    private int lastHoveredTileIndex = -1; // Track the last hovered tile to prevent constant updates

    // ─────────────────────────────────────────────────────────────
    // Unity - Awake
    // ─────────────────────────────────────────────────────────────
    void Awake()
    {
        // Singleton
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        // (1) Build or fetch the UI object
        if (infoText == null && tileInfoUIPrefab != null)
        {
            GameObject uiInstance = Instantiate(tileInfoUIPrefab, uiParent ?? transform);
            infoText = uiInstance.GetComponentInChildren<TextMeshProUGUI>();
        }
        uiRoot = infoText != null ? infoText.transform.parent.gameObject : gameObject;
        uiRoot.SetActive(false);                // ← HIDDEN UNTIL READY
        ClearDisplay();

        // (2) Build highlight marker (or fallback)
        if (highlightMarkerPrefab != null)
            highlightMarker = Instantiate(highlightMarkerPrefab);
        else
        {
            // simple yellow ring
            highlightMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            highlightMarker.name = "FallbackHighlightMarker";
            highlightMarker.transform.localScale = new Vector3(1.1f, 0.05f, 1.1f);
            var mat = new Material(Shader.Find("Unlit/Color")) { color = new Color(1f, .92f, .2f, .6f) };
            highlightMarker.GetComponent<Renderer>().material = mat;
            Destroy(highlightMarker.GetComponent<Collider>());
        }
        highlightMarker.SetActive(false);       
    }

    // ─────────────────────────────────────────────────────────────
    // Public API: called by GameSceneInitializer when loading ends
    // ─────────────────────────────────────────────────────────────
    public void SetReady(bool value = true)
    {
        isReady = value;
        uiRoot.SetActive(value);
        if (!value) highlightMarker.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    // Unity - Update
    // ─────────────────────────────────────────────────────────────
    void Update()
    {
        if (!isReady) return;

        bool hovering = false;

        // Use TileDataHelper which is aware of both Planet and Moon generators
        if (TileDataHelper.Instance != null && Camera.main != null)
        {
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            // Raycast to find a tile, using the specified layer mask
            if (Physics.Raycast(ray, out var hit, 2000f, tileLayerMask)) // Increased distance and using layer mask
            {
                // Check if the hit object is a tile by looking for TileIndexHolder
                var tileIndexHolder = hit.collider.GetComponent<TileIndexHolder>();
                if (tileIndexHolder != null)
                {
                    hovering = true;
                    int tileIndex = tileIndexHolder.tileIndex;

                    // --- Optimization: Only update if the tile index has changed ---
                    if (tileIndex != lastHoveredTileIndex)
                    {
                        lastHoveredTileIndex = tileIndex; // Update the last hovered index

                        // Use the modern TileDataHelper to get unified tile data
                        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(tileIndex);

                        if (tileData != null)
                        {
                            // Position the highlight marker directly on the surface point hit by the raycast
                            highlightMarker.transform.position = hit.point;
                            // Align the marker with the surface normal for correct orientation on the sphere
                            highlightMarker.transform.up = hit.normal;
                            highlightMarker.SetActive(true);

                            // Build the info string using the modern HexTileData structure
                            sb.Clear();
                            string bodyName = isMoonTile ? "Moon" : "Planet";
                            string biomeName = System.Enum.GetName(typeof(Biome), tileData.biome);
                            
                            sb.AppendLine($"  <b>{biomeName}</b> {(tileData.isHill ? "(Hill)" : "")}");
                            sb.AppendLine($"  Elevation: {tileData.elevation:F2}");
                            sb.AppendLine($"  Food: {tileData.food}   Prod: {tileData.production}");
                            sb.AppendLine($"  Gold: {tileData.gold}   Sci: {tileData.science}");
                            sb.AppendLine($"  Culture: {tileData.culture}");
                            sb.AppendLine($"  <i><color=#888888>{bodyName} Tile #{tileIndex}</color></i>");

                            infoText.text = sb.ToString();
                        }
                    }
                    else
                    {
                        // It's the same tile, just make sure the marker is active
                        if (!highlightMarker.activeSelf)
                        {
                            highlightMarker.SetActive(true);
                        }
                    }
                }
            }
        }

        // If not hovering over any tile, hide the marker and clear the text
        if (!hovering)
        {
            if (lastHoveredTileIndex != -1)
            {
                lastHoveredTileIndex = -1; // Reset the index
                highlightMarker.SetActive(false);
                ClearDisplay();
            }
        }
    }

    void ClearDisplay() => infoText.text = "";
}
