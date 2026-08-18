using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// World-space unit label: shows only an icon by default and displays a tooltip when hovered.
public class UnitLabel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Icon")]
    [Tooltip("Icon image shown above the unit")]
    [SerializeField] private Image iconImage;

    [Header("Tooltip (optional)")]
    [Tooltip("Root GameObject for the tooltip UI. If provided, it will be enabled on hover and populated with text.")]
    [SerializeField] private GameObject tooltipRoot;
    [SerializeField] private TextMeshProUGUI tooltipText;


    [SerializeField] private Vector3 offset = new Vector3(0, 8f, 0); // Offset above unit - increased for better visibility

    private Transform target;
    private Camera mainCam;
    private CombatUnit combatUnit;
    private GameObject armyBadge;
    private Image armyBadgeBackground;
    private TextMeshProUGUI armyBadgeText;

    // Cached info for tooltip
    private string cachedUnitName;
    private string cachedOwnerName;
    private int cachedCurrentHP;
    private int cachedMaxHP;

    public void Initialize(Transform targetTransform, string unitName, string ownerName, int currentHP, int maxHP)
    {
        target = targetTransform;
        mainCam = Camera.main;

        // Try to auto-detect the unit icon from the parent CombatUnit/WorkerUnit data
        Sprite icon = null;
        if (target != null)
        {
            combatUnit = target.GetComponentInParent<CombatUnit>();
            if (combatUnit != null && combatUnit.data != null) icon = combatUnit.data.GetIcon(combatUnit.owner);
            else
            {
                var worker = target.GetComponentInParent<WorkerUnit>();
                if (worker != null && worker.data != null) icon = worker.data.GetIcon(worker.owner);
            }
        }

        if (iconImage != null)
        {
            if (icon != null) iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        // Initialize cached tooltip data and set tooltip inactive by default
        UpdateLabel(unitName, ownerName, currentHP, maxHP);
        EnsureArmyBadge();
        RefreshArmyBadge();
        if (tooltipRoot != null) tooltipRoot.SetActive(false);
    }

    public void UpdateLabel(string unitName, string ownerName, int currentHP, int maxHP)
    {
        cachedUnitName = unitName;
        cachedOwnerName = ownerName;
        cachedCurrentHP = currentHP;
        cachedMaxHP = maxHP;
        RefreshArmyBadge();


        // Also update tooltip text if present
        if (tooltipText != null)
        {
            tooltipText.text = GetTooltipString();
        }
    }

    /// <summary>
    /// Explicitly set the icon sprite (useful for non-unit targets like improvements)
    /// </summary>
    public void SetIcon(Sprite icon)
    {
        if (iconImage == null) return;
        iconImage.sprite = icon;
        iconImage.enabled = icon != null;
    }

    private string GetTooltipString()
    {
        string unitInfo = string.IsNullOrEmpty(cachedUnitName)
            ? "Unknown Unit"
            : $"{cachedUnitName}\nOwner: {cachedOwnerName}\nHP: {cachedCurrentHP}/{cachedMaxHP}";
        if (combatUnit == null)
            return unitInfo;

        var members = CampaignArmyService.GetMembers(combatUnit);
        int capacity = combatUnit.owner != null ? combatUnit.owner.GetMaxArmySize() : members.Count;
        string armyName = string.IsNullOrWhiteSpace(combatUnit.MilitaryFormationName)
            ? combatUnit.MilitaryFormationType.ToString()
            : combatUnit.MilitaryFormationName;
        return $"{armyName}  {members.Count}/{capacity}\n{unitInfo}";
    }

    void LateUpdate()
    {
        if (target == null) return;
        // Throttle to every 3rd frame, staggered per instance
        if ((Time.frameCount + (this.GetRuntimeId() & 0x7FFFFFFF)) % 3 != 0) return;

        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return;

        transform.position = target.position + offset;
        transform.rotation = mainCam.transform.rotation;
        RefreshArmyBadge();
    }

    private void EnsureArmyBadge()
    {
        if (combatUnit == null || armyBadge != null)
            return;

        armyBadge = new GameObject("Army Capacity Badge", typeof(RectTransform), typeof(Image));
        armyBadge.transform.SetParent(transform, false);
        var rect = armyBadge.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(54f, 24f);
        rect.anchoredPosition = new Vector2(38f, -26f);
        armyBadgeBackground = armyBadge.GetComponent<Image>();

        var textObject = new GameObject("Count", typeof(RectTransform), typeof(TextMeshProUGUI));
        textObject.transform.SetParent(armyBadge.transform, false);
        var textRect = textObject.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = textRect.offsetMax = Vector2.zero;
        armyBadgeText = textObject.GetComponent<TextMeshProUGUI>();
        armyBadgeText.font = TMP_Settings.defaultFontAsset;
        armyBadgeText.fontSize = 14f;
        armyBadgeText.fontStyle = FontStyles.Bold;
        armyBadgeText.alignment = TextAlignmentOptions.Center;
        armyBadgeText.color = Color.white;
        armyBadgeText.raycastTarget = false;
    }

    private void RefreshArmyBadge()
    {
        if (combatUnit == null)
            return;
        EnsureArmyBadge();
        if (armyBadge == null)
            return;

        bool isRepresentative = CampaignArmyService.IsRepresentative(combatUnit);
        armyBadge.SetActive(isRepresentative);
        if (!isRepresentative)
            return;

        int count = CampaignArmyService.GetMembers(combatUnit).Count;
        int capacity = combatUnit.owner != null ? combatUnit.owner.GetMaxArmySize() : count;
        armyBadgeText.text = $"{count}/{capacity}";
        armyBadgeBackground.color = count >= capacity
            ? new Color(0.78f, 0.27f, 0.15f, 0.96f)
            : new Color(0.08f, 0.42f, 0.36f, 0.96f);
    }

    // Pointer events for tooltip
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (tooltipRoot != null)
        {
            tooltipRoot.SetActive(true);
            if (tooltipText != null) tooltipText.text = GetTooltipString();
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tooltipRoot != null)
            tooltipRoot.SetActive(false);
    }
}
