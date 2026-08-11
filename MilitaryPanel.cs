// Assets/Scripts Repo/MilitaryPanel.cs
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Top-level Military panel: coordinates the existing Equipment tab (the pre-existing
/// EquipmentManagerPanel/"Equipment Panel" GameObject, referenced directly and shown/hidden
/// in place -- NOT reparented, since its root uses an independent Screen Space Overlay Canvas)
/// plus a new Armies tab listing military formations ("armies"), their units, and their
/// assigned commanders (Governors/Admirals), with UI to assign/remove commanders via
/// MilitaryCommanderAssignmentService.
///
/// Show(Civilization)/ShowDefault()/Hide() mirror EquipmentManagerPanel's API so UIManager's
/// existing SendMessage-based wiring (ShowEquipmentPanel/HideEquipmentPanel) continues to work
/// unchanged if this panel's root GameObject is assigned to UIManager's `equipmentPanel` field.
///
/// NOTE: `equipmentPanelInstance` must be assigned in the Inspector to the existing Equipment
/// Panel GameObject already present in the scene/HUD prefab -- it is intentionally NOT
/// instantiated or reparented here.
/// </summary>
public class MilitaryPanel : MonoBehaviour
{
    [Header("Equipment Tab (existing panel, referenced in place)")]
    [SerializeField] private GameObject equipmentPanelInstance;

    [Header("Tabs")]
    [SerializeField] private Button equipmentTabButton;
    [SerializeField] private Button armiesTabButton;
    [SerializeField] private GameObject armiesTabContent;

    [Header("Armies List")]
    [SerializeField] private RectTransform armyListContent;
    [SerializeField] private GameObject armyEntryPrefab;
    [SerializeField] private GameObject unitEntryPrefab;

    [Header("Commander Management")]
    [SerializeField] private GameObject commanderManagementPanel;
    [SerializeField] private TMP_Text commanderManagementTitle;
    [SerializeField] private RectTransform commanderAssignmentListContent;
    [SerializeField] private GameObject commanderAssignmentRowPrefab;

    [Header("Commander Management - Character Type Cycler")]
    [SerializeField] private TMP_Text characterTypeLabel;
    [SerializeField] private Button characterTypePrevButton;
    [SerializeField] private Button characterTypeNextButton;

    [Header("Commander Management - Character Cycler")]
    [SerializeField] private TMP_Text characterLabel;
    [SerializeField] private Button characterPrevButton;
    [SerializeField] private Button characterNextButton;

    [Header("Commander Management - Role Cycler")]
    [SerializeField] private TMP_Text roleLabel;
    [SerializeField] private Button rolePrevButton;
    [SerializeField] private Button roleNextButton;

    [Header("Commander Management - Actions")]
    [SerializeField] private Button assignCommanderButton;
    [SerializeField] private Button closeCommanderManagementButton;

    [Header("Panel Controls")]
    [SerializeField] private Button closeButton;

    private EquipmentManagerPanel equipmentManagerPanel;
    private Civilization currentCiv;
    private string managingFormationId;

    private int characterTypeIndex; // 0 = Governor, 1 = Admiral
    private int characterIndex;
    private int roleIndex;
    private List<string> characterOptionNames = new List<string>();
    private static readonly string[] CharacterTypeNames = { "Governor", "Admiral" };
    private static readonly string[] RoleNames = System.Enum.GetNames(typeof(CommandRole));

    private void Awake()
    {
        if (equipmentManagerPanel == null && equipmentPanelInstance != null)
            equipmentManagerPanel = equipmentPanelInstance.GetComponent<EquipmentManagerPanel>();

        if (equipmentTabButton != null) equipmentTabButton.onClick.AddListener(ShowEquipmentTab);
        if (armiesTabButton != null) armiesTabButton.onClick.AddListener(ShowArmiesTab);
        if (closeButton != null) closeButton.onClick.AddListener(Hide);
        if (closeCommanderManagementButton != null) closeCommanderManagementButton.onClick.AddListener(CloseCommanderManagement);
        if (assignCommanderButton != null) assignCommanderButton.onClick.AddListener(OnAssignCommanderClicked);

        if (characterTypePrevButton != null) characterTypePrevButton.onClick.AddListener(() => CycleCharacterType(-1));
        if (characterTypeNextButton != null) characterTypeNextButton.onClick.AddListener(() => CycleCharacterType(1));
        if (characterPrevButton != null) characterPrevButton.onClick.AddListener(() => CycleCharacter(-1));
        if (characterNextButton != null) characterNextButton.onClick.AddListener(() => CycleCharacter(1));
        if (rolePrevButton != null) rolePrevButton.onClick.AddListener(() => CycleRole(-1));
        if (roleNextButton != null) roleNextButton.onClick.AddListener(() => CycleRole(1));

        EnsureListLayout(armyListContent);
        EnsureListLayout(commanderAssignmentListContent);

        if (commanderManagementPanel != null) commanderManagementPanel.SetActive(false);
    }

    /// <summary>
    /// Ensures a runtime-populated list container auto-stacks its children vertically,
    /// without requiring these layout components to be hand-authored into prefab YAML.
    /// </summary>
    private static void EnsureListLayout(RectTransform container)
    {
        if (container == null) return;
        if (!container.TryGetComponent<VerticalLayoutGroup>(out var layout))
            layout = container.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 4f;

        if (!container.TryGetComponent<ContentSizeFitter>(out var fitter))
            fitter = container.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    public void Show(Civilization civ)
    {
        currentCiv = civ;
        if (equipmentManagerPanel != null) equipmentManagerPanel.Show(civ);
        PopulateArmies();
        ShowEquipmentTab();
        gameObject.SetActive(true);
    }

    public void ShowDefault()
    {
        if (equipmentManagerPanel != null) equipmentManagerPanel.ShowDefault();
        ShowEquipmentTab();
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (equipmentManagerPanel != null) equipmentManagerPanel.Hide();
        if (commanderManagementPanel != null) commanderManagementPanel.SetActive(false);
        gameObject.SetActive(false);
        currentCiv = null;
        managingFormationId = null;
    }

    public void ShowEquipmentTab()
    {
        if (equipmentPanelInstance != null) equipmentPanelInstance.SetActive(true);
        if (armiesTabContent != null) armiesTabContent.SetActive(false);
    }

    public void ShowArmiesTab()
    {
        if (equipmentPanelInstance != null) equipmentPanelInstance.SetActive(false);
        if (armiesTabContent != null) armiesTabContent.SetActive(true);
        PopulateArmies();
    }

    private void ClearChildren(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    private void PopulateArmies()
    {
        if (armyListContent == null) return;
        ClearChildren(armyListContent);
        if (currentCiv == null || armyEntryPrefab == null) return;

        var formations = currentCiv.GetMilitaryFormations();
        foreach (var formation in formations)
            BuildArmyEntry(formation);
    }

    private void BuildArmyEntry(MilitaryFormationSummary formation)
    {
        var entry = Instantiate(armyEntryPrefab, armyListContent);

        var nameText = entry.transform.Find("Name")?.GetComponent<TMP_Text>();
        if (nameText != null) nameText.text = formation.FormationName;

        var typeText = entry.transform.Find("Type")?.GetComponent<TMP_Text>();
        if (typeText != null) typeText.text = formation.FormationType.ToString();

        var unitCountText = entry.transform.Find("UnitCount")?.GetComponent<TMP_Text>();
        if (unitCountText != null) unitCountText.text = $"{formation.Members.Count} unit(s)";

        var commandersText = entry.transform.Find("Commanders")?.GetComponent<TMP_Text>();
        if (commandersText != null) commandersText.text = BuildCommandersSummary(formation.Commanders);

        var manageButton = entry.transform.Find("ManageButton")?.GetComponent<Button>();
        if (manageButton != null)
        {
            string formationId = formation.FormationId;
            manageButton.onClick.AddListener(() => OpenCommanderManagement(formationId));
        }

        var expandButton = entry.transform.Find("ExpandButton")?.GetComponent<Button>();
        var unitListContainer = entry.transform.Find("UnitListContainer") as RectTransform;
        if (unitListContainer != null)
        {
            unitListContainer.gameObject.SetActive(false);
            EnsureListLayout(unitListContainer);
            BuildUnitRows(formation.Members, unitListContainer);
            if (expandButton != null)
                expandButton.onClick.AddListener(() => unitListContainer.gameObject.SetActive(!unitListContainer.gameObject.activeSelf));
        }
    }

    private void BuildUnitRows(List<CombatUnit> members, Transform container)
    {
        if (unitEntryPrefab == null || container == null) return;
        foreach (var unit in members)
        {
            if (unit == null) continue;
            var row = Instantiate(unitEntryPrefab, container);
            var nameText = row.transform.Find("Name")?.GetComponent<TMP_Text>();
            if (nameText != null) nameText.text = unit.data != null ? unit.data.unitName : unit.name;
            var healthText = row.transform.Find("Health")?.GetComponent<TMP_Text>();
            if (healthText != null) healthText.text = $"{unit.currentHealth}/{unit.MaxHealth} HP";
        }
    }

    private string BuildCommandersSummary(List<MilitaryCommanderAssignment> commanders)
    {
        if (commanders == null || commanders.Count == 0) return "No commander assigned";
        var sb = new StringBuilder();
        for (int i = 0; i < commanders.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(FormatAssignment(commanders[i]));
        }
        return sb.ToString();
    }

    private string FormatAssignment(MilitaryCommanderAssignment assignment)
    {
        string characterName = GetCharacterName(assignment.CharacterKind, assignment.CharacterId);
        return $"{assignment.Role}: {characterName}";
    }

    private string GetCharacterName(CommanderCharacterKind kind, int characterId)
    {
        if (kind == CommanderCharacterKind.Governor)
        {
            var governor = currentCiv?.governors?.FirstOrDefault(g => g.Id == characterId);
            return governor != null ? governor.Name : "Unknown Governor";
        }

        var admiral = AdmiralManager.Instance != null ? AdmiralManager.Instance.GetAdmiral(characterId) : null;
        return admiral != null ? admiral.admiralName : "Unknown Admiral";
    }

    private void OpenCommanderManagement(string formationId)
    {
        managingFormationId = formationId;
        if (commanderManagementPanel != null) commanderManagementPanel.SetActive(true);
        if (commanderManagementTitle != null) commanderManagementTitle.text = $"Manage Commanders: {formationId}";
        PopulateCommanderAssignmentList(formationId);
        characterTypeIndex = 0;
        roleIndex = 0;
        RefreshCharacterTypeLabel();
        RefreshRoleLabel();
        RefreshCharacterOptions();
    }

    private void CloseCommanderManagement()
    {
        if (commanderManagementPanel != null) commanderManagementPanel.SetActive(false);
        managingFormationId = null;
        PopulateArmies();
    }

    private void PopulateCommanderAssignmentList(string formationId)
    {
        if (commanderAssignmentListContent == null) return;
        ClearChildren(commanderAssignmentListContent);
        if (commanderAssignmentRowPrefab == null) return;

        var service = MilitaryCommanderAssignmentService.Instance;
        var assignments = service != null ? service.GetAssignments(formationId) : null;
        if (assignments == null) return;

        foreach (var assignment in assignments)
        {
            var row = Instantiate(commanderAssignmentRowPrefab, commanderAssignmentListContent);
            var label = row.transform.Find("Label")?.GetComponent<TMP_Text>();
            if (label != null) label.text = FormatAssignment(assignment);

            var removeButton = row.transform.Find("RemoveButton")?.GetComponent<Button>();
            if (removeButton != null)
            {
                var role = assignment.Role;
                removeButton.onClick.AddListener(() =>
                {
                    MilitaryCommanderAssignmentService.Instance?.RemoveAssignment(formationId, role);
                    PopulateCommanderAssignmentList(formationId);
                });
            }
        }
    }

    private void CycleCharacterType(int direction)
    {
        characterTypeIndex = (characterTypeIndex + direction + CharacterTypeNames.Length) % CharacterTypeNames.Length;
        RefreshCharacterTypeLabel();
        RefreshCharacterOptions();
    }

    private void CycleCharacter(int direction)
    {
        if (characterOptionNames.Count == 0) return;
        characterIndex = (characterIndex + direction + characterOptionNames.Count) % characterOptionNames.Count;
        RefreshCharacterLabel();
    }

    private void CycleRole(int direction)
    {
        roleIndex = (roleIndex + direction + RoleNames.Length) % RoleNames.Length;
        RefreshRoleLabel();
    }

    private void RefreshCharacterTypeLabel()
    {
        if (characterTypeLabel != null) characterTypeLabel.text = CharacterTypeNames[characterTypeIndex];
    }

    private void RefreshRoleLabel()
    {
        if (roleLabel != null) roleLabel.text = RoleNames[roleIndex];
    }

    private void RefreshCharacterLabel()
    {
        if (characterLabel == null) return;
        characterLabel.text = characterOptionNames.Count == 0 ? "None available" : characterOptionNames[characterIndex];
    }

    private bool IsGovernorSelected => characterTypeIndex == 0;

    private void RefreshCharacterOptions()
    {
        characterOptionNames.Clear();
        if (currentCiv != null)
        {
            if (IsGovernorSelected)
            {
                if (currentCiv.governors != null)
                    characterOptionNames.AddRange(currentCiv.governors.Select(g => g.Name));
            }
            else
            {
                // MapActorSlot (stable), not GetCivIndex (mutable list position) - matches
                // MilitaryCommanderAssignmentService.OwnerCivilizationId's convention for admiral ownership.
                int civIndex = currentCiv.MapActorSlot;
                if (AdmiralManager.Instance != null)
                    characterOptionNames.AddRange(AdmiralManager.Instance.admirals
                        .Where(a => a.ownerCivilizationId == civIndex && a.status == AdmiralStatus.Active)
                        .Select(a => a.admiralName));
            }
        }
        characterIndex = 0;
        RefreshCharacterLabel();
    }

    private void OnAssignCommanderClicked()
    {
        if (currentCiv == null || string.IsNullOrEmpty(managingFormationId)) return;
        if (characterOptionNames.Count == 0) return;

        var role = (CommandRole)roleIndex;
        var service = MilitaryCommanderAssignmentService.GetOrCreate();
        bool success;
        string reason;

        if (IsGovernorSelected)
        {
            var governor = currentCiv.governors != null && characterIndex < currentCiv.governors.Count
                ? currentCiv.governors[characterIndex]
                : null;
            success = service.TryAssignGovernor(currentCiv, governor, managingFormationId, role, out reason);
        }
        else
        {
            int civIndex = currentCiv.MapActorSlot;
            var eligibleAdmirals = AdmiralManager.Instance != null
                ? AdmiralManager.Instance.admirals.Where(a => a.ownerCivilizationId == civIndex && a.status == AdmiralStatus.Active).ToList()
                : new List<AdmiralInstance>();
            var admiral = characterIndex < eligibleAdmirals.Count ? eligibleAdmirals[characterIndex] : null;
            success = service.TryAssignAdmiral(currentCiv, admiral, managingFormationId, role, out reason);
        }

        if (!success)
        {
            UIManager.Instance?.ShowNotification($"Could not assign commander: {reason}");
            return;
        }

        PopulateCommanderAssignmentList(managingFormationId);
    }
}
