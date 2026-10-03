using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Political assignment panel for every city and herd in a civilization.</summary>
public class GovernorHoldingsPanelUI : MonoBehaviour
{
    [Header("Root")]
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button closeButton;

    [Header("Governor Navigation")]
    [SerializeField] private Button previousGovernorButton;
    [SerializeField] private Button nextGovernorButton;
    [SerializeField] private Image governorPortraitImage;
    [SerializeField] private TMP_Text governorNameText;
    [SerializeField] private TMP_Text loyaltyText;
    [SerializeField] private TMP_Text personalityText;
    [SerializeField] private TMP_Text holdingsCountText;

    [Header("Cities")]
    [SerializeField] private ScrollRect citiesScroll;
    [SerializeField] private Transform citiesRoot;
    [Header("Herds")]
    [SerializeField] private ScrollRect herdsScroll;
    [SerializeField] private Transform herdsRoot;
    [Header("Row")]
    [SerializeField] private GovernorHoldingRowUI rowPrefab;

    private readonly List<GovernorHoldingRowUI> cityRows = new List<GovernorHoldingRowUI>();
    private readonly List<GovernorHoldingRowUI> herdRows = new List<GovernorHoldingRowUI>();
    private Civilization civ;
    private Governor governor;
    private int governorIndex;
    private Action onChanged;
    private GovernmentPanel governmentPanel;

    private GameObject Root => root != null ? root : gameObject;
    public bool IsVisible => Root.activeSelf;

    public void Show(Civilization civilization, Governor target, Action changed)
    {
        civ = civilization;
        governor = target;
        onChanged = changed;
        governmentPanel = GetComponentInParent<GovernmentPanel>();
        GovernmentUiUtil.SetClick(closeButton, Hide);
        Root.SetActive(true);
        SelectCurrentGovernor();
        Refresh(true);
    }

    public void Hide()
    {
        civ = null;
        governor = null;
        Root.SetActive(false);
    }

    private List<Governor> ValidGovernors() => civ?.governors?.Where(g => g != null).ToList() ?? new List<Governor>();

    private void SelectCurrentGovernor()
    {
        var valid = ValidGovernors();
        int found = valid.IndexOf(governor);
        governorIndex = found >= 0 ? found : Mathf.Clamp(governorIndex, 0, Mathf.Max(0, valid.Count - 1));
        governor = valid.Count > 0 ? valid[governorIndex] : null;
    }

    private void Navigate(int direction)
    {
        var valid = ValidGovernors();
        if (valid.Count < 2) return;
        int current = valid.IndexOf(governor);
        governorIndex = ((current < 0 ? governorIndex : current) + direction + valid.Count) % valid.Count;
        governor = valid[governorIndex];
        Refresh(true);
    }

    private void Refresh(bool resetScroll = false)
    {
        SelectCurrentGovernor();
        var valid = ValidGovernors();
        GovernmentUiUtil.SetText(titleText, "GOVERNOR HOLDINGS");
        GovernmentUiUtil.SetClick(previousGovernorButton, () => Navigate(-1));
        GovernmentUiUtil.SetClick(nextGovernorButton, () => Navigate(1));
        GovernmentUiUtil.SetInteractable(previousGovernorButton, valid.Count > 1);
        GovernmentUiUtil.SetInteractable(nextGovernorButton, valid.Count > 1);

        if (governor == null)
        {
            GovernmentUiUtil.SetImage(governorPortraitImage, null);
            GovernmentUiUtil.SetText(governorNameText, "No governors");
            GovernmentUiUtil.SetText(loyaltyText, string.Empty);
            GovernmentUiUtil.SetText(personalityText, string.Empty);
            GovernmentUiUtil.SetText(holdingsCountText, "0 Cities • 0 Herds");
            GovernmentUiUtil.FillList(citiesRoot, rowPrefab, cityRows, Array.Empty<City>(), (row, city) => { });
            GovernmentUiUtil.FillList(herdsRoot, rowPrefab, herdRows, Array.Empty<Herd>(), (row, herd) => { });
            return;
        }

        string governorName = GovernmentPresentation.FormatGovernorName(civ, governor);
        GovernmentUiUtil.SetImage(governorPortraitImage, GovernorPortraitService.GetSprite(governor.PortraitId));
        GovernmentUiUtil.SetText(governorNameText, governorName);
        GovernmentUiUtil.SetText(loyaltyText, $"Loyalty {governor.Opinion:+0;-0;0}");
        GovernmentUiUtil.SetText(personalityText, string.Join(" • ", governor.PersonalityTraits.Take(2)));
        GovernmentUiUtil.SetText(holdingsCountText,
            $"{governor.Cities.Count} {(governor.Cities.Count == 1 ? "City" : "Cities")} • {governor.Herds.Count} {(governor.Herds.Count == 1 ? "Herd" : "Herds")}");

        var cities = civ.cities.Where(c => c != null).OrderBy(HoldingGroup).ThenBy(GovernmentPresentation.NameOf).ToList();
        GovernmentUiUtil.FillList(citiesRoot, rowPrefab, cityRows, cities, BindCity);
        var herds = (civ.herds ?? new List<Herd>()).Where(h => h != null).OrderBy(HoldingGroup).ThenBy(GovernmentPresentation.NameOf).ToList();
        GovernmentUiUtil.FillList(herdsRoot, rowPrefab, herdRows, herds, BindHerd);
        if (resetScroll)
        {
            if (citiesScroll != null) citiesScroll.verticalNormalizedPosition = 1f;
            if (herdsScroll != null) herdsScroll.verticalNormalizedPosition = 1f;
        }
    }

    private int HoldingGroup(City city) => city.governor == governor ? 0 : city.governor == null ? 1 : 2;
    private int HoldingGroup(Herd herd) => herd.governor == governor ? 0 : herd.governor == null ? 1 : 2;
    private string OwnerLabel(Governor owner) => owner == null ? "Unassigned" : $"Controlled by {GovernmentPresentation.FormatGovernorName(civ, owner)}";

    private void BindCity(GovernorHoldingRowUI row, City city)
    {
        Governor owner = city.governor;
        bool dangerous = owner != null;
        string action = owner == null ? "ASSIGN" : owner == governor ? "REMOVE" : "TRANSFER";
        row.Bind(null, GovernmentPresentation.NameOf(city), $"Population {city.Population} • Level {city.level}", OwnerLabel(owner), action,
            dangerous, civ.governorsEnabled, () => ActOnCity(city, owner));
    }

    private void BindHerd(GovernorHoldingRowUI row, Herd herd)
    {
        Governor owner = herd.governor;
        bool dangerous = owner != null;
        string action = owner == null ? "ASSIGN" : owner == governor ? "REMOVE" : "TRANSFER";
        row.Bind(null, GovernmentPresentation.NameOf(herd), $"Level {herd.level}", OwnerLabel(owner), action,
            dangerous, civ.governorsEnabled, () => ActOnHerd(herd, owner));
    }

    private void ActOnCity(City city, Governor owner)
    {
        if (owner == null) { Apply(civ.AssignGovernorToCity(governor, city)); return; }
        var preview = Civilization.PreviewHoldingChange(owner, owner == governor ? null : governor, true);
        string cityName = GovernmentPresentation.NameOf(city);
        string oldName = GovernmentPresentation.FormatGovernorName(civ, owner);
        bool remove = owner == governor;
        var request = new PoliticalConfirmRequest
        {
            title = remove ? "Revoke City?" : $"Transfer {cityName}?",
            description = remove ? $"Removing {cityName} from {oldName} will leave the city unassigned."
                : $"Transfer {cityName} from {oldName} to {GovernmentPresentation.FormatGovernorName(civ, governor)}?",
            confirmLabel = remove ? "REMOVE" : "TRANSFER",
            onConfirm = () => Apply(remove ? civ.RemoveGovernorFromCity(governor, city) : civ.AssignGovernorToCity(governor, city))
        };
        AddPreviewLines(request, owner, remove ? null : governor, preview);
        RequestConfirmation(request);
    }

    private void ActOnHerd(Herd herd, Governor owner)
    {
        if (owner == null) { Apply(civ.AssignGovernorToHerd(governor, herd)); return; }
        bool remove = owner == governor;
        var preview = Civilization.PreviewHoldingChange(owner, remove ? null : governor, false);
        string herdName = GovernmentPresentation.NameOf(herd);
        var request = new PoliticalConfirmRequest
        {
            title = remove ? "Revoke Herd?" : $"Transfer {herdName}?",
            description = remove ? $"Removing {herdName} from {GovernmentPresentation.FormatGovernorName(civ, owner)} will leave it unassigned."
                : $"Transfer {herdName} from {GovernmentPresentation.FormatGovernorName(civ, owner)} to {GovernmentPresentation.FormatGovernorName(civ, governor)}?",
            confirmLabel = remove ? "REMOVE" : "TRANSFER",
            onConfirm = () => Apply(remove ? civ.RemoveGovernorFromHerd(governor, herd) : civ.AssignGovernorToHerd(governor, herd))
        };
        AddPreviewLines(request, owner, remove ? null : governor, preview);
        RequestConfirmation(request);
    }

    private void AddPreviewLines(PoliticalConfirmRequest request, Governor oldOwner, Governor recipient, Civilization.HoldingOpinionPreview preview)
    {
        request.lines.Add(new PoliticalEffectLine { label = GovernmentPresentation.FormatGovernorName(civ, oldOwner), value = $"{Mathf.RoundToInt(preview.oldGovernorChange):+0;-0;0} Loyalty", harmful = true });
        if (preview.addsGrievance)
            request.lines.Add(new PoliticalEffectLine { label = "Political grievance", value = "City Reassigned", harmful = true });
        if (recipient != null)
            request.lines.Add(new PoliticalEffectLine { label = GovernmentPresentation.FormatGovernorName(civ, recipient), value = $"{Mathf.RoundToInt(preview.newGovernorChange):+0;-0;0} Loyalty", beneficial = true });
    }

    private void RequestConfirmation(PoliticalConfirmRequest request)
    {
        if (governmentPanel != null) governmentPanel.RequestConfirmation(request);
        else { GovernmentUiUtil.SetText(statusText, "Confirmation dialog is unavailable."); }
    }

    private void Apply(bool succeeded)
    {
        GovernmentUiUtil.SetText(statusText, succeeded ? string.Empty : "That assignment could not be made.");
        Refresh();
        if (succeeded) onChanged?.Invoke();
    }
}
