using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

public sealed class CityDrawerTests
{
    [Test]
    public void CampaignCityPanelReferencePointsAtDrawer()
    {
        var scene = EditorSceneManager.OpenPreviewScene("Assets/Scenes/Game.unity");
        try
        {
            var manager = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<UIManager>(true)).Single();
            var panel = (GameObject)new SerializedObject(manager).FindProperty("cityPanel").objectReferenceValue;
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.activeSelf, Is.False);
            Assert.That(panel.GetComponentInChildren<CityUIScrollNavigator>(true), Is.Not.Null);
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [Test]
    public void CampaignPrefabHasFiveReachableSectionsInOneSheet()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            "Assets/Scripts Repo/UI/City UI Rebuild.prefab");
        Assert.That(prefab, Is.Not.Null);
        var drawer = prefab.transform.Find("CityPanelRoot");
        Assert.That(drawer, Is.Not.Null);
        var scroll = drawer.Find("City Scroll View").GetComponent<ScrollRect>();
        Assert.That(scroll.vertical && !scroll.horizontal, Is.True);
        Assert.That(scroll.viewport.GetComponent<RectMask2D>(), Is.Not.Null);
        Assert.That(scroll.content.Cast<Transform>().Select(child => child.name), Is.EqualTo(new[]
        {
            "CitizensAndTilesSection", "ProductionSection", "BuildingsAndSpecialistsSection",
            "CrimeAndDiseaseSection", "UnitStorageSection"
        }));
        Assert.That(scroll.content.Cast<Transform>().All(child => child.gameObject.activeSelf), Is.True);
        Assert.That(scroll.content.GetComponentsInChildren<ScrollRect>(false), Is.Empty);
        Assert.That(drawer.Find("VerticalNavigation").GetComponentsInChildren<Button>().Length, Is.EqualTo(5));
        Assert.That(drawer.Find("City Panel Shell").GetComponent<Image>().sprite, Is.Not.Null);

        var city = new SerializedObject(prefab.GetComponent<CityUI>());
        Assert.That(city.FindProperty("scrollNavigator").objectReferenceValue, Is.EqualTo(drawer.GetComponent<CityUIScrollNavigator>()));
        foreach (var field in new[] { "buildingsContainer", "unitsContainer", "equipmentContainer", "missilesContainer" })
        {
            var productionList = (Transform)city.FindProperty(field).objectReferenceValue;
            Assert.That(productionList.parent.name, Is.EqualTo("ProductionSection"));
            Assert.That(productionList.GetComponent<VerticalLayoutGroup>().childControlHeight, Is.True);
        }
    }
}
