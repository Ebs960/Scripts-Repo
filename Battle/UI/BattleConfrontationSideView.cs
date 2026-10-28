using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>One side column of the pre-battle confrontation screen.</summary>
public sealed class BattleConfrontationSideView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI sideLabel;
    [SerializeField] private TextMeshProUGUI civName;
    [SerializeField] private Image emblem;
    [SerializeField] private TextMeshProUGUI commander;
    [SerializeField] private TextMeshProUGUI soldiers;
    [SerializeField] private TextMeshProUGUI units;
    [SerializeField] private TextMeshProUGUI composition;
    [SerializeField] private TextMeshProUGUI reinforcements;

    public void Set(string side, string civ, Sprite emblemSprite, string commanderText, string soldierText, string unitText, string compositionText, string reinforcementText)
    {
        sideLabel.text = side;
        civName.text = civ;
        emblem.sprite = emblemSprite;
        emblem.enabled = emblemSprite != null;
        commander.text = commanderText;
        soldiers.text = soldierText;
        units.text = unitText;
        composition.text = compositionText;
        reinforcements.text = reinforcementText;
    }
}
