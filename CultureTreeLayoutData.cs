using UnityEngine;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "CultureTreeLayoutData", menuName = "Data/Culture Tree Layout Data")]
public class CultureTreeLayoutData : ScriptableObject
{
    [Header("Layout Information")]
    public string layoutName = "Culture Tree Layout";
    public Vector2 contentSize = new Vector2(2000, 1000);
    public Vector2 defaultNodeSize = new Vector2(200, 100);
    
    [Header("Culture Positions")]
    [SerializeField] private List<CulturePosition> culturePositions = new List<CulturePosition>();
    
    [System.Serializable]
    public class CulturePosition
    {
        public string cultureName;
        public Vector2 position;
        public Vector2Int gridPosition;
        
        public CulturePosition(string name, Vector2 pos, Vector2Int gridPos)
        {
            cultureName = name;
            position = pos;
            gridPosition = gridPos;
        }
    }
    
    // Create default asset through menu
#if UNITY_EDITOR
    [MenuItem("Tools/Create Culture Tree Layout Data")]
    static void CreateCultureTreeLayoutData()
    {
        CultureTreeLayoutData asset = ScriptableObject.CreateInstance<CultureTreeLayoutData>();
        
        // Create Data folder if it doesn't exist
        if (!AssetDatabase.IsValidFolder("Assets/Data"))
        {
            AssetDatabase.CreateFolder("Assets", "Data");
        }
        
        string path = "Assets/Data/CultureTreeLayoutData.asset";
        
        // If file already exists, create with unique name
        if (AssetDatabase.LoadAssetAtPath<CultureTreeLayoutData>(path) != null)
        {
            path = AssetDatabase.GenerateUniqueAssetPath(path);
        }
        
        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = asset;
}
#endif

    // Public API for managing culture positions
    public void SetCulturePosition(string cultureName, Vector2 position, Vector2Int gridPosition)
    {
        if (string.IsNullOrEmpty(cultureName)) return;
        
        // Find existing position
        for (int i = 0; i < culturePositions.Count; i++)
        {
            if (culturePositions[i].cultureName == cultureName)
            {
                culturePositions[i].position = position;
                culturePositions[i].gridPosition = gridPosition;
                MarkDirty();
                return;
            }
        }
        
        // Add new position
        culturePositions.Add(new CulturePosition(cultureName, position, gridPosition));
        MarkDirty();
    }
    
    public Vector2 GetCulturePosition(string cultureName)
    {
        foreach (var pos in culturePositions)
        {
            if (pos.cultureName == cultureName)
                return pos.position;
        }
        return Vector2.zero;
    }
    
    public Vector2Int GetCultureGridPosition(string cultureName)
    {
        foreach (var pos in culturePositions)
        {
            if (pos.cultureName == cultureName)
                return pos.gridPosition;
        }
        return Vector2Int.zero;
    }
    
    public bool HasCulturePosition(string cultureName)
    {
        foreach (var pos in culturePositions)
        {
            if (pos.cultureName == cultureName)
                return true;
        }
        return false;
    }
    
    public void RemoveCulturePosition(string cultureName)
    {
        for (int i = culturePositions.Count - 1; i >= 0; i--)
        {
            if (culturePositions[i].cultureName == cultureName)
            {
                culturePositions.RemoveAt(i);
                MarkDirty();
                break;
            }
        }
    }
    
    public void ClearAllPositions()
    {
        culturePositions.Clear();
        MarkDirty();
    }
    
    public List<CulturePosition> GetAllPositions()
    {
        return new List<CulturePosition>(culturePositions);
    }
    
    public int GetCultureCount()
    {
        return culturePositions.Count;
    }
    
    private void MarkDirty()
    {
#if UNITY_EDITOR
        EditorUtility.SetDirty(this);
#endif
    }
}
