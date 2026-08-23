#if UNITY_EDITOR
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class AlphaFinalizationTests
{
    [Test]
    public void CanonicalCityUi_IsTheRebuildAndHasTabs()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AlphaFinalizationValidator.CanonicalCityPrefabPath);
        Assert.That(prefab, Is.Not.Null);
        Assert.That(prefab.name, Is.EqualTo("City UI Rebuild"));
        Assert.That(prefab.GetComponent<CityUI>(), Is.Not.Null);
        Assert.That(prefab.GetComponent<CityUITabController>(), Is.Not.Null);
    }

    [Test]
    public void UiManager_ReferencesCanonicalCityPrefab()
    {
        var manager = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/UI/UI Manager.prefab");
        var serialized = new SerializedObject(manager.GetComponent<UIManager>());
        var reference = serialized.FindProperty("cityPanelPrefab").objectReferenceValue;
        Assert.That(AssetDatabase.GetAssetPath(reference), Is.EqualTo(AlphaFinalizationValidator.CanonicalCityPrefabPath));
    }

    [Test]
    public void AlphaValidator_ReportsNoCompetingCityUi()
    {
        var competing = AlphaFinalizationValidator.ValidateAll()
            .Where(f => f.message.StartsWith("Competing CityUI"));
        Assert.That(competing, Is.Empty);
    }
}
#endif
