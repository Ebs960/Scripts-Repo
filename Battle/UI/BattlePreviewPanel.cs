using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>View for the authored pre-battle confrontation prefab. BattlePreviewUI feeds it data and wires its buttons.</summary>
public sealed class BattlePreviewPanel : MonoBehaviour
{
    [SerializeField] private BattleConfrontationSideView attackerSide;
    [SerializeField] private BattleConfrontationSideView defenderSide;
    [SerializeField] private TextMeshProUGUI battlefieldText;
    [SerializeField] private TextMeshProUGUI oddsText;
    [SerializeField] private TextMeshProUGUI advantageText;
    [SerializeField] private TextMeshProUGUI lossesText;
    [SerializeField] private TextMeshProUGUI factorsText;
    [SerializeField] private TextMeshProUGUI commanderSelectorText;
    [SerializeField] private Button fightButton;
    [SerializeField] private Button autoResolveButton;
    [SerializeField] private Button retreatButton;
    [SerializeField] private Button cancelButton;
    [SerializeField] private Button previousCommanderButton;
    [SerializeField] private Button nextCommanderButton;
    [SerializeField] private Button roleButton;
    [SerializeField] private Button assignButton;

    public BattleConfrontationSideView AttackerSide => attackerSide;
    public BattleConfrontationSideView DefenderSide => defenderSide;
    public Button FightButton => fightButton;
    public Button AutoResolveButton => autoResolveButton;
    public Button RetreatButton => retreatButton;
    public Button CancelButton => cancelButton;
    public Button PreviousCommanderButton => previousCommanderButton;
    public Button NextCommanderButton => nextCommanderButton;
    public Button RoleButton => roleButton;
    public Button AssignButton => assignButton;

    public void SetBattlefield(string text) => battlefieldText.text = text;
    public void SetCommanderSelector(string text) => commanderSelectorText.text = text;

    public void SetForecastPending()
    {
        oddsText.text = "Calculating...";
        advantageText.text = string.Empty;
        lossesText.text = string.Empty;
        factorsText.text = string.Empty;
    }

    public void SetForecast(string odds, string advantage, string losses, string factors)
    {
        oddsText.text = odds;
        advantageText.text = advantage;
        lossesText.text = losses;
        factorsText.text = factors;
    }

    public void SetButtons(bool manualBattle, bool retreat, bool cancelAllowed)
    {
        fightButton.interactable = manualBattle;
        SetTooltip(fightButton, manualBattle ? "Fight: Begin the tactical battle." : "Fight unavailable: Manual battles are not permitted for this engagement.");
        autoResolveButton.interactable = true;
        SetTooltip(autoResolveButton, "Auto-Resolve: Resolve this engagement automatically.");
        retreatButton.interactable = retreat;
        SetTooltip(retreatButton, retreat ? "Retreat: Withdraw before the battle begins." : "Retreat unavailable: Pre-battle withdrawal is not permitted.");
        cancelButton.gameObject.SetActive(cancelAllowed);
        cancelButton.interactable = cancelAllowed;
        SetTooltip(cancelButton, cancelAllowed ? "Back: Return to the campaign." : "Back unavailable: This engagement cannot be cancelled.");
    }

    private static void SetTooltip(Button button, string text)
    {
        var tip = button != null ? button.GetComponent<BattleHoverTip>() : null;
        if (tip != null) tip.SetText(text);
    }
}
