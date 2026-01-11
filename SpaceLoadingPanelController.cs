using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;
using System.Collections;

/// <summary>
/// Specialized loading panel for space travel with video background and spaceship display
/// </summary>
public class SpaceLoadingPanelController : MonoBehaviour
{
    public static SpaceLoadingPanelController Instance { get; private set; }

    [Header("Video Background")]
    [Tooltip("Video player for the warp drive/space travel background")]
    public VideoPlayer videoPlayer;
    [Tooltip("RawImage to display the video")]
    public RawImage videoDisplay;
    [Tooltip("Video clips for different types of space travel")]
    public VideoClip[] warpDriveClips;
    [Tooltip("Default warp drive video clip")]
    public VideoClip defaultWarpClip;

    [Header("UI Elements")]
    [Tooltip("Progress bar showing loading progress")]
    public Slider progressBar;
    [Tooltip("Text showing current loading status")]
    public TextMeshProUGUI statusText;
    [Tooltip("Text showing percentage")]
    public TextMeshProUGUI percentageText;
    [Tooltip("Panel containing the entire space loading UI")]
    public GameObject spaceLoadingPanel;

    [Header("Spaceship Display")]
    [Tooltip("Container for spaceship visualization during travel")]
    public Transform spaceshipDisplayContainer;
    [Tooltip("Manager for displaying spaceships during travel")]
    public SpaceshipDisplayManager spaceshipManager;

    [Header("Audio")]
    [Tooltip("Audio source for space travel sounds")]
    public AudioSource spaceAudioSource;
    [Tooltip("Audio clips for different warp drive sounds")]
    public AudioClip[] warpAudioClips;

    // Private fields
    private RenderTexture videoRenderTexture;
    private bool isLoading = false;
    private float currentProgress = 0f;
    private string currentStatus = "";
    
    // Video management
    private VideoClip currentVideoClip;
    private bool videoInitialized = false;

    void Awake()
    {
        // Singleton pattern
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            
            // IMPORTANT: Hide immediately on creation
            InitializeAndHide();
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        // Initialize video render texture if video player exists
        if (videoPlayer != null && videoDisplay != null)
        {
            InitializeVideoSystem();
        }

        // Initialize spaceship display manager
        if (spaceshipManager == null && spaceshipDisplayContainer != null)
        {
            spaceshipManager = spaceshipDisplayContainer.GetComponent<SpaceshipDisplayManager>();
            if (spaceshipManager == null)
            {
                spaceshipManager = spaceshipDisplayContainer.gameObject.AddComponent<SpaceshipDisplayManager>();
            }
        }

        // Hide panel by default
        if (spaceLoadingPanel != null)
            spaceLoadingPanel.SetActive(false);
    }

    /// <summary>
    /// Initialize components and hide the panel immediately
    /// </summary>
    private void InitializeAndHide()
    {
        // Find components if not assigned
        if (spaceLoadingPanel == null)
            spaceLoadingPanel = GetComponentInChildren<GameObject>();
        
        if (progressBar == null)
            progressBar = GetComponentInChildren<Slider>();
            
        if (statusText == null)
            statusText = GetComponentInChildren<TextMeshProUGUI>();

        // Hide immediately
        HideSpaceLoading();
}

    void Start()
    {
        // Ensure we're hidden at start
        HideSpaceLoading();
        
        // Prepare video system
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted += OnVideoPrepared;
            videoPlayer.loopPointReached += OnVideoLoopPointReached;
        }
    }

    /// <summary>
    /// Initialize the video rendering system
    /// </summary>
    private void InitializeVideoSystem()
    {
        // Create render texture for video
        videoRenderTexture = new RenderTexture(1920, 1080, 0);
        videoRenderTexture.Create();

        // Configure video player
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.targetTexture = videoRenderTexture;
        videoPlayer.isLooping = true;
        videoPlayer.playOnAwake = false;

        // Set video texture to raw image
        videoDisplay.texture = videoRenderTexture;

        videoInitialized = true;
}

    /// <summary>
    /// Show the space loading panel with optional spaceship display
    /// </summary>
    public void ShowSpaceLoading(string initialStatus = "Preparing for space travel...", GameObject[] playerSpaceships = null)
    {
        if (spaceLoadingPanel != null)
            spaceLoadingPanel.SetActive(true);

        isLoading = true;
        currentProgress = 0f;
        currentStatus = initialStatus;

        // Update UI
        UpdateProgressDisplay();

        // Start video background
        StartWarpDriveVideo();

        // Display player spaceships if provided
        if (playerSpaceships != null && spaceshipManager != null)
        {
            spaceshipManager.DisplaySpaceships(playerSpaceships);
        }

        // Play audio
        PlayWarpAudio();
}

    /// <summary>
    /// Hide the space loading panel
    /// </summary>
    public void HideSpaceLoading()
    {
        if (spaceLoadingPanel != null)
            spaceLoadingPanel.SetActive(false);

        isLoading = false;

        // Stop video
        if (videoPlayer != null && videoPlayer.isPlaying)
            videoPlayer.Stop();

        // Clear spaceship display
        if (spaceshipManager != null)
            spaceshipManager.ClearDisplay();

        // Stop audio
        if (spaceAudioSource != null && spaceAudioSource.isPlaying)
            spaceAudioSource.Stop();
}

    /// <summary>
    /// Update loading progress (0.0 to 1.0)
    /// </summary>
    public void SetProgress(float progress)
    {
        currentProgress = Mathf.Clamp01(progress);
        UpdateProgressDisplay();
    }

    /// <summary>
    /// Update loading status text
    /// </summary>
    public void SetStatus(string status)
    {
        currentStatus = status;
        UpdateProgressDisplay();
    }

    /// <summary>
    /// Update the progress bar and text displays
    /// </summary>
    private void UpdateProgressDisplay()
    {
        if (progressBar != null)
            progressBar.value = currentProgress;

        if (statusText != null)
            statusText.text = currentStatus;

        if (percentageText != null)
            percentageText.text = $"{(currentProgress * 100f):F0}%";
    }

    /// <summary>
    /// Start playing the warp drive video background
    /// </summary>
    private void StartWarpDriveVideo()
    {
        if (videoPlayer == null || !videoInitialized) return;

        // Select video clip
        VideoClip clipToPlay = defaultWarpClip;
        if (warpDriveClips != null && warpDriveClips.Length > 0)
        {
            clipToPlay = warpDriveClips[Random.Range(0, warpDriveClips.Length)];
        }

        if (clipToPlay != null)
        {
            currentVideoClip = clipToPlay;
            videoPlayer.clip = clipToPlay;
            videoPlayer.Prepare();
        }
    }

    /// <summary>
    /// Play warp drive audio
    /// </summary>
    private void PlayWarpAudio()
    {
        if (spaceAudioSource == null) return;

        if (warpAudioClips != null && warpAudioClips.Length > 0)
        {
            AudioClip clipToPlay = warpAudioClips[Random.Range(0, warpAudioClips.Length)];
            spaceAudioSource.clip = clipToPlay;
            spaceAudioSource.loop = true;
            spaceAudioSource.Play();
        }
    }

    /// <summary>
    /// Called when video is prepared and ready to play
    /// </summary>
    private void OnVideoPrepared(VideoPlayer source)
    {
        if (isLoading)
        {
            source.Play();
}
    }

    /// <summary>
    /// Called when video reaches loop point
    /// </summary>
    private void OnVideoLoopPointReached(VideoPlayer source)
    {
        // Video will automatically loop due to isLooping = true
        // This is here for future custom loop logic if needed
    }

    /// <summary>
    /// Change the warp drive effect (for different travel types)
    /// </summary>
    public void SetWarpDriveType(WarpDriveType driveType)
    {
        if (warpDriveClips == null || warpDriveClips.Length == 0) return;

        int clipIndex = (int)driveType % warpDriveClips.Length;
        VideoClip newClip = warpDriveClips[clipIndex];

        if (newClip != currentVideoClip && videoPlayer != null)
        {
            currentVideoClip = newClip;
            videoPlayer.clip = newClip;
            
            if (isLoading)
            {
                videoPlayer.Prepare();
            }
        }
    }

    void OnDestroy()
    {
        // Clean up singleton reference
        if (Instance == this)
        {
            Instance = null;
        }
        
        // Clean up render texture
        if (videoRenderTexture != null)
        {
            videoRenderTexture.Release();
            videoRenderTexture = null;
        }

        // Unsubscribe from video events
        if (videoPlayer != null)
        {
            videoPlayer.prepareCompleted -= OnVideoPrepared;
            videoPlayer.loopPointReached -= OnVideoLoopPointReached;
        }
    }
}

/// <summary>
/// Types of warp drive effects for different travel scenarios
/// </summary>
public enum WarpDriveType
{
    Standard,       // Regular space travel
    Hyperspace,     // Fast interstellar travel
    Quantum,        // Quantum tunneling effects
    Experimental    // Advanced/alien technology
}
