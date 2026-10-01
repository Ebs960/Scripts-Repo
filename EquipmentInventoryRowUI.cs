using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EquipmentInventoryRowUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text equipmentName;
    [SerializeField] private TMP_Text stockCount;
    [SerializeField] private TMP_Text rarity;
    [SerializeField] private TMP_Text modifierSummary;
    [SerializeField] private GameObject selectedState;
    [SerializeField] private Button button;
    private EquipmentData item;
    public event Action<EquipmentData> Clicked;
    private void Awake() { if (button != null) button.onClick.AddListener(OnClick); }
    private void OnDestroy() { if (button != null) button.onClick.RemoveListener(OnClick); }
    private void OnClick() => Clicked?.Invoke(item);
    public void Bind(EquipmentData value, int count, bool selected, string summary)
    {
        item = value;
        if (icon != null) { icon.sprite = value != null ? value.icon : null; icon.enabled = value != null && value.icon != null; }
        if (equipmentName != null) equipmentName.text = value != null ? value.equipmentName : "None";
        if (stockCount != null) stockCount.text = value != null ? count.ToString() : string.Empty;
        if (rarity != null) rarity.text = value != null ? value.rarity.ToString() : string.Empty;
        if (modifierSummary != null) modifierSummary.text = summary ?? string.Empty;
        if (selectedState != null) selectedState.SetActive(selected);
    }
}
