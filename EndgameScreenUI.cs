using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Full-screen match result overlay. Shows the player's civilization Victory or Defeat illustration
/// with the outcome heading, victory-type subtitle, winning civilization and leader, and
/// Continue / Main Menu controls. Built at runtime from native UI so no prefab wiring is required.
/// </summary>
public sealed class EndgameScreenUI : MonoBehaviour
{
    private const int SortingOrder = 1200;
    private static readonly Color Navy = new Color(0.025f, 0.045f, 0.075f, 1f);
    private static readonly Color BandColor = new Color(0.02f, 0.035f, 0.06f, 0.88f);
    private static readonly Color Gold = new Color(0.72f, 0.52f, 0.21f, 1f);
    private static readonly Color TextColor = new Color(0.93f, 0.91f, 0.86f, 1f);
    private static readonly Color MutedText = new Color(0.72f, 0.76f, 0.79f, 1f);
    private static readonly Color DefeatRed = new Color(0.62f, 0.26f, 0.22f, 1f);

    private static EndgameScreenUI current;
    private static MenuUiSkin skin;

    private VictoryResult result;
    private Button continueButton;

    public static bool IsVisible => current != null;

    /// <summary>Optional shared skin for fonts/button sprites; set once by whichever menu owns one.</summary>
    public static void SetSkin(MenuUiSkin uiSkin) => skin = uiSkin;

    public static void Show(VictoryResult matchResult)
    {
        if (matchResult == null) return;
        CloseCurrent();
        var host = new GameObject("Endgame Screen");
        current = host.AddComponent<EndgameScreenUI>();
        current.result = matchResult;
        current.Build();
    }

    public static void CloseCurrent()
    {
        if (current == null) return;
        var closing = current;
        current = null;
        if (closing != null)
            Destroy(closing.gameObject);
    }

    private void OnDestroy()
    {
        if (current == this)
            current = null;
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = SortingOrder;
        gameObject.AddComponent<GraphicRaycaster>();
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var backdrop = Rect("Backdrop", transform, Vector2.zero, Vector2.one);
        var backdropImage = backdrop.AddComponent<Image>();
        backdropImage.color = Navy;
        backdropImage.raycastTarget = true;

        // Illustration fills the screen while keeping its 16:9 master aspect.
        Sprite illustration = ResolveIllustration();
        var art = Rect("Illustration", backdrop.transform, Vector2.zero, Vector2.one);
        var artImage = art.AddComponent<Image>();
        artImage.raycastTarget = false;
        if (illustration != null)
        {
            artImage.sprite = illustration;
            artImage.preserveAspect = true;
            var fitter = art.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = illustration.rect.height > 0f ? illustration.rect.width / illustration.rect.height : 16f / 9f;
        }
        else
        {
            artImage.color = Navy;
        }

        // Lower information band keeps text legible without a heavy panel shell.
        var band = Rect("Result Band", backdrop.transform, new Vector2(0f, 0f), new Vector2(1f, 0.3f));
        var bandImage = band.AddComponent<Image>();
        bandImage.color = BandColor;
        bandImage.raycastTarget = false;
        var rule = Rect("Gold Rule", band.transform, new Vector2(0.08f, 0.985f), new Vector2(0.92f, 1f));
        var ruleImage = rule.AddComponent<Image>();
        ruleImage.color = Gold;
        ruleImage.raycastTarget = false;

        bool victory = result.PlayerWon;
        Text("Heading", band.transform, victory ? "VICTORY" : "DEFEAT", 64, victory ? Gold : DefeatRed,
            new Vector2(0.08f, 0.56f), new Vector2(0.62f, 0.95f), FontStyles.Bold, TextAlignmentOptions.BottomLeft, heading: true);
        Text("Subtitle", band.transform, BuildSubtitle(), 30, TextColor,
            new Vector2(0.08f, 0.33f), new Vector2(0.62f, 0.56f), FontStyles.Normal, TextAlignmentOptions.TopLeft);
        Text("Details", band.transform, BuildDetails(), 22, MutedText,
            new Vector2(0.08f, 0.06f), new Vector2(0.62f, 0.33f), FontStyles.Normal, TextAlignmentOptions.TopLeft);

        var buttons = Rect("Buttons", band.transform, new Vector2(0.66f, 0.28f), new Vector2(0.92f, 0.78f));
        var layout = buttons.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 12;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        continueButton = AddButton(buttons.transform, "Continue", Gold, () => CloseCurrent());
        AddButton(buttons.transform, "Main Menu", new Color(0.12f, 0.18f, 0.23f, 1f), () =>
        {
            var gm = GameManager.Instance;
            if (gm != null) gm.EndGame();
            else UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
        });

        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(continueButton.gameObject);
    }

    private Sprite ResolveIllustration()
    {
        var player = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
        var playerData = player != null ? player.civData : null;
        if (playerData != null)
            return result.PlayerWon ? playerData.victoryIllustration : playerData.defeatIllustration;

        // No player civilization (observer or restored state): fall back to the winner's artwork.
        var winnerData = result.Winner != null ? result.Winner.civData : null;
        return winnerData != null ? winnerData.victoryIllustration : null;
    }

    private string BuildSubtitle()
    {
        if (!result.Type.HasValue)
            return string.IsNullOrWhiteSpace(result.Reason) ? "Match Concluded" : result.Reason;
        return result.TypeDisplayName;
    }

    private string BuildDetails()
    {
        string civ = result.WinnerCivName;
        if (string.IsNullOrEmpty(civ))
        {
            var player = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
            civ = player != null && player.civData != null ? player.civData.civName : null;
            string playerLeader = player != null && player.leader != null ? player.leader.leaderName : null;
            return Join(civ, playerLeader, result.Turn > 0 ? $"Turn {result.Turn}" : null);
        }

        string prefix = result.PlayerWon ? null : "Winner: ";
        return Join(prefix + civ, result.WinnerLeaderName, result.Turn > 0 ? $"Turn {result.Turn}" : null);
    }

    private static string Join(params string[] parts)
    {
        var sb = new System.Text.StringBuilder();
        foreach (string part in parts)
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            if (sb.Length > 0) sb.Append("   |   ");
            sb.Append(part);
        }
        return sb.ToString();
    }

    private Button AddButton(Transform parent, string label, Color color, UnityEngine.Events.UnityAction action)
    {
        var go = Rect(label, parent, Vector2.zero, Vector2.one);
        go.AddComponent<LayoutElement>().preferredHeight = 64;
        var image = go.AddComponent<Image>();
        image.color = color;
        if (skin != null && skin.genericButton != null)
        {
            image.sprite = skin.genericButton;
            image.type = Image.Type.Sliced;
        }
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        button.onClick.AddListener(action);
        var text = Text("Label", go.transform, label, 24, TextColor, Vector2.zero, Vector2.one, FontStyles.Bold,
            TextAlignmentOptions.Center);
        text.raycastTarget = false;
        return button;
    }

    private static GameObject Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return go;
    }

    private static TextMeshProUGUI Text(string name, Transform parent, string value, float size, Color color,
        Vector2 anchorMin, Vector2 anchorMax, FontStyles style, TextAlignmentOptions alignment, bool heading = false)
    {
        var go = Rect(name, parent, anchorMin, anchorMax);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = heading ? MenuUiTypography.Heading(skin) : MenuUiTypography.Body(skin);
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.alignment = alignment;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        return text;
    }
}
