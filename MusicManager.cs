using UnityEngine;
using System.Collections;
using System.Collections.Generic;

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
        // Play the current playlist if available
        if (currentPlaylist != null && currentPlaylist.Count > 0)
        {
            PlayMusicFromList(currentPlaylist);
        }
        else
        {
            Debug.LogWarning("MusicManager: No playlist available to play.");
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
            return;

        currentPlaylist = new List<AudioClip>(musicList);
        currentTrackIndex = 0;

        if (shufflePlaylists)
            ShufflePlaylist(currentPlaylist);

        if (!isChangingTrack)
            StartCoroutine(FadeAndPlayFromPlaylist());
    }

    public void InitializeMusicTracks()
    {
        musicPlaylists.Clear();

        // Build playlists for each civilization
        foreach (var civ in CivilizationManager.Instance.GetAllCivs())
        {
            if (civ.civData?.musicData == null) continue;

            foreach (var ageMusic in civ.civData.musicData.ageMusicTracks)
            {
                if (ageMusic.peaceMusicTracks?.Count > 0)
                    musicPlaylists[(civ.civData.civName, ageMusic.age, DiplomaticState.Peace)] = 
                        new List<AudioClip>(ageMusic.peaceMusicTracks);

                if (ageMusic.warMusicTracks?.Count > 0)
                    musicPlaylists[(civ.civData.civName, ageMusic.age, DiplomaticState.War)] = 
                        new List<AudioClip>(ageMusic.warMusicTracks);
            }
        }

        // Set initial music for player civilization
        var playerCiv = CivilizationManager.Instance.playerCiv;
        if (playerCiv != null)
        {
            UpdateMusic(playerCiv, playerCiv.GetCurrentAge(), DiplomaticState.Peace);
        }
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
        isChangingTrack = true;

        if (currentPlaylist == null || currentPlaylist.Count == 0)
        {
            isChangingTrack = false;
            yield break;
        }

        if (currentTrackIndex >= currentPlaylist.Count)
            currentTrackIndex = 0;

        AudioClip nextTrack = currentPlaylist[currentTrackIndex];

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

    public void StopMusicImmediate()
    {
        if (musicSource != null)
        {
            musicSource.Stop();
            musicSource.clip = null;
        }
    }
} 