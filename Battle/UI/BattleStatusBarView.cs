using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>View for the top tactical status bar. All values are pushed by BattleHUD from live battle state.</summary>
public sealed class BattleStatusBarView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI attackerName;
    [SerializeField] private TextMeshProUGUI attackerUnitCount;
    [SerializeField] private Image attackerEmblem;
    [SerializeField] private BattleHoverTip attackerTip;
    [SerializeField] private TextMeshProUGUI roundText;
    [SerializeField] private TextMeshProUGUI phaseText;
    [SerializeField] private TextMeshProUGUI activeSideText;
    [SerializeField] private TextMeshProUGUI defenderName;
    [SerializeField] private TextMeshProUGUI defenderUnitCount;
    [SerializeField] private Image defenderEmblem;
    [SerializeField] private BattleHoverTip defenderTip;

    public void Initialize(BattleTooltipView tooltip)
    {
        if (attackerTip != null) attackerTip.Initialize(tooltip);
        if (defenderTip != null) defenderTip.Initialize(tooltip);
    }

    public void SetSide(BattleSide side, string civName, Sprite emblem, string unitCount, string breakdown)
    {
        bool attacker = side == BattleSide.Attacker;
        (attacker ? attackerName : defenderName).text = civName;
        (attacker ? attackerUnitCount : defenderUnitCount).text = unitCount;
        var image = attacker ? attackerEmblem : defenderEmblem;
        image.sprite = emblem;
        image.enabled = emblem != null;
        (attacker ? attackerTip : defenderTip).SetText(breakdown);
    }

    public void SetCenter(string round, string phase, string activeSide)
    {
        roundText.text = round;
        phaseText.text = phase;
        activeSideText.text = activeSide;
    }
}
