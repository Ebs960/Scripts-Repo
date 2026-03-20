using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;

// World-space herd label: simple camera-facing label with optional tooltip and icon
public class HerdLabel : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("Parts (optional)")]
    [SerializeField] private TextMeshProUGUI herdNameText;
    [SerializeField] private TextMeshProUGUI ownerNameText;
    [SerializeField] private Image ownerIconImage;
    [SerializeField] private GameObject tooltipRoot;
    [SerializeField] private TextMeshProUGUI tooltipText;

    [SerializeField] private Vector3 offset = new Vector3(0f, 1.2f, 0f);

    private Transform target;
    private Camera mainCam;

    private string cachedHerdName;
    private string cachedOwnerName;

    public void Initialize(Transform targetTransform, string herdName, string ownerName, Sprite ownerIcon = null, string tooltip = null)
    {
        target = targetTransform;
        mainCam = Camera.main;
        cachedHerdName = herdName;
        cachedOwnerName = ownerName;

        if (herdNameText != null) herdNameText.text = herdName;
        if (ownerNameText != null) ownerNameText.text = ownerName;
        if (ownerIconImage != null) ownerIconImage.sprite = ownerIcon;
        if (tooltipRoot != null) tooltipRoot.SetActive(false);
        if (tooltipText != null && tooltip != null) tooltipText.text = tooltip;
    }

    public void UpdateLabel(string herdName, string ownerName, Sprite ownerIcon = null, string tooltip = null)
    {
        cachedHerdName = herdName;
        cachedOwnerName = ownerName;
        if (herdNameText != null) herdNameText.text = herdName;
        if (ownerNameText != null) ownerNameText.text = ownerName;
        if (ownerIconImage != null) ownerIconImage.sprite = ownerIcon;
        if (tooltipText != null && tooltip != null) tooltipText.text = tooltip;
    }

    void LateUpdate()
    {
        if (target == null) return;
        if (mainCam == null) mainCam = Camera.main;
        if (mainCam == null) return;
        transform.position = target.position + offset;
        transform.rotation = mainCam.transform.rotation;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (tooltipRoot != null)
        {
            tooltipRoot.SetActive(true);
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (tooltipRoot != null)
            tooltipRoot.SetActive(false);
    }

    /// <summary>
    /// Explicitly set icon sprite at runtime.
    /// </summary>
    public void SetIcon(Sprite s)
    {
        if (ownerIconImage == null) return;
        ownerIconImage.sprite = s;
    }
}
