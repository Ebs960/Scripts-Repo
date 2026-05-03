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
    [SerializeField] private GameObject entryPrefab;
    [SerializeField] private Transform buttonContainer;
    [SerializeField] private Button optionButtonPrefab;

    public void Awake()
    {
        if (closeButton != null)
            closeButton.onClick.AddListener(Hide);

        if (panelRoot != null)
            panelRoot.SetActive(false);
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

        titleText.text = "Political Affairs";
        subtitleText.text = $"Governors, lords, vassals, and ongoing events for {civ.civData?.civName ?? civ.name}.";

    private void ClearEntries()
    {
        if (contentRoot == null) return;
        foreach (Transform child in contentRoot)
            Destroy(child.gameObject);
    }

    private void BuildGovernorSection(Civilization civ)
    {
        var lines = new List<string>();
        var governors = civ.governors?.Where(g => g != null).ToList() ?? new List<Governor>();
        lines.Add($"Total Governors: {governors.Count}/{Mathf.Max(0, civ.governorCount)}");
        lines.Add($"Governors Unlocked: {(civ.governorsEnabled ? "Yes" : "No")}");

        if (governors.Count == 0) lines.Add("No governors currently appointed.");
        else
        {
            foreach (var governor in governors.OrderBy(g => g.Name))
            {
                int cityCount = governor.Cities?.Count ?? 0;
                int herdCount = governor.Herds?.Count ?? 0;
                string faction = governor.Faction != null ? governor.Faction.Name : "Unaffiliated";
                lines.Add($"• {governor.Name} | Opinion {Mathf.RoundToInt(governor.Opinion)} | Ambition {Mathf.RoundToInt(governor.AmbitionScore)} | Influence {Mathf.RoundToInt(governor.Influence)} | Cities {cityCount} | Herds {herdCount} | Faction {faction}");
            }
        }

        CreateEntry("Governors", string.Join("\n", lines));
    }

    private void BuildCouncilSection(Civilization civ)
    {
        var lines = new List<string>();
        int maxSeats = civ.MaxCouncilSeats;
        int occupied = civ.royalCouncil?.Count ?? 0;
        lines.Add($"Royal Council Seats: {occupied}/{Mathf.Max(0, maxSeats)}");

        if (occupied <= 0) lines.Add("No lords currently seated on the council.");
        else foreach (var lord in civ.royalCouncil.Where(g => g != null)) lines.Add($"• {lord.Name} ({lord.specialization}) | Opinion {Mathf.RoundToInt(lord.Opinion)}");

        var eligible = civ.GetEligibleGovernorsForCouncil();
        if (eligible != null && eligible.Count > 0)
        {
            lines.Add("Eligible Lords Not Seated:");
            foreach (var lord in eligible.Where(g => g != null))
                lines.Add($"  - {lord.Name} | Influence {Mathf.RoundToInt(lord.Influence)} | Grievances {lord.TotalGrievances()}");
        }

        BuildGovernorSection(civ);
        BuildCouncilSection(civ);
        BuildVassalSection(civ);
        BuildCurrentEventsSection(civ);
    }

    public void Hide()
    {
        if (root != null)
            root.SetActive(false);
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
        var panel = new GameObject(header, typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(contentRoot, false);
        panel.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.06f);
        panel.GetComponent<LayoutElement>().minHeight = 120f;

        var layout = panel.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(12, 12, 12, 12);
        layout.spacing = 6;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        layout.childForceExpandWidth = true;

        var title = CreateText(panel.transform, "EntryTitle", 22, FontStyles.Bold, TextAlignmentOptions.TopLeft);
        title.text = header;
        var text = CreateText(panel.transform, "EntryBody", 18, FontStyles.Normal, TextAlignmentOptions.TopLeft);
        text.text = body;

        if (record == null) return;

        var buttonRow = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
        buttonRow.transform.SetParent(panel.transform, false);
        var rowLayout = buttonRow.GetComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 8;
        rowLayout.childControlWidth = false;
        rowLayout.childForceExpandWidth = false;

        for (int i = 0; i < record.options.Count; i++)
        {
            int capturedIndex = i;
            var option = record.options[i];
            var button = Instantiate(optionButtonPrefab, buttonRoot);
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
        tmp.font = font != null ? font : TMP_Settings.defaultFontAsset;
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.color = Color.white;
        return tmp;
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
            playClick?.Invoke();
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
