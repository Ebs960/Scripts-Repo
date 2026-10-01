using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EquipmentSlotUI : MonoBehaviour
{
    [SerializeField] private EquipmentType slotType;
    [SerializeField] private bool projectileSlot;
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text itemName;
    [SerializeField] private GameObject selectedState;
    [SerializeField] private Button button;
    public EquipmentType SlotType => slotType;
    public bool IsProjectileSlot => projectileSlot;
    public event Action<EquipmentSlotUI> Clicked;
    private void Awake() { if (button != null) button.onClick.AddListener(Click); }
    private void OnDestroy() { if (button != null) button.onClick.RemoveListener(Click); }
    private void Click() => Clicked?.Invoke(this);
    public void Bind(EquipmentData item, bool configured, bool selected)
    {
        if (icon != null) { icon.sprite = item != null ? item.icon : null; icon.enabled = item != null && item.icon != null; }
        if (itemName != null) itemName.text = configured ? (item != null ? item.equipmentName : "None") : "Not configured";
        if (selectedState != null) selectedState.SetActive(selected);
    }
    public void SetInteractable(bool value) { if (button != null) button.interactable = value; }
}
