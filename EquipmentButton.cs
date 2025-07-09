using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class EquipmentButton : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI equipmentNameText;
    [SerializeField] private Image equipmentIcon;
    [SerializeField] private Button button;

    private EquipmentData equipmentData;
    private System.Action<EquipmentData> onEquipmentSelected;

    private void Awake()
    {
        if (button == null) button = GetComponent<Button>();
        button.onClick.AddListener(OnButtonClicked);
    }

    public void Setup(EquipmentData data, System.Action<EquipmentData> callback)
    {
        equipmentData = data;
        onEquipmentSelected = callback;
        
        equipmentNameText.text = data.equipmentName;
        if (data.icon != null)
        {
            equipmentIcon.sprite = data.icon;
            equipmentIcon.gameObject.SetActive(true);
        }
        else
        {
            equipmentIcon.gameObject.SetActive(false);
        }
    }

    private void OnButtonClicked()
    {
        onEquipmentSelected?.Invoke(equipmentData);
    }

    private void OnDestroy()
    {
        button.onClick.RemoveListener(OnButtonClicked);
    }
} 