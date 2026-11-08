using UnityEngine;
using TMPro;

[CreateAssetMenu(fileName = "Menu UI Skin", menuName = "UI/Menu UI Skin")]
public sealed class MenuUiSkin : ScriptableObject
{
    public TMP_FontAsset headingFont;
    public TMP_FontAsset bodyFont;
    public Sprite pauseMenuShell;
    public Sprite saveLoadShell;
    public Sprite saveSlotRow;
    public Sprite selectedSaveSlotRow;
    public Sprite genericButton;
    public Sprite confirmationDialogShell;
    public Sprite confirmButton;
    public Sprite cancelButton;
}

public static class MenuUiTypography
{
    private static TMP_FontAsset headingFont;
    private static TMP_FontAsset bodyFont;
    private static bool searched;

    public static TMP_FontAsset Heading(MenuUiSkin skin = null)
    {
        SearchFonts();
        return skin != null && skin.headingFont != null
            ? skin.headingFont
            : headingFont != null ? headingFont : TMP_Settings.defaultFontAsset;
    }

    public static TMP_FontAsset Body(MenuUiSkin skin = null)
    {
        SearchFonts();
        return skin != null && skin.bodyFont != null
            ? skin.bodyFont
            : bodyFont != null ? bodyFont : TMP_Settings.defaultFontAsset;
    }

    private static void SearchFonts()
    {
        if (searched) return;
        searched = true;
        foreach (TMP_FontAsset font in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
        {
            if (font == null) continue;
            if (headingFont == null && font.name.IndexOf("Cinzel", System.StringComparison.OrdinalIgnoreCase) >= 0)
                headingFont = font;
            if (bodyFont == null && font.name.IndexOf("Source Sans", System.StringComparison.OrdinalIgnoreCase) >= 0)
                bodyFont = font;
        }
    }
}
