using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(MainMenuManager))]
public class MainMenuManagerEditor : Editor
{
    SerializedProperty spriteEntriesProp;
    List<string> allNames;

    void OnEnable()
    {
        spriteEntriesProp = serializedObject.FindProperty("mapTypeSpriteEntries");
        RefreshMapTypeNames();
    }

    private void RefreshMapTypeNames()
    {
        allNames = MapTypeNameGenerator.BuildAllNames();
        if (allNames.Count == 0)
        {
            Debug.LogError("No map type names were generated!");
        }
        else
        {
            Debug.Log($"Generated {allNames.Count} map type names");
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
            RefreshMapTypeNames();
        }

        if (GUILayout.Button("Add Entry"))
        {
            spriteEntriesProp.arraySize++;
        }

        for (int i = 0; i < spriteEntriesProp.arraySize; i++)
        {
            SerializedProperty element = spriteEntriesProp.GetArrayElementAtIndex(i);
            SerializedProperty nameProp = element.FindPropertyRelative("mapTypeName");
            SerializedProperty spriteProp = element.FindPropertyRelative("sprite");

            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.BeginHorizontal();
            
            // Ensure we have a valid index
            int currentIndex = string.IsNullOrEmpty(nameProp.stringValue) ? 0 : 
                             allNames.Contains(nameProp.stringValue) ? allNames.IndexOf(nameProp.stringValue) : 0;
            
            int newIndex = EditorGUILayout.Popup("Map Type", currentIndex, allNames.ToArray());
            if (newIndex >= 0 && newIndex < allNames.Count)
            {
                nameProp.stringValue = allNames[newIndex];
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