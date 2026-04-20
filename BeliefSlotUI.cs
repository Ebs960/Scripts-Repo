using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;

public class BeliefSlotUI : MonoBehaviour, IDropHandler
{
    public BeliefCategory category;
    public Image iconImage;
    public TextMeshProUGUI label;
    public TextMeshProUGUI effectSummary;
    public ReligionUI owner;
    public UnityEngine.UI.Button clearButton;

    public void Start()
    {
        if (label == null) label = GetComponentInChildren<TextMeshProUGUI>();
        if (iconImage == null) iconImage = GetComponentInChildren<Image>();
        if (clearButton != null)
        {
            clearButton.onClick.RemoveAllListeners();
            clearButton.onClick.AddListener(() => { owner?.ClearBeliefCategory(category); });
        }
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (eventData.pointerDrag == null) return;
        var btn = eventData.pointerDrag.GetComponent<BeliefButtonUI>();
        if (btn == null || btn.belief == null) return;

        // Notify owner to assign
        owner?.AssignBeliefToCategory(category, btn.belief);
    }

    public void SetAssigned(BeliefData belief)
    {
        if (label != null) label.text = belief != null ? belief.beliefName : "(empty)";
        if (iconImage != null) iconImage.sprite = belief != null ? belief.icon : null;
        if (effectSummary != null)
            effectSummary.text = belief != null ? BuildSummary(belief) : "";
        if (clearButton != null) clearButton.gameObject.SetActive(belief != null);
    }

    private string BuildSummary(BeliefData b)
    {
        if (b == null) return "";
        return ReligionUI.BuildBeliefEffectSummary(b, true);
    }
}
