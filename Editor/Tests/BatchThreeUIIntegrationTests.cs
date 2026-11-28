using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class BatchThreeUIIntegrationTests
{
    private const string UiRoot = "Assets/Scripts Repo/UI/";

    [Test]
    public void VictoryPrefabUsesNewSpritesAndRetainsProgressBindings()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UiRoot + "Player UI.prefab");
        Assert.That(prefab, Is.Not.Null);
        var panel = prefab.GetComponentInChildren<VictoryProgressPanel>(true);
        Assert.That(panel, Is.Not.Null);
        var serialized = new SerializedObject(panel);
        Assert.That(serialized.FindProperty("panel").objectReferenceValue, Is.Not.Null);
        Assert.That(serialized.FindProperty("openButton").objectReferenceValue, Is.Not.Null);
        Assert.That(serialized.FindProperty("closeButton").objectReferenceValue, Is.Not.Null);
        var rows = serialized.FindProperty("rows");
        Assert.That(rows.arraySize, Is.EqualTo(3));
        for (int i = 0; i < rows.arraySize; i++)
        {
            var row = rows.GetArrayElementAtIndex(i);
            Assert.That(row.FindPropertyRelative("type").enumValueIndex, Is.EqualTo(i));
            Assert.That(row.FindPropertyRelative("value").objectReferenceValue, Is.Not.Null);
            Assert.That(row.FindPropertyRelative("fill").objectReferenceValue, Is.Not.Null);
        }
        Assert.That(panel.transform.Find("Victory Progress Panel").GetComponent<Image>().sprite,
            Is.EqualTo(AssetDatabase.LoadAssetAtPath<Sprite>(UiRoot + "Victory Condition Tracker Panel.png")));
        foreach (var name in new[] { "Domination Victory Icon.png", "Scoemce Victory Icon.png", "Culture Victory Icon.png" })
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UiRoot + name);
            Assert.That(sprite, Is.Not.Null);
            Assert.That(panel.GetComponentsInChildren<Image>(true), Has.Some.Matches<Image>(image => image.sprite == sprite));
        }
    }

    [Test]
    public void ElectionRuntimeViewBindsEveryMandatoryControl()
    {
        var assets = Resources.Load<BatchThreeUIAssets>("MenuUI/Batch Three UI Assets");
        Assert.That(assets, Is.Not.Null);
        Assert.That(assets.electionShell, Is.Not.Null);
        Assert.That(assets.concedeIcon, Is.Not.Null);
        Assert.That(assets.seizeIcon, Is.Not.Null);
        var root = new GameObject("Election UI Test");
        try
        {
            var view = ElectionDefeatModalUI.CreateRuntime(root.transform, assets);
            var serialized = new SerializedObject(view);
            foreach (var field in new[] { "modalRoot", "titleText", "explanationText", "publicOpinionText",
                "concedeConsequenceText", "seizeConsequenceText", "statusText", "concedeButton", "seizeButton" })
                Assert.That(serialized.FindProperty(field).objectReferenceValue, Is.Not.Null, field);
            Assert.That(((GameObject)serialized.FindProperty("modalRoot").objectReferenceValue).activeSelf, Is.False);
            Assert.That(view.GetComponentsInChildren<Image>(true), Has.Some.Matches<Image>(image => image.sprite == assets.electionShell));
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [Test]
    public void NotificationPrefabHasAnIndependentIcon()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(UiRoot + "Notification Panel.prefab");
        var icon = prefab != null ? prefab.transform.Find("Notification Card/Notification Icon") : null;
        Assert.That(icon, Is.Not.Null);
        Assert.That(icon.GetComponent<Image>().preserveAspect, Is.True);
        Assert.That(icon.GetComponent<Image>().raycastTarget, Is.False);
    }
}
