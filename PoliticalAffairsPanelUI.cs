using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PoliticalAffairsPanelUI : MonoBehaviour
{
    [Header("Panel Root")]
    [SerializeField] private GameObject panelRoot;

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI subtitleText;
    [SerializeField] private Button closeButton;

    [Header("Content")]
    [SerializeField] private RectTransform contentRoot;
    [SerializeField] private Transform governorContainer;
    [SerializeField] private Transform councilContainer;
    [SerializeField] private Transform vassalContainer;
    [SerializeField] private GameObject entryPrefab;
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private GameObject optionButtonPrefab;

    private TMP_FontAsset inheritedEntryFont;
    private FontStyles inheritedEntryFontStyle = FontStyles.Normal;
    private Color inheritedEntryColor = Color.white;

    public void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Hide);

        if (panelRoot != null)
            panelRoot.SetActive(false);

        CacheOptionButtonTextStyle();
    }

    public void Show(Civilization civ)
    {
        if (civ == null) return;
        if (panelRoot != null) panelRoot.SetActive(true);

        if (titleText != null)
            titleText.text = "Political Affairs";
        if (subtitleText != null)
            subtitleText.text = $"Governors, lords, vassals, and ongoing events for {civ.civData?.civName ?? civ.name}.";

        ClearEntries();

        BuildGovernorSection(civ);
        BuildCouncilSection(civ);
        BuildVassalSection(civ);
        BuildCurrentEventsSection(civ);
    }

    private void ClearEntries()
    {
        ClearContainer(contentRoot);
        ClearContainer(governorContainer);
        ClearContainer(councilContainer);
        ClearContainer(vassalContainer);
    }

    private void ClearContainer(Transform container)
    {
        if (container == null) return;
        foreach (Transform child in container)
            Destroy(child.gameObject);
    }

    public void Hide()
    {
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void BuildGovernorSection(Civilization civ)
    {
        var lines = new List<string>();
        var governors = civ.governors?.Where(g => g != null).ToList() ?? new List<Governor>();
        lines.Add($"Total Governors: {governors.Count}/{Mathf.Max(0, civ.governorCount)}");
        lines.Add($"Governors Unlocked: {(civ.governorsEnabled ? "Yes" : "No")}");

        if (governors.Count == 0)
        {
            lines.Add("No governors currently appointed.");
        }
        else
        {
            foreach (var governor in governors.OrderBy(g => g.Name))
            {
                int cityCount = governor.Cities?.Count ?? 0;
                int herdCount = governor.Herds?.Count ?? 0;
                string faction = governor.Faction != null ? governor.Faction.FactionName : "Unaffiliated";
                lines.Add($"• {governor.Name} | Opinion {Mathf.RoundToInt(governor.Opinion)} | Ambition {Mathf.RoundToInt(governor.AmbitionScore)} | Power {governor.PowerRank} | Cities {cityCount} | Herds {herdCount} | Faction {faction}");
            }
        }

        CreateEntry("Governors", string.Join("\n", lines), null);
    }

    private void BuildCouncilSection(Civilization civ)
    {
        var lines = new List<string>();
        int maxSeats = civ.MaxCouncilSeats;
        int occupied = civ.royalCouncil?.Count ?? 0;
        lines.Add($"Royal Council Seats: {occupied}/{Mathf.Max(0, maxSeats)}");

        if (occupied <= 0)
        {
            lines.Add("No lords currently seated on the council.");
        }
        else
        {
            foreach (var lord in civ.royalCouncil.Where(g => g != null))
                lines.Add($"• {lord.Name} ({lord.specialization}) | Opinion {Mathf.RoundToInt(lord.Opinion)}");
        }

        var eligible = civ.governors
            .Where(g => g != null && g.IsCouncilEligible && !civ.royalCouncil.Contains(g))
            .ToList();
        if (eligible != null && eligible.Count > 0)
        {
            lines.Add("Eligible Lords Not Seated:");
            foreach (var lord in eligible.Where(g => g != null))
                lines.Add($"  - {lord.Name} | Power {lord.PowerRank} | Grievances {lord.TotalGrievances()}");
        }

        CreateEntry("Lords & Royal Council", string.Join("\n", lines), null);
    }

    private void BuildVassalSection(Civilization civ)
    {
        var lines = new List<string>();
        var manager = SubjectManager.Instance;

        var overlordContract = manager?.GetOverlordContract(civ);
        if (overlordContract != null)
            lines.Add($"This realm is a vassal of {overlordContract.overlordCivName}. Liberty Desire: {overlordContract.libertyDesire:F2}");
        else
            lines.Add("This realm is not currently a vassal.");

        var subjects = manager?.GetSubjects(civ) ?? new List<VassalContract>();
        lines.Add($"Current Vassals: {subjects.Count}");
        if (subjects.Count == 0) lines.Add("No subject realms under your direct overlordship.");
        else
        {
            foreach (var contract in subjects)
            {
                if (contract == null) continue;
                lines.Add($"• {contract.subjectCivName} | Liberty {contract.libertyDesire:F2} | Opinion {Mathf.RoundToInt(contract.subjectOpinion)} | Tribute Gold {contract.goldTributePct:P0}");
            }
        }

        CreateEntry("Vassals & Overlord Affairs", string.Join("\n", lines), null);
    }

    private void BuildCurrentEventsSection(Civilization civ)
    {
        var events = PoliticalEventManager.Instance?.GetActiveEventsForCiv(civ);
        if (events == null || events.Count == 0)
        {
            CreateEntry("Current Events", "No active political events right now.", null);
            return;
        }

        foreach (var record in events.OrderBy(e => e.expiryTurn))
        {
            string optionSummary = string.Join(" | ", record.options.Select(o => o.label));
            CreateEntry(record.title, $"{record.body}\n\nOptions: {optionSummary}\nExpires: turn {record.expiryTurn}", record);
        }
    }

    private void CreateEntry(string header, string body, PoliticalEventRecord record)
    {
        var parent = GetSectionRoot(header) ?? contentRoot;
        GameObject panel;
        TextMeshProUGUI title = null;
        TextMeshProUGUI text = null;

        if (entryPrefab != null)
        {
            panel = Instantiate(entryPrefab, parent);
            panel.name = header;

            title = panel.transform.Find("EntryTitle")?.GetComponent<TextMeshProUGUI>();
            text = panel.transform.Find("EntryBody")?.GetComponent<TextMeshProUGUI>();

            if (title == null || text == null)
            {
                var tmps = panel.GetComponentsInChildren<TextMeshProUGUI>(true);
                if (title == null)
                    title = tmps.FirstOrDefault(t => t.name.IndexOf("title", StringComparison.OrdinalIgnoreCase) >= 0) ?? tmps.FirstOrDefault();
                if (text == null)
                    text = tmps.FirstOrDefault(t => t.name.IndexOf("body", StringComparison.OrdinalIgnoreCase) >= 0 && t != title) ?? tmps.FirstOrDefault(t => t != title);
            }

            if (title == null)
                title = CreateText(panel.transform, "EntryTitle", 22, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            if (text == null)
                text = CreateText(panel.transform, "EntryBody", 18, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        }
        else
        {
            panel = new GameObject(header, typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(parent, false);
            panel.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);
            panel.GetComponent<LayoutElement>().minHeight = 120f;

            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 12, 12);
            layout.spacing = 6;
            layout.childControlHeight = true;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;

            title = CreateText(panel.transform, "EntryTitle", 22, FontStyles.Bold, TextAlignmentOptions.TopLeft);
            text = CreateText(panel.transform, "EntryBody", 18, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        }

        if (title != null)
            title.text = header;
        if (text != null)
            text.text = body;

        if (record == null) return;

        Transform buttonRow = panel.transform.Find("Buttons");
        if (buttonRow == null)
        {
            var buttonRowObject = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            buttonRowObject.transform.SetParent(panel.transform, false);
            buttonRow = buttonRowObject.transform;
            var rowLayout = buttonRowObject.GetComponent<HorizontalLayoutGroup>();
            rowLayout.spacing = 8;
            rowLayout.childControlWidth = false;
            rowLayout.childForceExpandWidth = false;
        }

        for (int i = 0; i < record.options.Count; i++)
        {
            int capturedIndex = i;
            var option = record.options[i];
            var buttonObject = Instantiate(optionButtonPrefab, buttonRow);
            var button = buttonObject.GetComponent<Button>();
            if (button == null)
            {
                Debug.LogWarning("Option button prefab does not contain a Button component.", buttonObject);
                continue;
            }

            var label = button.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null) label.text = option.label;
            button.onClick.AddListener(() =>
            {
                PoliticalEventManager.Instance?.ResolveEvent(record.id, capturedIndex);
                var playerCiv = CivilizationManager.Instance?.GetAllCivs()?.FirstOrDefault(c => c != null && c.isPlayerControlled);
                Show(playerCiv);
            });
        }
    }

    private TextMeshProUGUI CreateText(string name, int size, FontStyles style, TMP_FontAsset font, TextAlignmentOptions alignment)
        => CreateText(transform, name, size, style, alignment, font);

    private TextMeshProUGUI CreateText(Transform parent, string name, int size, FontStyles style, TextAlignmentOptions alignment, TMP_FontAsset font = null)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.font = font != null ? font : inheritedEntryFont ?? TMP_Settings.defaultFontAsset;
        tmp.fontSize = size;
        tmp.fontStyle = style | inheritedEntryFontStyle;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.color = inheritedEntryColor;
        return tmp;
    }

    private void CacheOptionButtonTextStyle()
    {
        if (optionButtonPrefab == null) return;

        var template = optionButtonPrefab.GetComponentInChildren<TextMeshProUGUI>(true);
        if (template == null) return;

        inheritedEntryFont = template.font;
        inheritedEntryFontStyle = template.fontStyle;
        inheritedEntryColor = template.color;
    }

    private Transform GetSectionRoot(string header)
    {
        if (header.IndexOf("Governor", StringComparison.OrdinalIgnoreCase) >= 0)
            return governorContainer != null ? governorContainer : contentRoot;

        if (header.IndexOf("Lord", StringComparison.OrdinalIgnoreCase) >= 0 || header.IndexOf("Royal Council", StringComparison.OrdinalIgnoreCase) >= 0)
            return councilContainer != null ? councilContainer : contentRoot;

        if (header.IndexOf("Vassal", StringComparison.OrdinalIgnoreCase) >= 0 || header.IndexOf("Overlord", StringComparison.OrdinalIgnoreCase) >= 0)
            return vassalContainer != null ? vassalContainer : contentRoot;

        return contentRoot;
    }

    private Button CreateButton(string label, TMP_FontAsset font, Action onClick, Transform parent = null)
    {
        parent ??= transform;
        var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.22f, 0.28f, 0.38f, 1f);
        go.GetComponent<LayoutElement>().preferredHeight = 36f;
        go.GetComponent<LayoutElement>().preferredWidth = 180f;

        var button = go.GetComponent<Button>();
        button.onClick.AddListener(() =>
        {
            onClick?.Invoke();
        });

        var text = CreateText(go.transform, "Label", 18, FontStyles.Normal, TextAlignmentOptions.Center, font);
        text.text = label;
        var rect = text.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return button;
    }
}
