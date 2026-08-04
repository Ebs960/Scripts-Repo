using System.Collections.Generic;
using UnityEngine;

/// <summary>Single presentation owner for authority-derived cell highlights.</summary>
public sealed class BattleOverlayRenderer : MonoBehaviour
{
    private BattleManager manager;
    private BattlePresenter presenter;
    private BattleInputController input;

    public void Bind(BattleManager battleManager, BattlePresenter battlePresenter, BattleInputController controller)
    {
        manager = battleManager; presenter = battlePresenter; input = controller;
        if (input != null) input.SelectionChanged += Refresh;
        if (manager != null) manager.BattleStateChanged += OnStateChanged;
    }

    public void RenderMap(BattleMap map) { if (map == null) Clear(); else Refresh(); }
    public void Clear() => presenter?.SetOverlays(-1, null, null);

    private void OnDestroy()
    {
        if (input != null) input.SelectionChanged -= Refresh;
        if (manager != null) manager.BattleStateChanged -= OnStateChanged;
    }
    private void OnStateChanged(BattleSession _) => Refresh();

    private void Refresh()
    {
        if (manager?.ActiveBattle == null || presenter == null || input == null) { Clear(); return; }
        var moves = new List<int>(); var targets = new List<int>();
        var selected = manager.GetBattleUnit(input.SelectedUnitId);
        if (selected != null)
        {
            for (int i = 0; i < manager.ActiveBattle.Map.CellCount; i++)
                if (manager.IsLegalCellForMode(selected.UnitId, i, input.Mode))
                    (input.Mode == BattleInteractionMode.Attack ? targets : moves).Add(i);
        }
        presenter.SetOverlays(selected?.CellIndex ?? input.SelectedCellIndex, moves, targets);
    }
}
