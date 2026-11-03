
using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "CultureTreeBackgroundData", menuName = "Data/Culture Tree Background Data")]
public class CultureTreeBackgroundData : ScriptableObject
{
    [System.Serializable]
    public class AgeBackground
    {
        [Tooltip("The tech/culture age this background represents")]
        public TechAge age;

        [Tooltip("Legacy age background. Any sprite aspect ratio is supported.")]
        public Sprite backgroundImage;

        [Tooltip("Use custom width instead of default 1792")]
        public bool useCustomWidth = false;

        [Tooltip("Custom width override (only used if useCustomWidth is true)")]
        public float customWidth = 1792f;
    }

    [Header("Age-Based Backgrounds")]
    [Tooltip("Assign a background image for each age. Images will be arranged in age order.")]
    public AgeBackground[] ageBackgrounds;
    [Header("Panoramic Segments (overrides age backgrounds when populated)")]
    public TreePanoramaSegment[] segments;
    [Min(1)] public float displayHeight = 1024f;
    public bool showEraMarkers;

    public List<TreePanoramaPlacement> GetPlacements()
    {
        if (segments != null && segments.Length > 0)
            return TreePanorama.Resolve(segments, backgroundScale, displayHeight, imageSpacing);
        var legacy = new List<TreePanoramaSegment>();
        if (ageBackgrounds != null)
            foreach (var entry in ageBackgrounds)
                if (entry != null)
                    legacy.Add(new TreePanoramaSegment { sprite = entry.backgroundImage,
                        startAge = entry.age, endAge = entry.age,
                        displayWidth = entry.useCustomWidth ? entry.customWidth : 1792f });
        return TreePanorama.Resolve(legacy, backgroundScale, displayHeight, imageSpacing);
    }

    [Header("Display Settings")]
    [Tooltip("Scale factor for all background images")]
    public float backgroundScale = 1f;

    [Tooltip("Spacing between background images in pixels")]
    public float imageSpacing = 0f;

    [Header("Layout Settings")]
    public Vector2 contentSize = new Vector2(2000f, 1000f);
    public Vector2 defaultNodeSize = new Vector2(200f, 100f);

#if UNITY_EDITOR
    [MenuItem("Tools/Culture Tree/Create Background Data Asset")]
    public static void CreateBackgroundDataAsset()
    {
        CultureTreeBackgroundData asset = ScriptableObject.CreateInstance<CultureTreeBackgroundData>();
        asset.contentSize = new Vector2(2000f, 1000f);
        asset.defaultNodeSize = new Vector2(200f, 100f);

        string path = "Assets/Data/CultureTreeBackgroundData.asset";
        string directory = System.IO.Path.GetDirectoryName(path);
        if (!System.IO.Directory.Exists(directory))
        {
            System.IO.Directory.CreateDirectory(directory);
        }

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();

        EditorUtility.FocusProjectWindow();
        Selection.activeObject = asset;
}
#endif

    /// <summary>
    /// Get the background sprite for a specific age
    /// </summary>
    public Sprite GetBackgroundForAge(TechAge age)
    {
        foreach (var placement in GetPlacements())
            if ((int)age >= (int)placement.startAge && (int)age <= (int)placement.endAge)
                return placement.sprite;
        return null;
    }

    /// <summary>
    /// Get all background sprites in age order
    /// </summary>
    public Sprite[] GetAllBackgroundsInOrder()
    {
        var backgrounds = new List<Sprite>();
        foreach (var placement in GetPlacements()) backgrounds.Add(placement.sprite);
        return backgrounds.ToArray();
    }

    /// <summary>
    /// Get the width (in pixels) for a specific age background, scaled
    /// </summary>
    public float GetWidthForAge(TechAge age)
    {
        return TreePanorama.AgeWidth(GetPlacements(), age);
    }

    /// <summary>
    /// Calculate total width of all backgrounds
    /// </summary>
    public float GetTotalWidth()
    {
        return TreePanorama.Size(GetPlacements()).x;
    }

    /// <summary>
    /// Get the X position where a specific age should start
    /// </summary>
    public float GetAgeStartPosition(TechAge targetAge)
    {
        return TreePanorama.AgeStart(GetPlacements(), targetAge);
    }
}
