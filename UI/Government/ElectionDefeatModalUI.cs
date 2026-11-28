using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Mandatory decision shown after the player loses a national election.</summary>
public sealed class ElectionDefeatModalUI : MonoBehaviour
{
    [SerializeField] private GameObject modalRoot;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text explanationText;
    [SerializeField] private TMP_Text publicOpinionText;
    [SerializeField] private TMP_Text concedeConsequenceText;
    [SerializeField] private TMP_Text seizeConsequenceText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button concedeButton;
    [SerializeField] private Button seizeButton;

    private Civilization civilization;
    private bool resolving;
    public bool IsVisible => modalRoot != null && modalRoot.activeInHierarchy;

    public static ElectionDefeatModalUI CreateRuntime(Transform parent, BatchThreeUIAssets assets)
    {
        var host = new GameObject("Election Defeat UI");
        host.SetActive(false);
        host.transform.SetParent(parent, false);
        var view = host.AddComponent<ElectionDefeatModalUI>();
        view.BuildRuntimeView(assets);
        host.SetActive(true);
        return view;
    }

    private void BuildRuntimeView(BatchThreeUIAssets assets)
    {
        var canvasObject = new GameObject("Election Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = 32000;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        var backdrop = CreateImage("Backdrop", canvasObject.transform, null, new Color(0, 0, 0, 0.65f));
        Stretch(backdrop.rectTransform, Vector2.zero, Vector2.one);
        modalRoot = backdrop.gameObject;
        var shell = CreateImage("Election Defeat Panel", backdrop.transform, assets != null ? assets.electionShell : null, Color.white);
        SetRect(shell.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 520));
        titleText = CreateText("Title", shell.transform, 28, TextAlignmentOptions.Center);
        SetRect(titleText.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -38), new Vector2(440, 42));
        explanationText = CreateText("Explanation", shell.transform, 19, TextAlignmentOptions.Center);
        SetRect(explanationText.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -108), new Vector2(690, 54));
        publicOpinionText = CreateText("Public Opinion", shell.transform, 18, TextAlignmentOptions.Center);
        SetRect(publicOpinionText.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -155), new Vector2(650, 32));
        BuildDecision(shell.transform, -180, "Concede Power", assets != null ? assets.concedeIcon : null,
            out concedeConsequenceText, out concedeButton);
        BuildDecision(shell.transform, 180, "Seize Power", assets != null ? assets.seizeIcon : null,
            out seizeConsequenceText, out seizeButton);
        statusText = CreateText("Status", shell.transform, 17, TextAlignmentOptions.Center);
        SetRect(statusText.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 42), new Vector2(680, 35));
        modalRoot.SetActive(false);
    }

    private static void BuildDecision(Transform parent, float x, string label, Sprite icon,
        out TMP_Text consequence, out Button button)
    {
        var iconImage = CreateImage(label + " Icon", parent, icon, Color.white);
        iconImage.raycastTarget = false;
        iconImage.preserveAspect = true;
        SetRect(iconImage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x, 12), new Vector2(88, 88));
        var heading = CreateText(label + " Heading", parent, 22, TextAlignmentOptions.Center);
        heading.text = label;
        SetRect(heading.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x, 82), new Vector2(300, 30));
        consequence = CreateText(label + " Consequence", parent, 17, TextAlignmentOptions.Center);
        SetRect(consequence.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x, -67), new Vector2(300, 65));
        var buttonImage = CreateImage(label + " Button", parent, null, new Color(1, 1, 1, 0.01f));
        SetRect(buttonImage.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(x, -148), new Vector2(250, 42));
        button = buttonImage.gameObject.AddComponent<Button>();
        button.targetGraphic = buttonImage;
        var buttonText = CreateText("Label", buttonImage.transform, 19, TextAlignmentOptions.Center);
        buttonText.text = label;
        Stretch(buttonText.rectTransform, Vector2.zero, Vector2.one);
    }

    private static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        return image;
    }

    private static TMP_Text CreateText(string name, Transform parent, float size, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.fontSize = size;
        text.enableAutoSizing = true;
        text.fontSizeMin = size * 0.7f;
        text.fontSizeMax = size;
        text.alignment = alignment;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static void SetRect(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private void Awake()
    {
        GovernmentUiUtil.SetClick(concedeButton, Concede);
        GovernmentUiUtil.SetClick(seizeButton, SeizePower);
        GovernmentUiUtil.SetActive(modalRoot, false);
    }

    private void OnEnable()
    {
        ElectionManager.PlayerElectionDefeatPending += Show;
        TryShowPending();
    }

    private void OnDisable()
    {
        ElectionManager.PlayerElectionDefeatPending -= Show;
    }

    private void Update()
    {
        if (modalRoot != null && !modalRoot.activeSelf) TryShowPending();
    }

    private void TryShowPending()
    {
        var player = PoliticalActionRules.FindPlayerCivilization();
        if (player?.electionState?.pendingPlayerDefeat == true) Show(player);
    }

    private void Show(Civilization civ)
    {
        if (civ?.electionState?.pendingPlayerDefeat != true) return;
        if (UIManager.Instance != null && !UIManager.Instance.CanPresentElectionDecision) return;
        if (GameManager.Instance != null && GameManager.Instance.IsMatchConcluded) return;
        civilization = civ;
        resolving = false;
        var election = civ.electionState.history?.LastOrDefault(record => record != null
            && record.electionId == civ.electionState.pendingDefeatElectionId);
        float opinion = election != null ? election.publicOpinionAtElection : civ.electionState.publicApproval;
        GovernmentUiUtil.SetText(titleText, "Election Defeat");
        GovernmentUiUtil.SetText(explanationText, "The electorate has rejected your administration. You must concede power or overturn the result.");
        GovernmentUiUtil.SetText(publicOpinionText, $"Public Opinion: {opinion:0.##}% ({ElectionManager.PlayerVictoryThreshold:0.##}% required)");
        GovernmentUiUtil.SetText(concedeConsequenceText, "Concede Power: accept the result and end the campaign in defeat.");
        GovernmentUiUtil.SetText(seizeConsequenceText, "Seize Power: impose authoritarian rule and face a nationwide rebellion.");
        GovernmentUiUtil.SetText(statusText, string.Empty);
        GovernmentUiUtil.SetInteractable(concedeButton, true);
        GovernmentUiUtil.SetInteractable(seizeButton, true);
        GovernmentUiUtil.SetActive(modalRoot, true);
        if (GameManager.Instance != null) GameManager.Instance.gamePaused = true;
    }

    private void Concede()
    {
        if (resolving || civilization == null) return;
        resolving = true;
        GovernmentUiUtil.SetInteractable(concedeButton, false);
        GovernmentUiUtil.SetInteractable(seizeButton, false);
        ElectionManager.ConcedePlayerDefeat(civilization);
        GovernmentUiUtil.SetActive(modalRoot, false);
    }

    private void SeizePower()
    {
        if (resolving || civilization == null) return;
        resolving = true;
        GovernmentUiUtil.SetInteractable(concedeButton, false);
        GovernmentUiUtil.SetInteractable(seizeButton, false);
        if (ElectionManager.SeizePower(civilization))
        {
            GovernmentUiUtil.SetActive(modalRoot, false);
            return;
        }

        resolving = false;
        GovernmentUiUtil.SetText(statusText, "No valid emergency authoritarian government is configured.");
        GovernmentUiUtil.SetInteractable(concedeButton, true);
        GovernmentUiUtil.SetInteractable(seizeButton, true);
    }
}