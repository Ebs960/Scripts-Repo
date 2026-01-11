using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [SerializeField] private AudioSource musicSource;
    [SerializeField] private float fadeDuration = 1f;
    [SerializeField] private bool shufflePlaylists = true;

    private Dictionary<(string, TechAge, DiplomaticState), List<AudioClip>> musicPlaylists = new Dictionary<(string, TechAge, DiplomaticState), List<AudioClip>>();
    private List<AudioClip> currentPlaylist = new List<AudioClip>();
    private int currentTrackIndex = 0;
    private bool isChangingTrack = false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (musicSource == null)
            {
                musicSource = GetComponent<AudioSource>();
                if (musicSource == null)
                {
                musicSource = gameObject.AddComponent<AudioSource>();
}
            }

            musicSource.loop = false; // Don't loop in-game music tracks
            musicSource.playOnAwake = false; // We control playback manually
            musicSource.volume = 0.75f; // Force a reasonable volume for testing
            PlayerPrefs.SetFloat("GameMusicVolume", 0.75f); // Also reset the preference
        }
        else
        {
            Destroy(gameObject);
        }
    }

    public void PlayMusic()
    {
// Check if music is enabled
        bool musicEnabled = PlayerPrefs.GetInt("MusicEnabled", 1) == 1;
if (!musicEnabled)
        {
return;
        }

        // Play the current playlist if available
        if (currentPlaylist != null && currentPlaylist.Count > 0)
        {
PlayMusicFromList(currentPlaylist);
        }
        else
        {
            Debug.LogWarning($"[MusicManager] No playlist available to play. currentPlaylist null: {currentPlaylist == null}, count: {currentPlaylist?.Count ?? 0}");
            Debug.LogWarning($"[MusicManager] Total available playlists: {musicPlaylists.Count}");
            
            // List all available playlists for debugging
            foreach (var playlist in musicPlaylists)
            {
}
            
            // FALLBACK: Try to play any available playlist
            if (musicPlaylists.Count > 0)
            {
                var firstPlaylist = musicPlaylists.First();
PlayMusicFromList(firstPlaylist.Value);
            }
            else
            {
                Debug.LogError("[MusicManager] No playlists available at all! Music cannot play.");
            }
        }
    }

    private void PlayMusicFromList(List<AudioClip> musicList)
    {
if (musicList == null || musicList.Count == 0)
        {
            Debug.LogWarning("MusicManager: PlayMusicFromList called with empty list.");
            return;
        }

        // If already playing from same playlist, don't restart
        if (currentPlaylist == musicList && musicSource.isPlaying)
        {
return;
        }

        // MEMORY FIX: Only create a new copy if we need to shuffle, otherwise just reference
        if (shufflePlaylists)
        {
currentPlaylist = new List<AudioClip>(musicList);
            ShufflePlaylist(currentPlaylist);
        }
        else
        {
currentPlaylist = musicList; // Just reference, no copy
        }
        
        currentTrackIndex = 0;
if (!isChangingTrack)
        {
StartCoroutine(FadeAndPlayFromPlaylist());
        }
        else
        {
            Debug.LogWarning("[MusicManager] Already changing track, not starting new coroutine");
        }
    }

    public void InitializeMusicTracks()
    {
musicPlaylists.Clear();

        // Check if CivilizationManager exists
        if (CivilizationManager.Instance == null)
        {
            Debug.LogError("[MusicManager] CivilizationManager.Instance is null! Cannot initialize music tracks.");
            return;
        }

        // MEMORY FIX: Only load music for PLAYER civilization, not all civs!
        var playerCiv = CivilizationManager.Instance.playerCiv;
        if (playerCiv == null)
        {
            Debug.LogWarning("[MusicManager] No player civilization found! Cannot initialize music.");
            return;
        }

        // Only build playlists for the player's civilization
        if (playerCiv.civData?.musicData == null) 
        {
            Debug.LogWarning($"[MusicManager] Player civilization {playerCiv?.civData?.civName ?? "NULL"} has no musicData!");
            return;
        }
foreach (var ageMusic in playerCiv.civData.musicData.ageMusicTracks)
        {
            if (ageMusic.peaceMusicTracks?.Count > 0)
            {
                var peaceKey = (playerCiv.civData.civName, ageMusic.age, DiplomaticState.Peace);
                // Don't copy the list - just reference it to save memory
                musicPlaylists[peaceKey] = ageMusic.peaceMusicTracks;
            }

            if (ageMusic.warMusicTracks?.Count > 0)
            {
                var warKey = (playerCiv.civData.civName, ageMusic.age, DiplomaticState.War);
                // Don't copy the list - just reference it to save memory
                musicPlaylists[warKey] = ageMusic.warMusicTracks;
            }
        }
// Set initial music for player civilization
        var currentAge = playerCiv.GetCurrentAge();
UpdateMusic(playerCiv, currentAge, DiplomaticState.Peace);
}

    public void UpdateMusic(Civilization civ, TechAge currentAge, DiplomaticState currentState)
    {
        if (!civ.isPlayerControlled) return;

        List<AudioClip> newPlaylist = null;
        var lookupKey = (civ.civData.civName, currentAge, currentState);
if (musicPlaylists.TryGetValue(lookupKey, out newPlaylist))
        {
            if (newPlaylist == null || newPlaylist.Count == 0)
            {
                Debug.LogWarning($"MusicManager: Found playlist for {lookupKey.civName} but it's empty.");
                return;
            }
PlayMusicFromList(newPlaylist);
        }
        else
        {
            Debug.LogWarning($"MusicManager: Could not find a music playlist for key: (Civ: {lookupKey.civName}, Age: {lookupKey.currentAge}, State: {lookupKey.currentState}). No music will be played.");
        }
    }

    private bool ArePlaylistsEqual(List<AudioClip> playlist1, List<AudioClip> playlist2)
    {
        if (playlist1 == null || playlist2 == null)
            return playlist1 == playlist2;

        if (playlist1.Count != playlist2.Count)
            return false;

        for (int i = 0; i < playlist1.Count; i++)
        {
            if (playlist1[i] != playlist2[i])
                return false;
        }

        return true;
    }

    private void ShufflePlaylist(List<AudioClip> playlist)
    {
        for (int i = 0; i < playlist.Count; i++)
        {
            AudioClip temp = playlist[i];
            int randomIndex = Random.Range(i, playlist.Count);
            playlist[i] = playlist[randomIndex];
            playlist[randomIndex] = temp;
        }
    }

    private IEnumerator FadeAndPlayFromPlaylist()
    {
isChangingTrack = true;

        if (currentPlaylist == null || currentPlaylist.Count == 0)
        {
            Debug.LogWarning("[MusicManager] FadeAndPlayFromPlaylist: currentPlaylist is null or empty");
            isChangingTrack = false;
            yield break;
        }

        if (currentTrackIndex >= currentPlaylist.Count)
        {
currentTrackIndex = 0;
        }

        AudioClip nextTrack = currentPlaylist[currentTrackIndex];
if (nextTrack == null)
        {
            Debug.LogError("[MusicManager] Next track is null! Cannot play.");
            isChangingTrack = false;
            yield break;
        }

        // Fade out
        float startVolume = musicSource.volume;
        float elapsedTime = 0f;
while (elapsedTime < fadeDuration)
        {
            musicSource.volume = Mathf.Lerp(startVolume, 0f, elapsedTime / fadeDuration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // Change track
musicSource.Stop();
        musicSource.clip = nextTrack;
        musicSource.Play();
// Fade in
        elapsedTime = 0f;
while (elapsedTime < fadeDuration)
        {
            musicSource.volume = Mathf.Lerp(0f, startVolume, elapsedTime / fadeDuration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }
isChangingTrack = false;
    }

    public void OnMusicTrackFinished()
    {
        if (currentPlaylist == null || currentPlaylist.Count == 0 || isChangingTrack)
            return;

        currentTrackIndex = (currentTrackIndex + 1) % currentPlaylist.Count;
        StartCoroutine(FadeAndPlayFromPlaylist());
    }

    void Update()
    {
        // Check if current track has finished playing
        if (musicSource != null && !musicSource.isPlaying && !isChangingTrack && !musicSource.loop)
        {
            OnMusicTrackFinished();
        }
    }

    public void SetVolume(float volume)
    {
        if (musicSource != null)
        {
            musicSource.volume = volume;
            PlayerPrefs.SetFloat("GameMusicVolume", volume);
        }
    }

    public void SetMusicEnabled(bool enabled)
    {
        PlayerPrefs.SetInt("MusicEnabled", enabled ? 1 : 0);
        
        if (enabled)
        {
            // Restore volume and play if we have a playlist
            float savedVolume = PlayerPrefs.GetFloat("GameMusicVolume", 0.75f);
            SetVolume(savedVolume);
            PlayMusic();
        }
        else
        {
            // Stop music
            StopMusicImmediate();
        }
    }

    public void StopMusicImmediate()
    {
        if (musicSource != null)
        {
            musicSource.Stop();
            musicSource.clip = null;
        }
    }

    /// <summary>
    /// Clean up audio resources to free memory
    /// </summary>
    public void CleanupAudioResources()
    {
// Stop playback
        StopMusicImmediate();
        
        // Clear playlist references
        currentPlaylist?.Clear();
        currentPlaylist = null;
        
        // Clear all playlists
        musicPlaylists.Clear();
        
        // Force unload unused audio assets
        Resources.UnloadUnusedAssets();
}

    void OnDestroy()
    {
        // Clean up when MusicManager is destroyed
        CleanupAudioResources();
    }
} 