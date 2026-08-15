using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Script for the transport unit button prefab.
/// This is attached to the button prefab used in the TransportUIManager.
/// </summary>
public class TransportUnitButton : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI unitNameText;
    [SerializeField] private Image unitIconImage;
    [SerializeField] private Button buttonComponent;
    
    private CombatUnit representedUnit;
    
    public void SetupButton(CombatUnit unit)
    {
        representedUnit = unit;
        
        UnitUIPresenter.Bind(unit, unitNameText, unitIconImage);
        
        // Set up button click handler
        if (buttonComponent != null)
        {
            buttonComponent.onClick.RemoveAllListeners();
            buttonComponent.onClick.AddListener(() => 
            {
                if (TransportUIManager.Instance != null)
                {
                    TransportUIManager.Instance.EnterDeployUnitMode(representedUnit);
                }
            });
        }
    }
}
