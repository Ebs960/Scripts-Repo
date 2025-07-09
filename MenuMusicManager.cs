using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

public class MenuMusicManager : MonoBehaviour
{
    public static MenuMusicManager Instance { get; private set; }

    [SerializeField] private AudioSource musicSource;
    [SerializeField] private float fadeDuration = 1f;
    [SerializeField] private bool shufflePlaylists = true;

    [Header("Menu Music")]
    [SerializeField] private List<AudioClip> menuMusic = new List<AudioClip>();
    [SerializeField] private List<AudioClip> mainMenuMusic = new List<AudioClip>();
    [SerializeField] private List<AudioClip> civilizationSelectionMusic = new List<AudioClip>();
    [SerializeField] private List<AudioClip> gameSetupMusic = new List<AudioClip>();
    [SerializeField] private bool loopMenuMusic = true;
    [SerializeField] private bool useSharedMenuMusic = true;

    private List<AudioClip> currentPlaylist = new List<AudioClip>();
    private int currentTrackIndex = 0;
    private bool isChangingTrack = false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            // Removed DontDestroyOnLoad so this object is destroyed on scene change

            if (musicSource == null)
            {
                musicSource = gameObject.AddComponent<AudioSource>();
                Debug.Log("MenuMusicManager: Added AudioSource component automatically");
            }

            musicSource.loop = loopMenuMusic;
            musicSource.volume = PlayerPrefs.GetFloat("MenuMusicVolume", 0.75f);

            SceneManager.sceneLoaded += OnSceneLoaded;
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        string sceneName = scene.name;

        // Stop menu music if entering game scene
        if (sceneName == "Game")
        {
            StartCoroutine(FadeOut());
            return;
        }

        // Play appropriate menu music
        if (useSharedMenuMusic)
        {
            PlayMenuMusic();
        }
        else
        {
            switch (sceneName)
            {
                case "MainMenu":
                    PlayMusicFromList(mainMenuMusic);
                    break;
                case "CivilizationSelection":
                    PlayMusicFromList(civilizationSelectionMusic);
                    break;
                case "GameSetup":
                    PlayMusicFromList(gameSetupMusic);
                    break;
            }
        }
    }

    public void PlayMenuMusic()
    {
        if (menuMusic != null && menuMusic.Count > 0)
        {
            PlayMusicFromList(menuMusic);
        }
    }

    private void PlayMusicFromList(List<AudioClip> musicList)
    {
        if (musicList == null || musicList.Count == 0)
            return;

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

    private IEnumerator FadeOut()
    {
        float startVolume = musicSource.volume;
        float elapsedTime = 0f;

        while (elapsedTime < fadeDuration)
        {
            musicSource.volume = Mathf.Lerp(startVolume, 0f, elapsedTime / fadeDuration);
            elapsedTime += Time.deltaTime;
            yield return null;
        }

        musicSource.Stop();
    }

    public void SetVolume(float volume)
    {
        if (musicSource != null)
        {
            musicSource.volume = volume;
            PlayerPrefs.SetFloat("MenuMusicVolume", volume);
        }
    }
} 