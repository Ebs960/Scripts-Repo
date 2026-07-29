using UnityEngine;
using UnityEngine.EventSystems;

public class SimpleTooltipTarget : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private string title;
    private string description;

    public void Bind(string tooltipTitle, string tooltipDescription)
    {
        title = tooltipTitle;
        description = tooltipDescription;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!string.IsNullOrWhiteSpace(description)) TooltipSystem.Instance?.ShowSimpleTooltip(title, description);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        TooltipSystem.Instance?.RequestHideTooltip();
    }
}
