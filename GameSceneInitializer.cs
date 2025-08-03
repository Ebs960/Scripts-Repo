using UnityEngine;
using System.Collections;

/// <summary>
/// Boots the scene, shows a loading panel, starts the game, and hides the
/// loading UI when the planet is finished – then wakes TileInfoDisplay.
/// </summary>
public class GameSceneInitializer : MonoBehaviour
{
    [SerializeField] GameObject loadingPanelPrefab;
    GameObject loadingPanelInstance;

    IEnumerator Start()
    {
        // frame 0 – let Awake() run everywhere else
        yield return null;

        // 1. Spawn loading panel
        if (loadingPanelPrefab != null)
        {
            loadingPanelInstance = Instantiate(loadingPanelPrefab);
            loadingPanelInstance.SetActive(true);
            // Early yield to ensure UI updates and panel is visible
            yield return null;
        }

        // 2. Kick off world generation
        if (GameManager.Instance != null && !GameManager.Instance.gameInProgress)
            yield return StartCoroutine(GameManager.Instance.StartNewGame());

        // 3. Optional delay so the player actually sees 100 % for a moment
        yield return new WaitForSeconds(0.5f);

        // 4. Do NOT hide the loading panel here. It will be hidden by SolarSystemManager after all planets are generated.
        // Wait for SolarSystemManager to complete before waking TileInfoDisplay
        if (SolarSystemManager.Instance != null)
        {
            yield return new WaitUntil(() => SolarSystemManager.Instance != null);
            // Subscribe to the solar system initialization event
            bool solarSystemReady = false;
            SolarSystemManager.Instance.OnSolarSystemInitialized += () => solarSystemReady = true;
            yield return new WaitUntil(() => solarSystemReady);
        }

        // 5. Wake the tile-info UI (only after solar system is ready)
        if (TileInfoDisplay.Instance != null) TileInfoDisplay.Instance.SetReady();
    }
}
