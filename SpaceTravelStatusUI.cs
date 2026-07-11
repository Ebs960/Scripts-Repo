using UnityEngine;
using TMPro;

/// <summary>
/// Legacy countdown panel retired. Spacecraft now remain visible on the 3D hex map;
/// queued movement is shown by SpaceMapWorldController and SpaceMapUI selection info.
/// </summary>
public class SpaceTravelStatusUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TextMeshProUGUI statusText;
    private void Awake() { Refresh(); }
    public void Refresh()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        if (statusText != null) statusText.text = "Space movement uses visible hex paths.";
    }
}
