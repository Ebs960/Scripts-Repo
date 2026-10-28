using UnityEngine;

public sealed class BattleResultUI : MonoBehaviour
{
    private const string PanelResource = BattleUIUtil.ResourceFolder + "BattleResultPanel";

    private BattleManager manager;
    private BattleResultPanel panel;
    private BattleTooltipView modalTooltip;

    public static BattleResultUI GetOrCreate(BattleManager manager)
    {
        var existing = manager.GetComponent<BattleResultUI>();
        return existing != null ? existing : manager.gameObject.AddComponent<BattleResultUI>();
    }

    public void Bind(BattleManager battleManager)
    {
        if (manager == battleManager) return;
        if (manager != null) manager.BattleResolved -= Show;
        manager = battleManager;
        if (manager == null) return;
        manager.BattleResolved += Show;
        Build();
    }

    private void OnDestroy()
    {
        if (manager != null)
            manager.BattleResolved -= Show;
    }

    private void Build()
    {
        if (panel != null) return;
        var prefab = Resources.Load<BattleResultPanel>(PanelResource);
        if (prefab == null)
        {
            Debug.LogError($"[BattleResultUI] Resources/{PanelResource}.prefab is missing. Run Tools > Battle UI > Build Tactical Battle Prefabs.");
            return;
        }
        panel = Instantiate(prefab, transform, false);
        panel.name = "Battle Result";
        panel.ContinueButton.onClick.AddListener(Continue);
        modalTooltip = panel.GetComponentInChildren<BattleTooltipView>(true)
            ?? manager.GetComponentInChildren<BattleHUD>(true)?.CreateTooltipOverlay((RectTransform)panel.transform);
        var continueTip = panel.ContinueButton.GetComponent<BattleHoverTip>();
        if (continueTip == null) continueTip = panel.ContinueButton.gameObject.AddComponent<BattleHoverTip>();
        continueTip.Initialize(modalTooltip);
        continueTip.SetText("Continue: Return to the campaign.");
        panel.gameObject.SetActive(false);
    }

    private void Show(BattleResult result)
    {
        // Background AI-vs-AI engagements publish campaign events but must not
        // interrupt the player with a tactical result modal.
        var preview = manager != null ? manager.PendingPreview : null;
        bool playerInvolved = result != null && result.WasPlayerInvolved || preview != null
            && ((preview.Attacker != null && preview.Attacker.owner != null && preview.Attacker.owner.isPlayerControlled)
                || (preview.Defender != null && preview.Defender.owner != null && preview.Defender.owner.isPlayerControlled));
        if (!playerInvolved)
            return;

        Build();
        if (panel == null) return;

        // Counts are whole tactical units, not individual soldier casualties.
        var attacker = new int[3];
        var defender = new int[3];
        for (int i = 0; i < result.UnitOutcomes.Count; i++)
        {
            var outcome = result.UnitOutcomes[i];
            var counts = outcome.Side == BattleSide.Attacker ? attacker : defender;
            if (outcome.Died) counts[0]++;
            else if (outcome.Retreated) counts[1]++;
            else counts[2]++;
        }

        bool attackerHuman = manager != null && manager.IsHumanControlledSide(BattleSide.Attacker);
        bool defenderHuman = manager != null && manager.IsHumanControlledSide(BattleSide.Defender);
        string title = "BATTLE CONCLUDED";
        Color color = new Color(0.93f, 0.76f, 0.28f);
        if (attackerHuman != defenderHuman)
        {
            bool won = result.WinningSide == (attackerHuman ? BattleSide.Attacker : BattleSide.Defender);
            title = won ? "VICTORY" : "DEFEAT";
            color = won ? new Color(0.93f, 0.76f, 0.28f) : new Color(0.95f, 0.4f, 0.35f);
        }

        string attackerCiv = BattleUIUtil.CivName(BattleUIUtil.SideOwner(preview, BattleSide.Attacker));
        string defenderCiv = BattleUIUtil.CivName(BattleUIUtil.SideOwner(preview, BattleSide.Defender));
        string winnerCiv = result.WinningSide == BattleSide.Attacker ? attackerCiv : defenderCiv;
        string resolution = BattleUIUtil.Humanize(result.ResolutionType.ToString()) + (result.WasAutoResolved ? " (auto-resolved)" : string.Empty);
        string commanderExperience = CommanderExperienceSummary(result, preview);
        if (!string.IsNullOrEmpty(commanderExperience)) resolution += $"\n{commanderExperience}";

        panel.Show(title, color, $"Winner: {result.WinningSide} - {winnerCiv}", $"Resolution: {resolution}",
            $"Final round: {result.FinalRound}", attackerCiv, attacker, defenderCiv, defender);
        panel.gameObject.SetActive(true);
    }

    private static string CommanderExperienceSummary(BattleResult result, EngagementPreview preview)
    {
        int attackerExperience = 0;
        int defenderExperience = 0;
        bool found = false;
        for (int i = 0; i < result.CommanderOutcomes.Count; i++)
        {
            var outcome = result.CommanderOutcomes[i];
            if (outcome == null || outcome.ExperienceGained <= 0 || !TryGetFormationSide(preview, outcome.FormationId, out var side))
                continue;

            found = true;
            if (side == BattleSide.Attacker) attackerExperience += outcome.ExperienceGained;
            else defenderExperience += outcome.ExperienceGained;
        }

        return found
            ? $"Commander experience: Attacker +{attackerExperience:N0} XP; Defender +{defenderExperience:N0} XP"
            : string.Empty;
    }

    private static bool TryGetFormationSide(EngagementPreview preview, string formationId, out BattleSide side)
    {
        side = BattleSide.Attacker;
        if (preview == null || string.IsNullOrEmpty(formationId)) return false;
        for (int i = 0; i < preview.AttackerUnits.Count; i++)
            if (preview.AttackerUnits[i].FormationId == formationId) return true;
        for (int i = 0; i < preview.DefenderUnits.Count; i++)
            if (preview.DefenderUnits[i].FormationId == formationId)
            {
                side = BattleSide.Defender;
                return true;
            }
        return false;
    }

    public void PresentRestored(BattleResult result) => Show(result);

    private void Continue()
    {
        manager?.ContinueAfterBattleResult();
        if (panel != null) panel.gameObject.SetActive(false);
    }
}
