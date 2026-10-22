using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Mandatory decision shown after the player loses a national election.</summary>
public sealed class ElectionDefeatModalUI : MonoBehaviour
{
    [SerializeField] private GameObject modalRoot;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text explanationText;
    [SerializeField] private TMP_Text publicOpinionText;
    [SerializeField] private TMP_Text concedeConsequenceText;
    [SerializeField] private TMP_Text seizeConsequenceText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button concedeButton;
    [SerializeField] private Button seizeButton;

    private Civilization civilization;
    private bool resolving;

    private void Awake()
    {
        GovernmentUiUtil.SetClick(concedeButton, Concede);
        GovernmentUiUtil.SetClick(seizeButton, SeizePower);
        GovernmentUiUtil.SetActive(modalRoot, false);
    }

    private void OnEnable()
    {
        ElectionManager.PlayerElectionDefeatPending += Show;
        TryShowPending();
    }

    private void OnDisable()
    {
        ElectionManager.PlayerElectionDefeatPending -= Show;
    }

    private void Update()
    {
        if (modalRoot != null && !modalRoot.activeSelf) TryShowPending();
    }

    private void TryShowPending()
    {
        var player = PoliticalActionRules.FindPlayerCivilization();
        if (player?.electionState?.pendingPlayerDefeat == true) Show(player);
    }

    private void Show(Civilization civ)
    {
        if (civ?.electionState?.pendingPlayerDefeat != true) return;
        civilization = civ;
        resolving = false;
        var election = civ.electionState.history?.LastOrDefault(record => record != null
            && record.electionId == civ.electionState.pendingDefeatElectionId);
        float opinion = election != null ? election.publicOpinionAtElection : civ.electionState.publicApproval;
        GovernmentUiUtil.SetText(titleText, "Election Defeat");
        GovernmentUiUtil.SetText(explanationText, "The electorate has rejected your administration. You must concede power or overturn the result.");
        GovernmentUiUtil.SetText(publicOpinionText, $"Public Opinion: {opinion:0.##}% (50% required)");
        GovernmentUiUtil.SetText(concedeConsequenceText, "Concede Power: accept the result and end the campaign in defeat.");
        GovernmentUiUtil.SetText(seizeConsequenceText, "Seize Power: impose authoritarian rule and face a nationwide rebellion.");
        GovernmentUiUtil.SetText(statusText, string.Empty);
        GovernmentUiUtil.SetInteractable(concedeButton, true);
        GovernmentUiUtil.SetInteractable(seizeButton, true);
        GovernmentUiUtil.SetActive(modalRoot, true);
        if (GameManager.Instance != null) GameManager.Instance.gamePaused = true;
    }

    private void Concede()
    {
        if (resolving || civilization == null) return;
        resolving = true;
        GovernmentUiUtil.SetInteractable(concedeButton, false);
        GovernmentUiUtil.SetInteractable(seizeButton, false);
        ElectionManager.ConcedePlayerDefeat(civilization);
    }

    private void SeizePower()
    {
        if (resolving || civilization == null) return;
        resolving = true;
        GovernmentUiUtil.SetInteractable(concedeButton, false);
        GovernmentUiUtil.SetInteractable(seizeButton, false);
        if (ElectionManager.SeizePower(civilization))
        {
            GovernmentUiUtil.SetActive(modalRoot, false);
            return;
        }

        resolving = false;
        GovernmentUiUtil.SetText(statusText, "No valid emergency authoritarian government is configured.");
        GovernmentUiUtil.SetInteractable(concedeButton, true);
        GovernmentUiUtil.SetInteractable(seizeButton, true);
    }
}