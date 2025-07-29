using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Loading screen specifically for planet transitions and space travel
/// </summary>
public class PlanetTransitionLoader : MonoBehaviour
{
    public static PlanetTransitionLoader Instance { get; private set; }

    [Header("UI References")]
    public Canvas loadingCanvas;
    public GameObject loadingPanel;
    public TextMeshProUGUI loadingText;
    public TextMeshProUGUI statusText;
    public Slider progressBar;
    public Image backgroundImage;
    
    [Header("Visual Effects")]
    public Animator transitionAnimator; // Optional animator for cool effects
    public ParticleSystem starfieldEffect; // Optional starfield particle effect
    public AudioSource travelSounds; // Optional travel sound effects
    
    [Header("Loading Messages")]
    public string[] travelMessages = {
        "Initiating warp drive...",
        "Calculating hyperspace coordinates...",
        "Engaging stellar navigation...",
        "Approaching target system...",
        "Entering planetary orbit...",
        "Scanning surface conditions...",
        "Preparing for atmospheric entry..."
    };
    
    [Header("Settings")]
    public float messageChangeInterval = 1.5f;
    public bool showProgressBar = true;
    public bool playTransitionEffects = true;

    private Coroutine currentLoadingCoroutine;
    private bool isLoading = false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            SetupLoadingScreen();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Setup the loading screen UI if not configured
    /// </summary>
    private void SetupLoadingScreen()
    {
        if (loadingCanvas == null)
        {
            loadingCanvas = GetComponent<Canvas>();
            if (loadingCanvas == null)
            {
                loadingCanvas = gameObject.AddComponent<Canvas>();
                loadingCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                loadingCanvas.sortingOrder = 1000; // Highest priority
                gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        if (loadingPanel == null)
        {
            CreateLoadingPanel();
        }

        // Start hidden
        Hide();
    }

    /// <summary>
    /// Create the loading panel programmatically
    /// </summary>
    private void CreateLoadingPanel()
    {
        // Main loading panel
        GameObject panelGO = new GameObject("LoadingPanel");
        panelGO.transform.SetParent(loadingCanvas.transform, false);
        loadingPanel = panelGO;
        
        // Full screen background
        backgroundImage = panelGO.AddComponent<Image>();
        backgroundImage.color = new Color(0, 0, 0.1f, 0.95f); // Dark blue
        
        RectTransform panelRect = panelGO.GetComponent<RectTransform>();
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        // Loading text
        GameObject loadingTextGO = new GameObject("LoadingText");
        loadingTextGO.transform.SetParent(panelGO.transform, false);
        loadingText = loadingTextGO.AddComponent<TextMeshProUGUI>();
        loadingText.text = "Loading...";
        loadingText.fontSize = 36;
        loadingText.alignment = TextAlignmentOptions.Center;
        loadingText.color = Color.white;
        
        RectTransform loadingTextRect = loadingTextGO.GetComponent<RectTransform>();
        loadingTextRect.anchorMin = new Vector2(0, 0.6f);
        loadingTextRect.anchorMax = new Vector2(1, 0.7f);
        loadingTextRect.offsetMin = Vector2.zero;
        loadingTextRect.offsetMax = Vector2.zero;

        // Status text (sub-message)
        GameObject statusTextGO = new GameObject("StatusText");
        statusTextGO.transform.SetParent(panelGO.transform, false);
        statusText = statusTextGO.AddComponent<TextMeshProUGUI>();
        statusText.text = "";
        statusText.fontSize = 18;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.color = Color.gray;
        
        RectTransform statusTextRect = statusTextGO.GetComponent<RectTransform>();
        statusTextRect.anchorMin = new Vector2(0, 0.5f);
        statusTextRect.anchorMax = new Vector2(1, 0.6f);
        statusTextRect.offsetMin = Vector2.zero;
        statusTextRect.offsetMax = Vector2.zero;

        // Progress bar
        if (showProgressBar)
        {
            CreateProgressBar(panelGO);
        }
    }

    /// <summary>
    /// Create the progress bar
    /// </summary>
    private void CreateProgressBar(GameObject parent)
    {
        GameObject progressGO = new GameObject("ProgressBar");
        progressGO.transform.SetParent(parent.transform, false);
        progressBar = progressGO.AddComponent<Slider>();
        
        // Background
        GameObject backgroundGO = new GameObject("Background");
        backgroundGO.transform.SetParent(progressGO.transform, false);
        Image background = backgroundGO.AddComponent<Image>();
        background.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        
        // Fill Area
        GameObject fillAreaGO = new GameObject("Fill Area");
        fillAreaGO.transform.SetParent(progressGO.transform, false);
        
        // Fill
        GameObject fillGO = new GameObject("Fill");
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        Image fill = fillGO.AddComponent<Image>();
        fill.color = new Color(0, 0.8f, 1f, 1f); // Cyan
        
        // Setup slider
        progressBar.fillRect = fillGO.GetComponent<RectTransform>();
        progressBar.value = 0f;
        
        // Position progress bar
        RectTransform progressRect = progressGO.GetComponent<RectTransform>();
        progressRect.anchorMin = new Vector2(0.2f, 0.4f);
        progressRect.anchorMax = new Vector2(0.8f, 0.45f);
        progressRect.offsetMin = Vector2.zero;
        progressRect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Show the loading screen with travel message
    /// </summary>
    public void Show(string destinationName = "Unknown Planet")
    {
        if (isLoading) return;

        isLoading = true;
        loadingPanel.SetActive(true);
        
        loadingText.text = $"Traveling to {destinationName}";
        
        if (playTransitionEffects)
        {
            StartTransitionEffects();
        }
        
        // Start message cycling
        currentLoadingCoroutine = StartCoroutine(CycleLoadingMessages());
    }

    /// <summary>
    /// Hide the loading screen
    /// </summary>
    public void Hide()
    {
        isLoading = false;
        loadingPanel.SetActive(false);
        
        if (currentLoadingCoroutine != null)
        {
            StopCoroutine(currentLoadingCoroutine);
            currentLoadingCoroutine = null;
        }
        
        StopTransitionEffects();
    }

    /// <summary>
    /// Update the progress bar
    /// </summary>
    public void SetProgress(float progress)
    {
        if (progressBar != null)
        {
            progressBar.value = Mathf.Clamp01(progress);
        }
    }

    /// <summary>
    /// Update the status text
    /// </summary>
    public void SetStatus(string status)
    {
        if (statusText != null)
        {
            statusText.text = status;
        }
    }

    /// <summary>
    /// Cycle through loading messages
    /// </summary>
    private IEnumerator CycleLoadingMessages()
    {
        int messageIndex = 0;
        
        while (isLoading)
        {
            if (travelMessages.Length > 0)
            {
                SetStatus(travelMessages[messageIndex]);
                messageIndex = (messageIndex + 1) % travelMessages.Length;
            }
            
            yield return new WaitForSeconds(messageChangeInterval);
        }
    }

    /// <summary>
    /// Start visual transition effects
    /// </summary>
    private void StartTransitionEffects()
    {
        if (transitionAnimator != null)
        {
            transitionAnimator.SetBool("IsTransitioning", true);
        }
        
        if (starfieldEffect != null)
        {
            starfieldEffect.Play();
        }
        
        if (travelSounds != null && !travelSounds.isPlaying)
        {
            travelSounds.Play();
        }
    }

    /// <summary>
    /// Stop visual transition effects
    /// </summary>
    private void StopTransitionEffects()
    {
        if (transitionAnimator != null)
        {
            transitionAnimator.SetBool("IsTransitioning", false);
        }
        
        if (starfieldEffect != null)
        {
            starfieldEffect.Stop();
        }
        
        if (travelSounds != null)
        {
            travelSounds.Stop();
        }
    }

    /// <summary>
    /// Show loading screen for a specific duration
    /// </summary>
    public IEnumerator ShowForDuration(string destinationName, float duration)
    {
        Show(destinationName);
        
        float elapsed = 0f;
        while (elapsed < duration)
        {
            SetProgress(elapsed / duration);
            elapsed += Time.deltaTime;
            yield return null;
        }
        
        SetProgress(1f);
        yield return new WaitForSeconds(0.5f); // Brief pause at 100%
        
        Hide();
    }

    /// <summary>
    /// Static method to easily show loading screen
    /// </summary>
    public static void ShowLoading(string destinationName = "Unknown Planet")
    {
        if (Instance != null)
        {
            Instance.Show(destinationName);
        }
    }

    /// <summary>
    /// Static method to easily hide loading screen
    /// </summary>
    public static void HideLoading()
    {
        if (Instance != null)
        {
            Instance.Hide();
        }
    }

    /// <summary>
    /// Static method to update progress
    /// </summary>
    public static void UpdateProgress(float progress)
    {
        if (Instance != null)
        {
            Instance.SetProgress(progress);
        }
    }

    /// <summary>
    /// Static method to update status
    /// </summary>
    public static void UpdateStatus(string status)
    {
        if (Instance != null)
        {
            Instance.SetStatus(status);
        }
    }
}
