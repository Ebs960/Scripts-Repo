using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>View for the authored battle-result prefab.</summary>
public sealed class BattleResultPanel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI winnerText;
    [SerializeField] private TextMeshProUGUI resolutionText;
    [SerializeField] private TextMeshProUGUI roundText;
    [SerializeField] private TextMeshProUGUI attackerName;
    [SerializeField] private TextMeshProUGUI attackerLost;
    [SerializeField] private TextMeshProUGUI attackerRetreated;
    [SerializeField] private TextMeshProUGUI attackerSurvivors;
    [SerializeField] private TextMeshProUGUI defenderName;
    [SerializeField] private TextMeshProUGUI defenderLost;
    [SerializeField] private TextMeshProUGUI defenderRetreated;
    [SerializeField] private TextMeshProUGUI defenderSurvivors;
    [SerializeField] private Button continueButton;

    public Button ContinueButton => continueButton;

    public void Show(string title, Color titleColor, string winner, string resolution, string round,
        string attackerCiv, int[] attackerCounts, string defenderCiv, int[] defenderCounts)
    {
        titleText.text = title;
        titleText.color = titleColor;
        winnerText.text = winner;
        resolutionText.text = resolution;
        roundText.text = round;
        attackerName.text = attackerCiv;
        attackerLost.text = attackerCounts[0].ToString();
        attackerRetreated.text = attackerCounts[1].ToString();
        attackerSurvivors.text = attackerCounts[2].ToString();
        defenderName.text = defenderCiv;
        defenderLost.text = defenderCounts[0].ToString();
        defenderRetreated.text = defenderCounts[1].ToString();
        defenderSurvivors.text = defenderCounts[2].ToString();
    }
}
