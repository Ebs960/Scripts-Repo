
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

        [Tooltip("Background image for this age (recommended 1792x1024)")]
        public Sprite backgroundImage;

        [Tooltip("Use custom width instead of default 1792")]
        public bool useCustomWidth = false;

        [Tooltip("Custom width override (only used if useCustomWidth is true)")]
        public float customWidth = 1792f;
    }

    [Header("Age-Based Backgrounds")]
    [Tooltip("Assign a background image for each age. Images will be arranged in age order.")]
    public AgeBackground[] ageBackgrounds;

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

        Debug.Log($"Created CultureTreeBackgroundData asset at: {path}");
    }
#endif

    /// <summary>
    /// Get the background sprite for a specific age
    /// </summary>
    public Sprite GetBackgroundForAge(TechAge age)
    {
        if (ageBackgrounds == null) return null;
        foreach (var ageBackground in ageBackgrounds)
        {
            if (ageBackground.age == age)
                return ageBackground.backgroundImage;
        }
        return null;
    }

    /// <summary>
    /// Get all background sprites in age order
    /// </summary>
    public Sprite[] GetAllBackgroundsInOrder()
    {
        var backgrounds = new List<Sprite>();
        var allAges = System.Enum.GetValues(typeof(TechAge));

        foreach (TechAge age in allAges)
        {
            var background = GetBackgroundForAge(age);
            if (background != null)
            {
                backgrounds.Add(background);
            }
        }

        return backgrounds.ToArray();
    }

    /// <summary>
    /// Get the width (in pixels) for a specific age background, scaled
    /// </summary>
    public float GetWidthForAge(TechAge age)
    {
        if (ageBackgrounds == null) return 1792f * backgroundScale;
        foreach (var ageBackground in ageBackgrounds)
        {
            if (ageBackground.age == age)
            {
                float width = ageBackground.useCustomWidth ? ageBackground.customWidth : 1792f;
                return width * backgroundScale;
            }
        }
        return 1792f * backgroundScale;
    }

    /// <summary>
    /// Calculate total width of all backgrounds
    /// </summary>
    public float GetTotalWidth()
    {
        float totalWidth = 0f;
        var allAges = System.Enum.GetValues(typeof(TechAge));

        foreach (TechAge age in allAges)
        {
            var background = GetBackgroundForAge(age);
            if (background != null)
            {
                totalWidth += GetWidthForAge(age) + imageSpacing;
            }
        }

        return Mathf.Max(0f, totalWidth - imageSpacing);
    }

    /// <summary>
    /// Get the X position where a specific age should start
    /// </summary>
    public float GetAgeStartPosition(TechAge targetAge)
    {
        float currentX = 0f;
        var allAges = System.Enum.GetValues(typeof(TechAge));

        foreach (TechAge age in allAges)
        {
            if (age == targetAge)
                return currentX;

            var background = GetBackgroundForAge(age);
            if (background != null)
            {
                currentX += GetWidthForAge(age) + imageSpacing;
            }
        }

        return currentX;
    }
}
