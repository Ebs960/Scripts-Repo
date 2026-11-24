using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class EndgameScreenUI : MonoBehaviour
{
    [SerializeField] private Image illustration;
    [SerializeField] private AspectRatioFitter illustrationCover;
    [SerializeField] private Image conditionIcon;
    [SerializeField] private TMP_Text heading;
    [SerializeField] private TMP_Text subtitle;
    [SerializeField] private TMP_Text playerCivilization;
    [SerializeField] private TMP_Text playerLeader;
    [SerializeField] private Button mainMenuButton;
    [SerializeField] private Sprite dominationIcon;
    [SerializeField] private Sprite scienceIcon;
    [SerializeField] private Sprite cultureIcon;
    [SerializeField] private Color victoryColor = new Color(0.84f, 0.71f, 0.44f);
    [SerializeField] private Color defeatColor = new Color(0.77f, 0.80f, 0.83f);

    public void Present(VictoryResult result, CivData player, string leaderName)
    {
        Sprite art = player == null ? null : result.PlayerWon ? player.victoryIllustration : player.defeatIllustration;
        illustration.sprite = art;
        illustration.enabled = art != null;
        illustrationCover.enabled = art != null;
        if (art != null)
            illustrationCover.aspectRatio = art.rect.width / art.rect.height;

        heading.text = result.PlayerWon ? "VICTORY" : "DEFEAT";
        heading.color = result.PlayerWon ? victoryColor : defeatColor;
        subtitle.text = !result.Type.HasValue
            ? string.IsNullOrWhiteSpace(result.Reason) ? "Match Concluded" : result.Reason
            : !result.PlayerWon && !string.IsNullOrWhiteSpace(result.WinnerCivName)
                ? $"{result.TypeDisplayName} — {result.WinnerCivName}"
                : result.TypeDisplayName;
        conditionIcon.sprite = result.Type switch
        {
            VictoryType.Domination => dominationIcon,
            VictoryType.Science => scienceIcon,
            VictoryType.Culture => cultureIcon,
            _ => null
        };
        conditionIcon.enabled = conditionIcon.sprite != null;
        playerCivilization.text = player != null ? player.civName : string.Empty;
        playerLeader.text = leaderName ?? string.Empty;
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(mainMenuButton.gameObject);
    }

    public void ReturnToMainMenu()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.EndGame();
    }
}
