using UnityEngine;
using TMPro;

public class HerdWorldUI : MonoBehaviour
{
    public TextMeshProUGUI populationText;
    public TextMeshProUGUI moveText;
    public TextMeshProUGUI yieldsText; // single field to show aggregated yields
    public TextMeshProUGUI civText;
    public TextMeshProUGUI herdNameText;

    private Herd herd;
    private Camera cam;

    public void Initialize(Herd h)
    {
        herd = h;
        cam = Camera.main;
        UpdateUI();
    }

    void LateUpdate()
    {
        if (herd == null) return;
        if (cam == null) cam = Camera.main;
        // Face camera
        if (cam != null)
        {
            transform.rotation = Quaternion.LookRotation(transform.position - cam.transform.position);
        }
        UpdateUI();
    }

    private void UpdateUI()
    {
        if (herd == null) return;
        if (populationText != null) populationText.text = $"Pop: {herd.GetPopulation()}";
        if (moveText != null) moveText.text = $"MP: {herd.movementPoints}/{herd.maxMovementPoints}";
        var tileY = herd.GetNeighborhoodTileYields();
        var animalY = herd.GetAnimalYields();
        if (yieldsText != null)
        {
            yieldsText.text = $"Yield (animals): F{animalY.Food} G{animalY.Gold} | Tiles: F{tileY.Food} G{tileY.Gold}";
        }
        // Civ and herd name
        if (civText != null) civText.text = herd.owner != null && herd.owner.civData != null ? herd.owner.civData.civName : "(No Owner)";
        var displayName = string.IsNullOrEmpty(herd.herdName) ? herd.gameObject.name : herd.herdName;
        if (herdNameText != null) herdNameText.text = displayName;
    }
}
