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
        if (!isReady) return;       // ← nothing runs until map is ready

        bool hovering = false;

        // Ray-cast against tile colliders
        if (GameManager.Instance?.planetGenerator != null && Camera.main != null)
        {
            var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
            if (Physics.Raycast(ray, out var hit, 1000f))
            {
                var holder = hit.collider.GetComponentInParent<TileIndexHolder>();
                if (holder != null)
                {
                    hovering = true;
                    int index = holder.tileIndex;
                    if (index >= 0 && index < PlanetGenerator.Instance.Tiles.Count)
                    {
                        var tile = PlanetGenerator.Instance.Tiles[index];

                        highlightMarker.transform.position = holder.transform.position;
                        highlightMarker.transform.up = holder.transform.up;
                        highlightMarker.SetActive(true);

                        sb.Clear();
                        sb.AppendLine($"  Biome Index: {tile.biomeIndex}   Height: {tile.height:F2}");
                        sb.AppendLine($"  Food: {tile.food}   Prod: {tile.production}");
                        sb.AppendLine($"  Gold: {tile.gold}   Sci: {tile.science}");
                        sb.AppendLine($"  Culture: {tile.culture}");
                        infoText.text = sb.ToString();
                    }
                }
            }
        }

        if (!hovering) highlightMarker.SetActive(false);
    }

    void ClearDisplay() => infoText.text = "";
}
