using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class SpaceMapUIIntegrationTests
{
    private const string Root = "Assets/Scripts Repo/";
    private const string Art = Root + "UI/Space UI/";

    private static Sprite LoadSprite(string fileName)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(Art + fileName);
        Assert.That(sprite, Is.Not.Null, fileName + " must import as a sprite");
        return sprite;
    }

    private static void AssertAssigned(SerializedObject target, params string[] fields)
    {
        foreach (var field in fields)
        {
            var property = target.FindProperty(field);
            Assert.That(property, Is.Not.Null, field);
            Assert.That(property.objectReferenceValue, Is.Not.Null, field);
        }
    }

    [Test]
    public void SpaceMapPrefabWiresMarkersAndOverlays()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "UI/Space Map UI.prefab");
        Assert.That(prefab, Is.Not.Null);

        var controller = new SerializedObject(prefab.GetComponentInChildren<SpaceMapWorldController>(true));
        Assert.That(controller.FindProperty("shipMarkerSprite").objectReferenceValue, Is.EqualTo(LoadSprite("Ship Marker Icon.png")));
        Assert.That(controller.FindProperty("fleetMarkerSprite").objectReferenceValue, Is.EqualTo(LoadSprite("Fleet Marker Icon.png")));
        Assert.That(controller.FindProperty("infrastructureMarkerSprite").objectReferenceValue, Is.EqualTo(LoadSprite("Infrastructure Marker Icon.png")));

        var view = prefab.GetComponentInChildren<SpaceMapUI>(true);
        var ui = new SerializedObject(view);
        AssertAssigned(ui, "navigationBar", "navigationEntityText", "navigationRouteText", "navigationDestinationText",
            "navigationConfirmButton", "navigationCancelButton", "legendToggleButton", "legendPanel", "legendCollapseButton",
            "legendSelectionSwatch", "legendReachableSwatch", "legendBlockedSwatch", "legendRouteSwatch", "legendForeignSwatch",
            "missionBarPrefab", "missionBarAnchor", "combatPreviewPanel");

        // Temporary overlays start hidden: no route is pending, the legend is collapsed, and no attack is being previewed.
        var navigationBar = (GameObject)ui.FindProperty("navigationBar").objectReferenceValue;
        var legend = (GameObject)ui.FindProperty("legendPanel").objectReferenceValue;
        var combat = (SpaceCombatPreviewPanel)ui.FindProperty("combatPreviewPanel").objectReferenceValue;
        Assert.That(navigationBar.activeSelf, Is.False);
        Assert.That(legend.activeSelf, Is.False);
        Assert.That(combat.gameObject.activeSelf, Is.False);
        Assert.That(combat.IsConfigured, Is.True);

        Assert.That(navigationBar.transform.Find("Art").GetComponent<Image>().sprite, Is.EqualTo(LoadSprite("Route Confirmation Row.png")));
        Assert.That(legend.transform.Find("Art").GetComponent<Image>().sprite, Is.EqualTo(LoadSprite("Space Map Legend.png")));
        Assert.That(combat.transform.Find("Window/Art").GetComponent<Image>().sprite, Is.EqualTo(LoadSprite("Combat Preview Shell.png")));

        // Artwork is decorative: only the controls take clicks.
        foreach (var name in new[] { "Art", "Selected Entity", "Route Information", "Destination" })
            Assert.That(navigationBar.transform.Find(name).GetComponent<Graphic>().raycastTarget, Is.False, name);
    }

    [Test]
    public void MissionBarPrefabOffersTheThreeExistingMissions()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Art + "Space Mission Bar.prefab");
        Assert.That(prefab, Is.Not.Null);
        var bar = prefab.GetComponent<SpaceMissionBarView>();
        Assert.That(bar, Is.Not.Null);
        Assert.That(prefab.transform.Find("Art").GetComponent<Image>().sprite, Is.EqualTo(LoadSprite("Mission Bar.png")));

        var slots = new SerializedObject(bar).FindProperty("slots");
        Assert.That(slots.arraySize, Is.EqualTo(3));
        var expected = new[] { SpaceMissionKind.SpaceStrike, SpaceMissionKind.CityBombardment, SpaceMissionKind.Recon };
        for (int i = 0; i < slots.arraySize; i++)
        {
            var slot = slots.GetArrayElementAtIndex(i);
            Assert.That(slot.FindPropertyRelative("kind").intValue, Is.EqualTo((int)expected[i]));
            Assert.That(slot.FindPropertyRelative("button").objectReferenceValue, Is.Not.Null);
            Assert.That(slot.FindPropertyRelative("label").objectReferenceValue, Is.Not.Null);
            Assert.That(slot.FindPropertyRelative("detail").objectReferenceValue, Is.Not.Null);
        }
    }

    [Test]
    public void EmbarkPrefabKeepsItsControllerAndRealDropdown()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "Managers/Space Embark Canvas.prefab");
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.GetComponent<Canvas>().renderMode, Is.EqualTo(RenderMode.ScreenSpaceOverlay));

        var embark = prefab.GetComponentInChildren<SpaceEmbarkUI>(true);
        Assert.That(embark, Is.Not.Null);
        Assert.That(embark.destinationDropdown, Is.Not.Null);
        Assert.That(embark.destinationDropdown.template, Is.Not.Null);
        Assert.That(embark.destinationDropdown.captionText, Is.Not.Null);
        Assert.That(embark.embarkButton, Is.Not.Null);
        Assert.That(embark.cancelButton, Is.Not.Null);
        Assert.That(embark.selectedUnitText, Is.Not.Null);
        Assert.That(embark.travelInfoText, Is.Not.Null);
        Assert.That(embark.embarkPanel, Is.Not.Null);
        AssertAssigned(new SerializedObject(embark), "unitDetailText");

        Assert.That(embark.embarkPanel.transform.Find("Art").GetComponent<Image>().sprite, Is.EqualTo(LoadSprite("Embarkation Panel Shell.png")));
        Assert.That(embark.embarkButton.GetComponentInChildren<TMP_Text>(true).text, Is.EqualTo("Confirm Travel"));
    }
}
