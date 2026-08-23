using UnityEngine;
using TMPro;

public class HerdWorldUI : MonoBehaviour
{
    public TextMeshProUGUI populationText;
    public TextMeshProUGUI moveText;
    public TextMeshProUGUI yieldsText; // single field to show aggregated yields
    public TextMeshProUGUI civText;
    public TextMeshProUGUI herdNameText;
    [Header("Disease")]
    public TextMeshProUGUI diseaseText;

    private Herd herd;
    private Camera cam;
    private bool _dirty = true;

    public void Initialize(Herd h)
    {
        herd = h;
        cam = Camera.main;
        _dirty = true;
        RefreshUIData();
    }

    /// <summary>Call this when yields/data change (turn change, herd move, production).</summary>
    public void MarkDirty() { _dirty = true; }

    void LateUpdate()
    {
        if (herd == null) return;
        if (cam == null) cam = Camera.main;
        // Face camera
        if (cam != null)
        {
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
        // Only recalculate yields when marked dirty
        if (_dirty)
        {
            _dirty = false;
            RefreshUIData();
        }
    }

    private void RefreshUIData()
    {
        if (herd == null) return;
        if (populationText != null) populationText.text = $"{herd.GetTotalAnimalCount()} livestock | {(herd.isPacked ? "Packed" : "Settled")}" + (herd.MilitaryGarrison.Count > 0 ? $" | Garrison {herd.MilitaryGarrison.Count}" : "") + (herd.IsStarving ? " | LOW FOOD" : "");
        if (moveText != null) moveText.text = $"MP: {herd.movementPoints}/{herd.maxMovementPoints}";
        var tileY = herd.GetNeighborhoodTileYields();
        var animalY = herd.GetAnimalYields();
        if (yieldsText != null)
        {
            yieldsText.text = $"Food {herd.foodReserve} ({herd.lastGrazedAmount - herd.FoodRequiredPerTurn:+#;-#;0}/turn)";
        }
        // Civ and herd name
        if (civText != null) civText.text = herd.owner != null && herd.owner.civData != null ? herd.owner.civData.civName : "(No Owner)";
        var displayName = string.IsNullOrEmpty(herd.herdName) ? herd.gameObject.name : herd.herdName;
        if (herdNameText != null) herdNameText.text = displayName;
        // Disease summary
        if (diseaseText != null)
        {
            if (herd.activeDiseases != null && herd.activeDiseases.Count > 0)
            {
                // Show top disease name and number of diseases
                var names = new System.Text.StringBuilder();
                int shown = 0;
                foreach (var d in herd.activeDiseases)
                {
                    if (d == null || d.data == null) continue;
                    if (shown > 0) names.Append(", ");
                    names.Append(d.data.diseaseName ?? "(Unknown)");
                    shown++;
                    if (shown >= 3) break;
                }
                diseaseText.text = $"Diseases: {names} ({herd.activeDiseases.Count})";
            }
            else diseaseText.text = "Diseases: None";
        }
    }
}
