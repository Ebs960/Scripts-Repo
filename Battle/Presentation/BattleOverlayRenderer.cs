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
    public void Clear()
    { presenter?.SetOverlays(-1, null, null); presenter?.SetRichOverlays(null); }

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
        var states=new Dictionary<int,BattlePresenter.CellOverlay>();
        var selected = manager.GetBattleUnit(input.SelectedUnitId);
        if (selected != null)
        {
            for (int i = 0; i < manager.ActiveBattle.Map.CellCount; i++)
            {
                var cell=manager.ActiveBattle.Map.GetCell(i); var flags=BattlePresenter.CellOverlay.None;
                if(cell.IsObjective)flags|=BattlePresenter.CellOverlay.Objective;
                if(cell.IsReinforcementEntry)flags|=BattlePresenter.CellOverlay.Reinforcement;
                if(cell.RetreatExitForSide==selected.Side)flags|=BattlePresenter.CellOverlay.RetreatExit;
                if (manager.IsLegalCellForMode(selected.UnitId, i, input.Mode))
                    (input.Mode == BattleInteractionMode.Attack ? targets : moves).Add(i);
                else if(input.Mode!=BattleInteractionMode.Selection)flags|=BattlePresenter.CellOverlay.Invalid;
                var contact=manager.GetUnitAtCell(i);
                if(contact!=null&&contact.Side!=selected.Side)flags|=manager.GetDetectionLevel(selected.Side,contact) switch
                { BattleDetectionLevel.Suspected=>BattlePresenter.CellOverlay.Suspected, BattleDetectionLevel.Detected=>BattlePresenter.CellOverlay.Detected,
                    BattleDetectionLevel.Identified=>BattlePresenter.CellOverlay.Identified, _=>BattlePresenter.CellOverlay.None };
                if(flags!=BattlePresenter.CellOverlay.None)states[i]=flags;
            }
            if (input.Mode == BattleInteractionMode.Retreat
                && manager.TryGetRetreatPath(selected.UnitId, -1, out var route, out _))
                for (int i = 0; i < route.Count; i++) { if (!moves.Contains(route[i])) moves.Add(route[i]);
                    states[route[i]]=states.TryGetValue(route[i],out var existing)?existing|BattlePresenter.CellOverlay.RetreatPath:BattlePresenter.CellOverlay.RetreatPath; }
        }
        presenter.SetOverlays(selected?.CellIndex ?? input.SelectedCellIndex, moves, targets);
        presenter.SetRichOverlays(states);
    }
}
