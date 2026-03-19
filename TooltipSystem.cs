using UnityEngine;
using UnityEngine.InputSystem;
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

        // Build bonuses text (percent modifiers shown as percentages; include flat bonuses)
        StringBuilder bonuses = new StringBuilder();
        if (tech.attackBonus != 0) bonuses.AppendLine($"{(tech.attackBonus>0?"+":"")}{tech.attackBonus}% Attack");
        if (tech.defenseBonus != 0) bonuses.AppendLine($"{(tech.defenseBonus>0?"+":"")}{tech.defenseBonus}% Defense");
        if (tech.movementBonus != 0) bonuses.AppendLine($"{(tech.movementBonus>0?"+":"")}{tech.movementBonus} Movement");
        if (!Mathf.Approximately(tech.foodModifier, 0f)) bonuses.AppendLine($"{(tech.foodModifier>0?"+":"")}{(tech.foodModifier*100f):0.#}% Food");
        if (tech.flatFoodBonus != 0) bonuses.AppendLine($"{(tech.flatFoodBonus>0?"+":"")}{tech.flatFoodBonus} Food");
        if (!Mathf.Approximately(tech.productionModifier, 0f)) bonuses.AppendLine($"{(tech.productionModifier>0?"+":"")}{(tech.productionModifier*100f):0.#}% Production");
        if (tech.flatProductionBonus != 0) bonuses.AppendLine($"{(tech.flatProductionBonus>0?"+":"")}{tech.flatProductionBonus} Production");
        if (!Mathf.Approximately(tech.goldModifier, 0f)) bonuses.AppendLine($"{(tech.goldModifier>0?"+":"")}{(tech.goldModifier*100f):0.#}% Gold");
        if (tech.flatGoldBonus != 0) bonuses.AppendLine($"{(tech.flatGoldBonus>0?"+":"")}{tech.flatGoldBonus} Gold");
        if (!Mathf.Approximately(tech.scienceModifier, 0f)) bonuses.AppendLine($"{(tech.scienceModifier>0?"+":"")}{(tech.scienceModifier*100f):0.#}% Science");
        if (tech.flatScienceBonus != 0) bonuses.AppendLine($"{(tech.flatScienceBonus>0?"+":"")}{tech.flatScienceBonus} Science");
        if (!Mathf.Approximately(tech.cultureModifier, 0f)) bonuses.AppendLine($"{(tech.cultureModifier>0?"+":"")}{(tech.cultureModifier*100f):0.#}% Culture");
        if (tech.flatCultureBonus != 0) bonuses.AppendLine($"{(tech.flatCultureBonus>0?"+":"")}{tech.flatCultureBonus} Culture");
        if (!Mathf.Approximately(tech.faithModifier, 0f)) bonuses.AppendLine($"{(tech.faithModifier>0?"+":"")}{(tech.faithModifier*100f):0.#}% Faith");
        if (tech.flatFaithBonus != 0) bonuses.AppendLine($"{(tech.flatFaithBonus>0?"+":"")}{tech.flatFaithBonus} Faith");

        if (tooltipBonuses != null)
            tooltipBonuses.text = bonuses.Length > 0 ? bonuses.ToString().TrimEnd() : "No bonuses";

        // Build unlocks text
        StringBuilder unlocks = new StringBuilder();
        // REMOVED: TechData no longer directly unlocks units/buildings
        // Availability is now controlled solely by requiredTechs in the respective data classes
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

        // Build bonuses text (percent modifiers shown as percentages; include flat bonuses)
        StringBuilder bonuses = new StringBuilder();
        if (culture.attackBonus != 0) bonuses.AppendLine($"{(culture.attackBonus>0?"+":"")}{culture.attackBonus}% Attack");
        if (culture.defenseBonus != 0) bonuses.AppendLine($"{(culture.defenseBonus>0?"+":"")}{culture.defenseBonus}% Defense");
        if (culture.movementBonus != 0) bonuses.AppendLine($"{(culture.movementBonus>0?"+":"")}{culture.movementBonus} Movement");
        if (!Mathf.Approximately(culture.foodModifier, 0f)) bonuses.AppendLine($"{(culture.foodModifier>0?"+":"")}{(culture.foodModifier*100f):0.#}% Food");
        if (culture.flatFoodBonus != 0) bonuses.AppendLine($"{(culture.flatFoodBonus>0?"+":"")}{culture.flatFoodBonus} Food");
        if (!Mathf.Approximately(culture.productionModifier, 0f)) bonuses.AppendLine($"{(culture.productionModifier>0?"+":"")}{(culture.productionModifier*100f):0.#}% Production");
        if (culture.flatProductionBonus != 0) bonuses.AppendLine($"{(culture.flatProductionBonus>0?"+":"")}{culture.flatProductionBonus} Production");
        if (!Mathf.Approximately(culture.goldModifier, 0f)) bonuses.AppendLine($"{(culture.goldModifier>0?"+":"")}{(culture.goldModifier*100f):0.#}% Gold");
        if (culture.flatGoldBonus != 0) bonuses.AppendLine($"{(culture.flatGoldBonus>0?"+":"")}{culture.flatGoldBonus} Gold");
        if (!Mathf.Approximately(culture.scienceModifier, 0f)) bonuses.AppendLine($"{(culture.scienceModifier>0?"+":"")}{(culture.scienceModifier*100f):0.#}% Science");
        if (culture.flatScienceBonus != 0) bonuses.AppendLine($"{(culture.flatScienceBonus>0?"+":"")}{culture.flatScienceBonus} Science");
        if (!Mathf.Approximately(culture.cultureModifier, 0f)) bonuses.AppendLine($"{(culture.cultureModifier>0?"+":"")}{(culture.cultureModifier*100f):0.#}% Culture");
        if (culture.flatCultureBonus != 0) bonuses.AppendLine($"{(culture.flatCultureBonus>0?"+":"")}{culture.flatCultureBonus} Culture");
        if (!Mathf.Approximately(culture.faithModifier, 0f)) bonuses.AppendLine($"{(culture.faithModifier>0?"+":"")}{(culture.faithModifier*100f):0.#}% Faith");
        if (culture.flatFaithBonus != 0) bonuses.AppendLine($"{(culture.flatFaithBonus>0?"+":"")}{culture.flatFaithBonus} Faith");

        if (tooltipBonuses != null)
            tooltipBonuses.text = bonuses.Length > 0 ? bonuses.ToString().TrimEnd() : "No bonuses";

        // Build unlocks text
        StringBuilder unlocks = new StringBuilder();
        // REMOVED: CultureData no longer directly unlocks units/buildings
        // Availability is now controlled solely by requiredCultures in the respective data classes
        // REMOVED: CultureData no longer directly unlocks policies
        // Policy availability is now controlled solely by requiredTechs/requiredCultures/requiredGovernments in PolicyData
        // This ensures a clean compilation

        if (tooltipUnlocks != null)
            tooltipUnlocks.text = unlocks.Length > 0 ? unlocks.ToString().TrimEnd() : "Nothing";

        ShowTooltip();
    }

    /// <summary>
    /// Show a simple tooltip with a title and description. Hides other sections.
    /// </summary>
    public void ShowSimpleTooltip(string title, string description)
    {
        if (tooltipTitle != null) tooltipTitle.text = title ?? "";
        if (tooltipDescription != null) tooltipDescription.text = description ?? "";
        if (tooltipBonuses != null) tooltipBonuses.text = "";
        if (tooltipUnlocks != null) tooltipUnlocks.text = "";
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

        Vector2 mousePosition = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
        
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
