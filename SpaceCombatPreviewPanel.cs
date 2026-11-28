using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Presentation and confirmation layer for a direct space attack. Every number shown comes from the
/// SpaceCombatPreview that SpaceCombatManager built; this panel performs no combat arithmetic.
/// </summary>
public class SpaceCombatPreviewPanel : MonoBehaviour
{
    [SerializeField] private Image attackerIcon;
    [SerializeField] private TextMeshProUGUI attackerNameText;
    [SerializeField] private TextMeshProUGUI attackerDetailText;
    [SerializeField] private Image defenderIcon;
    [SerializeField] private TextMeshProUGUI defenderNameText;
    [SerializeField] private TextMeshProUGUI defenderDetailText;
    [SerializeField] private TextMeshProUGUI summaryText;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    private const int MaxAdditionalLines = 3;

    private readonly StringBuilder text = new StringBuilder();
    private Action onConfirm;
    private Action onCancel;
    private bool wired;

    public bool IsOpen => gameObject.activeSelf;
    public bool IsConfigured => summaryText != null && confirmButton != null && cancelButton != null;

    public void Show(CombatUnit attacker, CombatUnit target, SpaceCombatPreview preview, Action confirm, Action cancel)
    {
        if (!wired)
        {
            wired = true;
            confirmButton.onClick.AddListener(() => Resolve(true));
            cancelButton.onClick.AddListener(() => Resolve(false));
        }
        onConfirm = confirm;
        onCancel = cancel;

        BindUnit(attacker, attackerIcon, attackerNameText, attackerDetailText, SpaceMapSelectionInfo.Label("Space Attack", attacker.CurrentSpaceAttack.ToString()));
        BindUnit(target, defenderIcon, defenderNameText, defenderDetailText, SpaceMapSelectionInfo.Label("Defense", target.CurrentDefense.ToString()));

        text.Clear();
        int additional = 0, hidden = 0;
        foreach (var hit in preview.affectedUnits)
        {
            if (hit == null) continue;
            if (hit.isPrimaryTarget)
            {
                text.Insert(0, SpaceMapSelectionInfo.Label("Predicted damage", $"{hit.predictedDamage} to {SpaceMapSelectionInfo.UnitName(target)}") + "\n");
                continue;
            }
            if (additional >= MaxAdditionalLines) { hidden++; continue; }
            additional++;
            string victim = SpaceMapSelectionInfo.UnitName(SpaceMapSelectionInfo.ResolveUnit(hit.unitId));
            text.Append(SpaceMapSelectionInfo.Label(hit.isFriendlyFire ? "Friendly fire" : "Also hit", $"{victim}, {hit.predictedDamage} damage")).Append('\n');
        }
        if (hidden > 0) text.Append($"and {hidden} more in the blast\n");
        text.Append(preview.targetMayCounterAttack
            ? SpaceMapSelectionInfo.Label("Counterattack", $"{preview.predictedCounterDamage} damage to {SpaceMapSelectionInfo.UnitName(attacker)}")
            : SpaceMapSelectionInfo.Label("Counterattack", "None expected"));
        summaryText.text = text.ToString();

        confirmButton.interactable = true;
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        onConfirm = null;
        onCancel = null;
        if (gameObject.activeSelf) gameObject.SetActive(false);
    }

    // Each choice is taken once: the callbacks are cleared before either runs, so a double click cannot attack twice.
    private void Resolve(bool confirmed)
    {
        Action callback = confirmed ? onConfirm : onCancel;
        onConfirm = null;
        onCancel = null;
        callback?.Invoke();
    }

    private static void BindUnit(CombatUnit unit, Image icon, TextMeshProUGUI nameText, TextMeshProUGUI detailText, string statLine)
    {
        Sprite sprite = SpaceMapSelectionInfo.UnitIcon(unit);
        if (icon != null) { icon.sprite = sprite; icon.enabled = sprite != null; }
        if (nameText != null) nameText.text = SpaceMapSelectionInfo.UnitName(unit);
        if (detailText != null)
            detailText.text = SpaceMapSelectionInfo.Label("Owner", SpaceMapSelectionInfo.CivilizationName(unit.owner)) + "\n"
                + SpaceMapSelectionInfo.Label("Health", $"{Mathf.Max(0, unit.currentHealth)}/{unit.MaxHealth}") + "\n" + statLine;
    }
}
