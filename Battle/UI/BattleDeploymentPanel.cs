using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Deployment UI model. All changes are submitted to BattleManager.</summary>
public sealed class BattleDeploymentPanel : MonoBehaviour
{
    private BattleManager manager;
    public BattleSession Session { get; private set; }
    public int SelectedUnitId { get; private set; } = -1;
    public string ValidationMessage { get; private set; } = string.Empty;
    public int UnitLimit => manager != null ? manager.DeploymentUnitLimit : 0;
    public event Action Changed;

    public void Bind(BattleSession session)
    { Bind(BattleManager.Instance, session); }

    public void Bind(BattleManager battleManager, BattleSession session)
    { manager = battleManager; Session = session; SelectedUnitId = -1; ValidationMessage = string.Empty; Changed?.Invoke(); }

    public IReadOnlyList<BattleUnitState> Reserves
        => manager != null && Session != null ? manager.GetDeploymentReserves(Session.ActiveSide) : Array.Empty<BattleUnitState>();

    public void SelectUnit(int unitId) { SelectedUnitId = unitId; Changed?.Invoke(); }
    public bool MoveSelected(int cellIndex)
    { string reason = "battle manager unavailable"; return Complete(manager != null && manager.TryDeployUnit(SelectedUnitId, cellIndex, out reason), reason); }
    public bool SwapWithReserve(int reserveUnitId)
    { string reason = "battle manager unavailable"; return Complete(manager != null && manager.TrySwapDeploymentReserve(SelectedUnitId, reserveUnitId, out reason), reason); }
    public bool ResetSide()
    { string reason = "battle manager unavailable"; return Complete(manager != null && Session != null && manager.ResetDeployment(Session.ActiveSide, out reason), reason); }
    public bool AutoDeploySide() => ResetSide();
    public bool Confirm()
    { string reason = "battle manager unavailable"; return Complete(manager != null && manager.ConfirmDeployment(out reason), reason); }
    public void CancelAction() { SelectedUnitId = -1; ValidationMessage = string.Empty; Changed?.Invoke(); }

    private bool Complete(bool success, string reason)
    { ValidationMessage = success ? string.Empty : (string.IsNullOrEmpty(reason) ? "deployment request rejected" : reason); Changed?.Invoke(); return success; }
}
