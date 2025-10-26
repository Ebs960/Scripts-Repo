using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Text;

/// <summary>
/// Global tooltip system for displaying hover information
/// </summary>
public class TooltipSystem : MonoBehaviour
{
    public static TooltipSystem Instance { get; private set; }

    [Header("Tooltip UI")]
    public GameObject tooltipPanel;
    public TextMeshProUGUI tooltipTitle;
    public TextMeshProUGUI tooltipDescription;
    public TextMeshProUGUI tooltipBonuses;
    public TextMeshProUGUI tooltipUnlocks;
    public LayoutElement tooltipLayoutElement;
    
    [Header("Settings")]
    public float maxTooltipWidth = 400f;
    public Vector3 tooltipOffset = new Vector3(10f, 10f, 0f);

    private RectTransform tooltipRect;
    private Canvas tooltipCanvas;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Initialize();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Initialize()
    {
        if (tooltipPanel != null)
        {
            tooltipRect = tooltipPanel.GetComponent<RectTransform>();
            tooltipCanvas = GetComponentInParent<Canvas>();
            HideTooltip();
        }
    }

    void Update()
    {
        if (tooltipPanel != null && tooltipPanel.activeInHierarchy)
        {
            UpdateTooltipPosition();
        }
    }

    public void ShowTechTooltip(TechData tech, Civilization civ)
    {
        if (tech == null) return;

        if (tooltipTitle != null) tooltipTitle.text = tech.techName;
        if (tooltipDescription != null) tooltipDescription.text = tech.description;

        // Build bonuses text
        StringBuilder bonuses = new StringBuilder();
        if (tech.attackBonus > 0) bonuses.AppendLine($"+{tech.attackBonus}% Attack");
        if (tech.defenseBonus > 0) bonuses.AppendLine($"+{tech.defenseBonus}% Defense");
        if (tech.movementBonus > 0) bonuses.AppendLine($"+{tech.movementBonus} Movement");
        if (tech.foodModifier > 0) bonuses.AppendLine($"+{tech.foodModifier}% Food");
        if (tech.productionModifier > 0) bonuses.AppendLine($"+{tech.productionModifier}% Production");
        if (tech.goldModifier > 0) bonuses.AppendLine($"+{tech.goldModifier}% Gold");
        if (tech.scienceModifier > 0) bonuses.AppendLine($"+{tech.scienceModifier}% Science");
        if (tech.cultureModifier > 0) bonuses.AppendLine($"+{tech.cultureModifier}% Culture");
        if (tech.faithModifier > 0) bonuses.AppendLine($"+{tech.faithModifier}% Faith");

        if (tooltipBonuses != null) 
            tooltipBonuses.text = bonuses.Length > 0 ? bonuses.ToString().TrimEnd() : "No bonuses";

        // Build unlocks text
        StringBuilder unlocks = new StringBuilder();
        if (tech.unlockedUnits != null && tech.unlockedUnits.Length > 0)
        {
            unlocks.AppendLine("Units:");
            foreach (var unit in tech.unlockedUnits)
                if (unit != null) unlocks.AppendLine($"  • {unit.unitName}");
        }
        if (tech.unlockedBuildings != null && tech.unlockedBuildings.Length > 0)
        {
            unlocks.AppendLine("Buildings:");
            foreach (var building in tech.unlockedBuildings)
                if (building != null) unlocks.AppendLine($"  • {building.buildingName}");
        }
        // REMOVED: Equipment unlocks display
        // Equipment is no longer "unlocked" by techs (no free items)
        // Instead, equipment becomes producible when EquipmentData.requiredTechs are met

        if (tooltipUnlocks != null)
            tooltipUnlocks.text = unlocks.Length > 0 ? unlocks.ToString().TrimEnd() : "Nothing";

        ShowTooltip();
    }

    public void ShowCultureTooltip(CultureData culture, Civilization civ)
    {
        if (culture == null) return;

        if (tooltipTitle != null) tooltipTitle.text = culture.cultureName;
        if (tooltipDescription != null) tooltipDescription.text = culture.description;

        // Build bonuses text
        StringBuilder bonuses = new StringBuilder();
        if (culture.attackBonus > 0) bonuses.AppendLine($"+{culture.attackBonus}% Attack");
        if (culture.defenseBonus > 0) bonuses.AppendLine($"+{culture.defenseBonus}% Defense");
        if (culture.movementBonus > 0) bonuses.AppendLine($"+{culture.movementBonus} Movement");
        if (culture.foodModifier > 0) bonuses.AppendLine($"+{culture.foodModifier}% Food");
        if (culture.productionModifier > 0) bonuses.AppendLine($"+{culture.productionModifier}% Production");
        if (culture.goldModifier > 0) bonuses.AppendLine($"+{culture.goldModifier}% Gold");
        if (culture.scienceModifier > 0) bonuses.AppendLine($"+{culture.scienceModifier}% Science");
        if (culture.cultureModifier > 0) bonuses.AppendLine($"+{culture.cultureModifier}% Culture");
        if (culture.faithModifier > 0) bonuses.AppendLine($"+{culture.faithModifier}% Faith");

        if (tooltipBonuses != null)
            tooltipBonuses.text = bonuses.Length > 0 ? bonuses.ToString().TrimEnd() : "No bonuses";

        // Build unlocks text
        StringBuilder unlocks = new StringBuilder();
        if (culture.unlockedUnits != null && culture.unlockedUnits.Length > 0)
        {
            unlocks.AppendLine("Units:");
            foreach (var unit in culture.unlockedUnits)
                if (unit != null) unlocks.AppendLine($"  • {unit.unitName}");
        }
        if (culture.unlockedBuildings != null && culture.unlockedBuildings.Length > 0)
        {
            unlocks.AppendLine("Buildings:");
            foreach (var building in culture.unlockedBuildings)
                if (building != null) unlocks.AppendLine($"  • {building.buildingName}");
        }
        if (culture.unlocksPolicies != null && culture.unlocksPolicies.Length > 0)
        {
            unlocks.AppendLine("Policies:");
            foreach (var policy in culture.unlocksPolicies)
                if (policy != null) unlocks.AppendLine($"  • {policy.policyName}");
        }

        if (tooltipUnlocks != null)
            tooltipUnlocks.text = unlocks.Length > 0 ? unlocks.ToString().TrimEnd() : "Nothing";

        ShowTooltip();
    }

    void ShowTooltip()
    {
        if (tooltipPanel != null)
        {
            tooltipPanel.SetActive(true);
            UpdateTooltipPosition();
        }
    }

    public void HideTooltip()
    {
        if (tooltipPanel != null)
        {
            tooltipPanel.SetActive(false);
        }
    }

    void UpdateTooltipPosition()
    {
        if (tooltipRect == null || tooltipCanvas == null) return;

        Vector2 mousePosition = Input.mousePosition;
        
        // Convert mouse position to canvas position
        Vector2 canvasPosition;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            tooltipCanvas.transform as RectTransform,
            mousePosition,
            tooltipCanvas.worldCamera,
            out canvasPosition);

        // Apply offset
        canvasPosition += new Vector2(tooltipOffset.x, tooltipOffset.y);

        // Keep tooltip within screen bounds
        Vector2 canvasSize = (tooltipCanvas.transform as RectTransform).sizeDelta;
        Vector2 tooltipSize = tooltipRect.sizeDelta;

        if (canvasPosition.x + tooltipSize.x > canvasSize.x)
            canvasPosition.x = canvasSize.x - tooltipSize.x;
        if (canvasPosition.y + tooltipSize.y > canvasSize.y)
            canvasPosition.y = canvasSize.y - tooltipSize.y;
        if (canvasPosition.x < 0)
            canvasPosition.x = 0;
        if (canvasPosition.y < 0)
            canvasPosition.y = 0;

        tooltipRect.localPosition = canvasPosition;
    }
}
