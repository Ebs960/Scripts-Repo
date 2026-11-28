using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class MissileUIIntegrationTests
{
    private const string PrefabPath = "Assets/Scripts Repo/Managers/Missile Manager.prefab";
    private const string ArtworkPath = "Assets/Scripts Repo/UI/Missile Management UI/";

    [Test]
    public void ManagerPrefabConnectsControllerAndArtwork()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<MissileLaunchMode>(), Is.Not.Null);
        var panel = prefab.GetComponentInChildren<MissilePanelUI>(true);
        Assert.That(panel, Is.Not.Null);
        var data = new SerializedObject(panel);
        foreach (var binding in new[]
        {
            ("rowArtwork", "Missile Inventory Row.png"),
            ("nuclearBadgeArtwork", "Nuclear Warning Badge Icon.png"),
            ("targetingBarArtwork", "Missile Targeting Bar.png")
        })
            Assert.That(data.FindProperty(binding.Item1).objectReferenceValue,
                Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(ArtworkPath + binding.Item2)));
        var root = (GameObject)data.FindProperty("panelRoot").objectReferenceValue;
        Assert.That(root.GetComponent<Image>().sprite,
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(ArtworkPath + "Missile Management Panel Shell.png")));
    }

    [Test]
    public void RowsShowActualMissilesAndSelectionMovesBetweenDuplicates()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        var missile = ScriptableObject.CreateInstance<MissileData>();
        missile.missileName = "Test missile";
        missile.range = 7;
        missile.isNuclear = true;
        try
        {
            var panel = instance.GetComponentInChildren<MissilePanelUI>(true);
            Invoke(panel, "ConfigureLayout");
            typeof(MissilePanelUI).GetField("_missiles", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(panel, new List<MissileData> { missile, missile });
            Invoke(panel, "PopulateList");
            var data = new SerializedObject(panel);
            var container = (Transform)data.FindProperty("missileListContainer").objectReferenceValue;
            Assert.That(container.childCount, Is.EqualTo(2));
            var first = container.GetChild(0);
            var second = container.GetChild(1);
            Assert.That(first.Find("Name").GetComponent<TextMeshProUGUI>().text, Is.EqualTo("Test missile"));
            Assert.That(first.Find("NuclearBadge").gameObject.activeSelf, Is.True);
            first.GetComponent<Button>().onClick.Invoke();
            Assert.That(first.Find("Selection").gameObject.activeSelf, Is.True);
            second.GetComponent<Button>().onClick.Invoke();
            Assert.That(first.Find("Selection").gameObject.activeSelf, Is.False);
            Assert.That(second.Find("Selection").gameObject.activeSelf, Is.True);
            Assert.That(((Button)data.FindProperty("launchButton").objectReferenceValue).interactable, Is.True);
        }
        finally
        {
            Object.DestroyImmediate(instance);
            Object.DestroyImmediate(missile);
        }
    }

    private static void Invoke(object target, string method)
    {
        typeof(MissilePanelUI).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);
    }
}
