using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class ManagementUIAssetsTests
{
    [Test]
    public void RuntimeAssetsReferenceImportedSpritesAndRosterCard()
    {
        var assets = Resources.Load<ManagementUIAssets>("MenuUI/Management UI Assets");
        Assert.That(assets, Is.Not.Null);
        foreach (var sprite in new[]
        {
            assets.legacyTrackerShell, assets.legacyEntryRow,
            assets.legacyDetailShell, assets.armyTransferShell
        })
        {
            Assert.That(sprite, Is.Not.Null);
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite)) as TextureImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.textureType, Is.EqualTo(TextureImporterType.Sprite));
            Assert.That(importer.spriteImportMode, Is.EqualTo(SpriteImportMode.Single));
            Assert.That(importer.alphaIsTransparency, Is.True);
            Assert.That(importer.mipmapEnabled, Is.False);
        }
        Assert.That(assets.armyRosterCard, Is.Not.Null);
        Assert.That(assets.armyRosterCard.GetComponent<UnityEngine.UI.Button>(), Is.Not.Null);
    }
}
