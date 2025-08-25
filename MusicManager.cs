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
                    Debug.Log("MusicManager: No AudioSource found, added one automatically.");
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
        Debug.Log("[MusicManager] PlayMusic() called");
        
        // Check if music is enabled
        bool musicEnabled = PlayerPrefs.GetInt("MusicEnabled", 1) == 1;
        Debug.Log($"[MusicManager] Music enabled: {musicEnabled}");
        if (!musicEnabled)
        {
            Debug.Log("MusicManager: Music is disabled, not playing.");
            return;
        }

        // Play the current playlist if available
        if (currentPlaylist != null && currentPlaylist.Count > 0)
        {
            Debug.Log($"[MusicManager] Current playlist has {currentPlaylist.Count} tracks, playing...");
            PlayMusicFromList(currentPlaylist);
        }
        else
        {
            Debug.LogWarning($"[MusicManager] No playlist available to play. currentPlaylist null: {currentPlaylist == null}, count: {currentPlaylist?.Count ?? 0}");
            Debug.LogWarning($"[MusicManager] Total available playlists: {musicPlaylists.Count}");
            
            // List all available playlists for debugging
            foreach (var playlist in musicPlaylists)
            {
                Debug.Log($"[MusicManager] Available playlist: {playlist.Key.Item1}, {playlist.Key.Item2}, {playlist.Key.Item3} with {playlist.Value.Count} tracks");
            }
            
            // FALLBACK: Try to play any available playlist
            if (musicPlaylists.Count > 0)
            {
                var firstPlaylist = musicPlaylists.First();
                Debug.Log($"[MusicManager] FALLBACK: Playing first available playlist: {firstPlaylist.Key.Item1}, {firstPlaylist.Key.Item2}, {firstPlaylist.Key.Item3}");
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
        Debug.Log($"[MusicManager] PlayMusicFromList called with {musicList?.Count ?? 0} tracks");
        
        if (musicList == null || musicList.Count == 0)
        {
            Debug.LogWarning("MusicManager: PlayMusicFromList called with empty list.");
            return;
        }

        // If already playing from same playlist, don't restart
        if (currentPlaylist == musicList && musicSource.isPlaying)
        {
            Debug.Log("[MusicManager] Already playing from same playlist, not restarting");
            return;
        }

        currentPlaylist = new List<AudioClip>(musicList);
        currentTrackIndex = 0;

        Debug.Log($"[MusicManager] Setting up new playlist with {currentPlaylist.Count} tracks");

        if (shufflePlaylists)
        {
            Debug.Log("[MusicManager] Shuffling playlist");
            ShufflePlaylist(currentPlaylist);
        }

        if (!isChangingTrack)
        {
            Debug.Log("[MusicManager] Starting coroutine to fade and play from playlist");
            StartCoroutine(FadeAndPlayFromPlaylist());
        }
        else
        {
            Debug.LogWarning("[MusicManager] Already changing track, not starting new coroutine");
        }
    }

    public void InitializeMusicTracks()
    {
        Debug.Log("[MusicManager] InitializeMusicTracks() starting...");
        musicPlaylists.Clear();

        // Check if CivilizationManager exists
        if (CivilizationManager.Instance == null)
        {
            Debug.LogError("[MusicManager] CivilizationManager.Instance is null! Cannot initialize music tracks.");
            return;
        }

        var allCivs = CivilizationManager.Instance.GetAllCivs();

        // Build playlists for each civilization
        foreach (var civ in allCivs)
        {
            
            if (civ.civData?.musicData == null) 
            {
                Debug.LogWarning($"[MusicManager] Civilization {civ?.civData?.civName ?? "NULL"} has no musicData! Skipping...");
                continue;
            }


            foreach (var ageMusic in civ.civData.musicData.ageMusicTracks)
            {
                if (ageMusic.peaceMusicTracks?.Count > 0)
                {
                    var peaceKey = (civ.civData.civName, ageMusic.age, DiplomaticState.Peace);
                    musicPlaylists[peaceKey] = new List<AudioClip>(ageMusic.peaceMusicTracks);
                }

                if (ageMusic.warMusicTracks?.Count > 0)
                {
                    var warKey = (civ.civData.civName, ageMusic.age, DiplomaticState.War);
                    musicPlaylists[warKey] = new List<AudioClip>(ageMusic.warMusicTracks);
                }
            }
        }

        Debug.Log($"[MusicManager] Total playlists created: {musicPlaylists.Count}");

        // Set initial music for player civilization
        var playerCiv = CivilizationManager.Instance.playerCiv;
        if (playerCiv != null)
        {
            Debug.Log($"[MusicManager] Setting initial music for player civilization: {playerCiv.civData?.civName ?? "NULL"}");
            var currentAge = playerCiv.GetCurrentAge();
            Debug.Log($"[MusicManager] Player civ current age: {currentAge}");
            UpdateMusic(playerCiv, currentAge, DiplomaticState.Peace);
        }
        else
        {
            Debug.LogWarning("[MusicManager] No player civilization found! Cannot set initial music.");
        }

        Debug.Log("[MusicManager] InitializeMusicTracks() completed");
    }

    public void UpdateMusic(Civilization civ, TechAge currentAge, DiplomaticState currentState)
    {
        if (!civ.isPlayerControlled) return;

        List<AudioClip> newPlaylist = null;
        var lookupKey = (civ.civData.civName, currentAge, currentState);
        Debug.Log($"MusicManager: Attempting to update music for {lookupKey.civName}, Age: {lookupKey.currentAge}, State: {lookupKey.currentState}");
        
        if (musicPlaylists.TryGetValue(lookupKey, out newPlaylist))
        {
            if (newPlaylist == null || newPlaylist.Count == 0)
            {
                Debug.LogWarning($"MusicManager: Found playlist for {lookupKey.civName} but it's empty.");
                return;
            }

            Debug.Log($"MusicManager: Successfully found playlist with {newPlaylist.Count} tracks.");
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
        Debug.Log("[MusicManager] FadeAndPlayFromPlaylist coroutine started");
        isChangingTrack = true;

        if (currentPlaylist == null || currentPlaylist.Count == 0)
        {
            Debug.LogWarning("[MusicManager] FadeAndPlayFromPlaylist: currentPlaylist is null or empty");
            isChangingTrack = false;
            yield break;
        }

        if (currentTrackIndex >= currentPlaylist.Count)
        {
            Debug.Log("[MusicManager] Track index reset to 0");
            currentTrackIndex = 0;
        }

        AudioClip nextTrack = currentPlaylist[currentTrackIndex];
        Debug.Log($"[MusicManager] Playing track {currentTrackIndex}: {nextTrack?.name ?? "NULL"}");

        if (nextTrack == null)
        {
            Debug.LogError("[MusicManager] Next track is null! Cannot play.");
            isChangingTrack = false;
            yield break;
        }

        // Fade out
        float startVolume = musicSource.volume;
        float elapsedTime = 0f;
        Debug.Log($"[MusicManager] Starting fade out from volume {startVolume}");

        while (elapsedTime < fadeDuration)
        {
            musicSource.volume = Mathf.Lerp(startVolume, 0f, elapsedTime / fadeDuration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        // Change track
        Debug.Log("[MusicManager] Stopping current track and setting new one");
        musicSource.Stop();
        musicSource.clip = nextTrack;
        musicSource.Play();

        Debug.Log($"[MusicManager] Started playing: {nextTrack.name}, isPlaying: {musicSource.isPlaying}");

        // Fade in
        elapsedTime = 0f;
        Debug.Log($"[MusicManager] Starting fade in to volume {startVolume}");
        while (elapsedTime < fadeDuration)
        {
            musicSource.volume = Mathf.Lerp(0f, startVolume, elapsedTime / fadeDuration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        Debug.Log("[MusicManager] FadeAndPlayFromPlaylist coroutine completed");
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
} 