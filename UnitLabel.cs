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
            var combat = target.GetComponentInParent<CombatUnit>();
            if (combat != null && combat.data != null) icon = combat.data.icon;
            else
            {
                var worker = target.GetComponentInParent<WorkerUnit>();
                if (worker != null && worker.data != null) icon = worker.data.icon;
            }
        }

        if (iconImage != null)
        {
            if (icon != null) iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        // Initialize cached tooltip data and set tooltip inactive by default
        UpdateLabel(unitName, ownerName, currentHP, maxHP);
        if (tooltipRoot != null) tooltipRoot.SetActive(false);
    }

    public void UpdateLabel(string unitName, string ownerName, int currentHP, int maxHP)
    {
        cachedUnitName = unitName;
        cachedOwnerName = ownerName;
        cachedCurrentHP = currentHP;
        cachedMaxHP = maxHP;


        // Also update tooltip text if present
        if (tooltipText != null)
        {
            tooltipText.text = GetTooltipString();
        }
    }

    private string GetTooltipString()
    {
        return string.IsNullOrEmpty(cachedUnitName)
            ? "Unknown Unit"
            : $"{cachedUnitName}\nOwner: {cachedOwnerName}\nHP: {cachedCurrentHP}/{cachedMaxHP}";
    }

    void LateUpdate()
    {
        if (target == null) { Destroy(gameObject); return; }
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return; // Still no camera, can't do anything.

        // Follow the target
        transform.position = target.position + offset;

        // Make the label face the camera and remain upright relative to the camera's view.
        transform.rotation = mainCam.transform.rotation;
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