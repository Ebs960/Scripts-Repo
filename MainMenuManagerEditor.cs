using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MainMenuManager))]
public class MainMenuManagerEditor : Editor
{
    SerializedProperty spriteEntriesProp;
    
    // MEMORY FIX: STATIC cache - persists across inspector open/close and even domain reloads
    // Map type names are constant strings that never change during a session
    // Building them costs CPU, so we cache forever until explicit refresh
    private static List<string> s_allNames;
    private static string[] s_cachedNamesArray;
    private static bool s_namesInitialized = false;

    void OnEnable()
    {
        spriteEntriesProp = serializedObject.FindProperty("mapTypeSpriteEntries");
        
        // Only build if never built before (static persists across inspector open/close)
        if (!s_namesInitialized || s_cachedNamesArray == null)
        {
            RefreshMapTypeNames();
        }
    }
    
    // No OnDisable cleanup - we WANT the cache to persist forever!

    private void RefreshMapTypeNames()
    {
        s_allNames = MapTypeNameGenerator.BuildAllNames();
        s_cachedNamesArray = s_allNames.ToArray();
        s_namesInitialized = true;
        
        if (s_allNames.Count == 0)
        {
            Debug.LogError("No map type names were generated!");
        }
        else
        {
}
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Draw default inspector except sprite entries
        DrawPropertiesExcluding(serializedObject, "mapTypeSpriteEntries");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Map Type Sprite Entries", EditorStyles.boldLabel);

        if (GUILayout.Button("Refresh Map Types"))
        {
            // Only rebuild when explicitly requested by user (e.g., if they edited MapTypeNameGenerator)
            RefreshMapTypeNames();
        }

        if (GUILayout.Button("Add Entry"))
        {
            spriteEntriesProp.arraySize++;
        }
        
        // Safety check - ensure we have cached data (handles domain reload edge cases)
        if (s_cachedNamesArray == null || s_cachedNamesArray.Length == 0)
        {
            RefreshMapTypeNames();
        }

        for (int i = 0; i < spriteEntriesProp.arraySize; i++)
        {
            SerializedProperty element = spriteEntriesProp.GetArrayElementAtIndex(i);
            SerializedProperty nameProp = element.FindPropertyRelative("mapTypeName");
            SerializedProperty spriteProp = element.FindPropertyRelative("sprite");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            
            // Find current index in cached list
            int currentIndex = 0;
            if (!string.IsNullOrEmpty(nameProp.stringValue) && s_allNames != null)
            {
                int foundIndex = s_allNames.IndexOf(nameProp.stringValue);
                if (foundIndex >= 0) currentIndex = foundIndex;
            }
            
            // Use static cached array - zero allocations per frame!
            int newIndex = EditorGUILayout.Popup("Map Type", currentIndex, s_cachedNamesArray);
            if (newIndex >= 0 && newIndex < s_cachedNamesArray.Length)
            {
                nameProp.stringValue = s_cachedNamesArray[newIndex];
            }

            if (GUILayout.Button("X", GUILayout.Width(20)))
            {
                spriteEntriesProp.DeleteArrayElementAtIndex(i);
                break;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.PropertyField(spriteProp);
            EditorGUILayout.EndVertical();
        }

        serializedObject.ApplyModifiedProperties();
    }
} 