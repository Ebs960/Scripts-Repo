using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "TechTreeLayoutData", menuName = "Data/Tech Tree Layout Data")]
public class TechTreeLayoutData : ScriptableObject
{
#if UNITY_EDITOR
    [MenuItem("Tools/Tech Tree/Create Layout Data Asset")]
    public static void CreateLayoutDataAsset()
    {
        TechTreeLayoutData asset = ScriptableObject.CreateInstance<TechTreeLayoutData>();
        asset.layoutName = "New Tech Tree Layout";
        asset.layoutVersion = 1;
        asset.contentSize = new Vector2(3000f, 1200f);
        asset.defaultNodeSize = new Vector2(180f, 90f);
        
        string path = "Assets/Data/TechTreeLayoutData.asset";
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
    [System.Serializable]
    public class TechPosition
    {
        [Tooltip("Name of the technology")]
        public string techName;
        
        [Tooltip("Position in the tech tree")]
        public Vector2 position;
        
        [Tooltip("Optional: Custom size for this tech node")]
        public Vector2 customSize = Vector2.zero; // Zero means use default size
        
        public TechPosition() { }
        
        public TechPosition(string name, Vector2 pos)
        {
            techName = name;
            position = pos;
        }
    }
    
    [Header("Layout Information")]
    [Tooltip("Name/description of this tech tree layout")]
    public string layoutName = "Default Tech Tree";
    
    [Tooltip("Version of this layout (for compatibility)")]
    public int layoutVersion = 1;
    
    [Header("Tech Positions")]
    [Tooltip("Positions of all technologies in this layout")]
    public List<TechPosition> techPositions = new List<TechPosition>();
    
    [Header("Layout Settings")]
    [Tooltip("Total content size of the tech tree")]
    public Vector2 contentSize = new Vector2(3000f, 1200f);
    
    [Tooltip("Default size for tech nodes in this layout")]
    public Vector2 defaultNodeSize = new Vector2(180f, 90f);
    
    /// <summary>
    /// Get the position for a specific technology
    /// </summary>
    public Vector2 GetTechPosition(string techName)
    {
        foreach (var techPos in techPositions)
        {
            if (techPos.techName == techName)
                return techPos.position;
        }
        return Vector2.zero; // Return zero if not found
    }
    
    /// <summary>
    /// Get the size for a specific technology
    /// </summary>
    public Vector2 GetTechSize(string techName)
    {
        foreach (var techPos in techPositions)
        {
            if (techPos.techName == techName)
            {
                return techPos.customSize == Vector2.zero ? defaultNodeSize : techPos.customSize;
            }
        }
        return defaultNodeSize; // Return default if not found
    }
    
    /// <summary>
    /// Check if this layout contains a specific technology
    /// </summary>
    public bool HasTech(string techName)
    {
        foreach (var techPos in techPositions)
        {
            if (techPos.techName == techName)
                return true;
        }
        return false;
    }
    
    /// <summary>
    /// Add or update a technology position
    /// </summary>
    public void SetTechPosition(string techName, Vector2 position, Vector2 customSize = default)
    {
        // Find existing entry
        for (int i = 0; i < techPositions.Count; i++)
        {
            if (techPositions[i].techName == techName)
            {
                techPositions[i].position = position;
                if (customSize != default)
                    techPositions[i].customSize = customSize;
                return;
            }
        }
        
        // Add new entry
        var newTechPos = new TechPosition(techName, position);
        if (customSize != default)
            newTechPos.customSize = customSize;
        techPositions.Add(newTechPos);
    }
    
    /// <summary>
    /// Remove a technology from the layout
    /// </summary>
    public void RemoveTech(string techName)
    {
        for (int i = techPositions.Count - 1; i >= 0; i--)
        {
            if (techPositions[i].techName == techName)
            {
                techPositions.RemoveAt(i);
                break;
            }
        }
    }
    
    /// <summary>
    /// Clear all tech positions
    /// </summary>
    public void ClearAllPositions()
    {
        techPositions.Clear();
    }
    
    /// <summary>
    /// Get all technology names in this layout
    /// </summary>
    public string[] GetAllTechNames()
    {
        string[] names = new string[techPositions.Count];
        for (int i = 0; i < techPositions.Count; i++)
        {
            names[i] = techPositions[i].techName;
        }
        return names;
    }
}
