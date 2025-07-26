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

    // ─────────────────────────────────────────────────────────────
    // Private fields
    // ─────────────────────────────────────────────────────────────
    GameObject uiRoot;              // top-level object that holds the text
    GameObject highlightMarker;     // ring / disc instance
    readonly StringBuilder sb = new StringBuilder();
    bool isReady = false;           // stays false until map is finished
    
    // Optimization: only update when hovering over a different tile
    int lastHoveredTileIndex = -1;
    bool wasHoveringLastFrame = false;

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
        if (!value) 
        {
            highlightMarker.SetActive(false);
            lastHoveredTileIndex = -1;
            wasHoveringLastFrame = false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Unity - Update
    // ─────────────────────────────────────────────────────────────
    void Update()
    {
        if (!isReady) return;

        bool hovering = false;
        int currentTileIndex = -1;

        // Use TileDataHelper which is aware of both Planet and Moon generators
        if (TileDataHelper.Instance != null && Camera.main != null)
        {
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            
            // Raycast with layer filtering to avoid atmosphere and other non-tile objects
            // Use a reasonable layermask - typically tiles are on Default layer (0)
            int layerMask = ~(1 << LayerMask.NameToLayer("Atmosphere")); // Exclude atmosphere layer
            
            if (Physics.Raycast(ray, out var hit, 2000f, layerMask))
            {
                // Check if the hit object is a tile by looking for TileIndexHolder
                var tileIndexHolder = hit.collider.GetComponent<TileIndexHolder>();
                if (tileIndexHolder != null)
                {
                    hovering = true;
                    currentTileIndex = tileIndexHolder.tileIndex;
                    
                    // Only update if we're hovering over a different tile than last frame
                    if (currentTileIndex != lastHoveredTileIndex)
                    {
                        // Use the modern TileDataHelper to get unified tile data
                        var (tileData, isMoonTile) = TileDataHelper.Instance.GetTileData(currentTileIndex);

                        if (tileData != null)
                        {
                            // Position the highlight marker at the tile center instead of hit point
                            Vector3 tileCenter = TileDataHelper.Instance.GetTileCenter(currentTileIndex);
                            highlightMarker.transform.position = tileCenter;
                            // Align the marker with the radial direction from planet center
                            highlightMarker.transform.up = tileCenter.normalized;
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
                            sb.AppendLine($"  <i><color=#888888>{bodyName} Tile #{currentTileIndex}</color></i>");

                            infoText.text = sb.ToString();
                        }
                        
                        lastHoveredTileIndex = currentTileIndex;
                    }
                    else if (!wasHoveringLastFrame)
                    {
                        // We're hovering over the same tile but weren't hovering last frame, show the marker
                        highlightMarker.SetActive(true);
                    }
                }
            }
        }

        // Always keep the display active once we've hovered over a tile
        // Don't hide the marker or clear text - just keep showing the last hovered tile
        wasHoveringLastFrame = hovering;
    }

    void ClearDisplay() => infoText.text = "";
}
