using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Small popup that displays when a technology or culture is researched.
/// Attach to a UI panel in the scene and assign the serialized fields.
/// Subscribes to TechManager.OnTechResearchCompleted and CultureManager.OnCultureResearchCompleted.
/// </summary>
public class ResearchPopupUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject popupRoot;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI detailsText; // bonuses / unlocks list
    [SerializeField] private UnityEngine.UI.Button closeButton;

    [Header("Behavior")]
    [Tooltip("How long (seconds) the popup remains visible. 0 = wait for click to dismiss.")]
    [SerializeField] private float autoHideSeconds = 5f;

    private Coroutine hideRoutine;

    void Awake()
    {
        if (popupRoot != null) popupRoot.SetActive(false);
    }


    void OnEnable()
    {
        if (TechManager.Instance != null)
            TechManager.Instance.OnTechResearchCompleted += OnTechCompleted;
        if (CultureManager.Instance != null)
            CultureManager.Instance.OnCultureResearchCompleted += OnCultureCompleted;
        if (closeButton != null) closeButton.onClick.AddListener(Hide);
    }

    void OnDisable()
    {
        if (TechManager.Instance != null)
            TechManager.Instance.OnTechResearchCompleted -= OnTechCompleted;
        if (CultureManager.Instance != null)
            CultureManager.Instance.OnCultureResearchCompleted -= OnCultureCompleted;
        if (closeButton != null) closeButton.onClick.RemoveListener(Hide);
    }

    private void OnTechCompleted(Civilization civ, TechData tech)
    {
        if (tech == null) return;
        ShowTechPopup(civ, tech);
    }

    private void OnCultureCompleted(Civilization civ, CultureData cult)
    {
        if (cult == null) return;
        ShowCulturePopup(civ, cult);
    }

    public void ShowTechPopup(Civilization civ, TechData tech)
    {
        if (popupRoot == null) return;
        if (iconImage != null) iconImage.sprite = tech.techIcon;
        if (titleText != null) titleText.text = tech.techName;
        if (descriptionText != null) descriptionText.text = tech.description;
        if (detailsText != null) detailsText.text = BuildTechDetails(tech);
        popupRoot.SetActive(true);
        if (hideRoutine != null) StopCoroutine(hideRoutine);
        if (autoHideSeconds > 0f) hideRoutine = StartCoroutine(AutoHide(autoHideSeconds));
    }

    public void ShowCulturePopup(Civilization civ, CultureData cult)
    {
        if (popupRoot == null) return;
        if (iconImage != null) iconImage.sprite = cult.cultureIcon;
        if (titleText != null) titleText.text = cult.cultureName;
        if (descriptionText != null) descriptionText.text = cult.description;
        if (detailsText != null) detailsText.text = BuildCultureDetails(cult);
        popupRoot.SetActive(true);
        if (hideRoutine != null) StopCoroutine(hideRoutine);
        if (autoHideSeconds > 0f) hideRoutine = StartCoroutine(AutoHide(autoHideSeconds));
    }

    private IEnumerator AutoHide(float seconds)
    {
        yield return new WaitForSeconds(seconds);
        Hide();
    }

    public void Hide()
    {
        if (hideRoutine != null) { StopCoroutine(hideRoutine); hideRoutine = null; }
        if (popupRoot != null) popupRoot.SetActive(false);
    }

    private string BuildTechDetails(TechData tech)
    {
        var sb = new StringBuilder();
        if (tech == null) return string.Empty;

        // Flat bonuses
        if (tech.flatFoodBonus != 0) sb.AppendLine($"+{tech.flatFoodBonus} Food / turn");
        if (tech.flatProductionBonus != 0) sb.AppendLine($"+{tech.flatProductionBonus} Production / turn");
        if (tech.flatGoldBonus != 0) sb.AppendLine($"+{tech.flatGoldBonus} Gold / turn");
        if (tech.flatScienceBonus != 0) sb.AppendLine($"+{tech.flatScienceBonus} Science / turn");
        if (tech.flatCultureBonus != 0) sb.AppendLine($"+{tech.flatCultureBonus} Culture / turn");
        if (tech.flatFaithBonus != 0) sb.AppendLine($"+{tech.flatFaithBonus} Faith / turn");

        // Percentage modifiers
        if (tech.foodModifier != 0f) sb.AppendLine($"Food: {tech.foodModifier:P0}");
        if (tech.productionModifier != 0f) sb.AppendLine($"Production: {tech.productionModifier:P0}");
        if (tech.goldModifier != 0f) sb.AppendLine($"Gold: {tech.goldModifier:P0}");
        if (tech.scienceModifier != 0f) sb.AppendLine($"Science: {tech.scienceModifier:P0}");
        if (tech.cultureModifier != 0f) sb.AppendLine($"Culture: {tech.cultureModifier:P0}");
        if (tech.faithModifier != 0f) sb.AppendLine($"Faith: {tech.faithModifier:P0}");

        // Unlocks
        if (tech.unlockedGovernments != null && tech.unlockedGovernments.Length > 0)
            sb.AppendLine($"Unlocks Governments: {tech.unlockedGovernments.Length}");
        if (tech.unlockedReligions != null && tech.unlockedReligions.Length > 0)
            sb.AppendLine($"Unlocks Religions: {tech.unlockedReligions.Length}");
        if (tech.unlockedLeaders != null && tech.unlockedLeaders.Length > 0)
            sb.AppendLine($"Unlocks Leaders: {tech.unlockedLeaders.Length}");
        if (tech.unlocksReligion) sb.AppendLine("Unlocks Religion mechanics");

        // Misc
        if (tech.cityCapIncrease != 0) sb.AppendLine($"+{tech.cityCapIncrease} City Capacity");
        if (tech.pantheonCapIncrease != 0) sb.AppendLine($"+{tech.pantheonCapIncrease} Pantheon Capacity");

        var details = sb.ToString().Trim();
        if (string.IsNullOrEmpty(details)) details = "(No immediate bonuses)";
        return details;
    }

    private string BuildCultureDetails(CultureData cult)
    {
        var sb = new StringBuilder();
        if (cult == null) return string.Empty;

        if (cult.flatFoodBonus != 0) sb.AppendLine($"+{cult.flatFoodBonus} Food / turn");
        if (cult.flatProductionBonus != 0) sb.AppendLine($"+{cult.flatProductionBonus} Production / turn");
        if (cult.flatGoldBonus != 0) sb.AppendLine($"+{cult.flatGoldBonus} Gold / turn");
        if (cult.flatScienceBonus != 0) sb.AppendLine($"+{cult.flatScienceBonus} Science / turn");
        if (cult.flatCultureBonus != 0) sb.AppendLine($"+{cult.flatCultureBonus} Culture / turn");
        if (cult.flatFaithBonus != 0) sb.AppendLine($"+{cult.flatFaithBonus} Faith / turn");

        if (cult.foodModifier != 0f) sb.AppendLine($"Food: {cult.foodModifier:P0}");
        if (cult.productionModifier != 0f) sb.AppendLine($"Production: {cult.productionModifier:P0}");
        if (cult.goldModifier != 0f) sb.AppendLine($"Gold: {cult.goldModifier:P0}");
        if (cult.scienceModifier != 0f) sb.AppendLine($"Science: {cult.scienceModifier:P0}");
        if (cult.cultureModifier != 0f) sb.AppendLine($"Culture: {cult.cultureModifier:P0}");
        if (cult.faithModifier != 0f) sb.AppendLine($"Faith: {cult.faithModifier:P0}");

        if (cult.unlocksPantheon) sb.AppendLine("Unlocks Pantheon mechanics");
        if (cult.unlocksReligion) sb.AppendLine("Unlocks Religion mechanics");
        if (cult.enablesGovernors) sb.AppendLine("Enables Governors");

        if (cult.additionalGovernorSlots != 0) sb.AppendLine($"+{cult.additionalGovernorSlots} Governor Slots");

        var details = sb.ToString().Trim();
        if (string.IsNullOrEmpty(details)) details = "(No immediate bonuses)";
        return details;
    }
}
