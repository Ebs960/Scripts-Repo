using UnityEngine;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "TechTreeBackgroundData", menuName = "Data/Tech Tree Background Data")]
public class TechTreeBackgroundData : ScriptableObject
{
    [System.Serializable]
    public class AgeBackground
    {
        [Tooltip("The tech age this background represents")]
        public TechAge age;
        
        [Tooltip("Background image for this age (1792x1024 recommended)")]
        public Sprite backgroundImage;
        
        [Tooltip("Use custom width instead of default 1792")]
        public bool useCustomWidth = false;
        
        [Tooltip("Custom width override (only used if useCustomWidth is true)")]
        public float customWidth = 1792f;
    }
    
    [Header("Age-Based Backgrounds")]
    [Tooltip("Assign a background image for each tech age. Images will be arranged in age order.")]
    public AgeBackground[] ageBackgrounds;
    
    [Header("Display Settings")]
    [Tooltip("Scale factor for all background images")]
    public float backgroundScale = 1f;
    
    [Tooltip("Spacing between background images in pixels")]
    public float imageSpacing = 0f;
    
    /// <summary>
    /// Get the background sprite for a specific age
    /// </summary>
    public Sprite GetBackgroundForAge(TechAge age)
    {
        foreach (var ageBackground in ageBackgrounds)
        {
            if (ageBackground.age == age)
                return ageBackground.backgroundImage;
        }
        return null;
    }
    
    /// <summary>
    /// Get all background images in age order
    /// </summary>
    public Sprite[] GetAllBackgroundsInOrder()
    {
        var backgrounds = new List<Sprite>();
        
        // Get all tech ages in order
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
    /// Get the width for a specific age background
    /// </summary>
    public float GetWidthForAge(TechAge age)
    {
        foreach (var ageBackground in ageBackgrounds)
        {
            if (ageBackground.age == age)
            {
                float width = ageBackground.useCustomWidth ? ageBackground.customWidth : 1792f;
                return width * backgroundScale;
            }
        }
        return 1792f * backgroundScale; // Default width
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
        
        return totalWidth - imageSpacing; // Remove last spacing
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
