using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "New MusicData", menuName = "Data/Music Data")]
public class MusicData : ScriptableObject
{
    [System.Serializable]
    public class AgeMusic
    {
        public TechAge age;
        [Tooltip("List of music tracks that will play during peace")]
        public List<AudioClip> peaceMusicTracks = new List<AudioClip>();
        [Tooltip("List of music tracks that will play during war")]
        public List<AudioClip> warMusicTracks = new List<AudioClip>();
    }

    public List<AgeMusic> ageMusicTracks = new List<AgeMusic>();
} 