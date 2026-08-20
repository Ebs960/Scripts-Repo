using System;
using UnityEngine;

public enum BattleInteractionMode { Selection, Movement, Attack, Deployment, Retreat, Embark, Disembark, Launch, Recovery, Depth, Detection }

/// <summary>Translates tactical gestures into BattleManager requests; it never edits simulation state.</summary>
public sealed class BattleInputController : MonoBehaviour
{
    private BattleManager manager;
    private BattleCameraController cameraController;
    public bool IsActive { get; private set; }
    public int SelectedUnitId { get; private set; } = -1;
    public int SelectedCellIndex { get; private set; } = -1;
    public BattleInteractionMode Mode { get; private set; }
    public BattleDomain DomainFilter { get; private set; } = BattleDomain.Land;
    public int SelectedWeaponIndex { get; private set; }
    public bool UseSpecialAttack { get; private set; }
    public bool IsCommandInputLocked { get; private set; }
    public event Action SelectionChanged;
    public event Action<string> RequestRejected;

    public void Bind(BattleManager battleManager, BattleCameraController camera)
    { manager = battleManager; cameraController = camera; }

    public void SetActive(bool active)
    { IsActive = active; if (!active) Cancel(); }

    public void SetMode(BattleInteractionMode mode) { Mode = mode; SelectionChanged?.Invoke(); }
    public void SetCommandInputLocked(bool locked) { IsCommandInputLocked = locked; }
    public void SetAttackSelection(int weaponIndex,bool special){SelectedWeaponIndex=Mathf.Max(0,weaponIndex);UseSpecialAttack=special;SelectionChanged?.Invoke();}
    public void SelectUnit(int unitId) { SelectedUnitId = unitId; SelectionChanged?.Invoke(); }

    public void SelectCell(int cellIndex)
    {
        if (!IsActive || IsCommandInputLocked || manager?.ActiveBattle == null) return;
        SelectedCellIndex = cellIndex;
        var clicked = manager.GetUnitAtCell(cellIndex);
        bool friendlyTargetMode = Mode == BattleInteractionMode.Embark || Mode == BattleInteractionMode.Recovery;
        if (!friendlyTargetMode && clicked != null && clicked.Side == manager.ActiveBattle.ActiveSide)
        {
            SelectedUnitId = clicked.UnitId;
            SelectionChanged?.Invoke();
            return;
        }
        bool ok = true; string reason = string.Empty;
        switch (Mode)
        {
            case BattleInteractionMode.Movement: ok = manager.TryMoveUnit(SelectedUnitId, cellIndex, out reason); break;
            case BattleInteractionMode.Attack:
                var attacker=manager.GetBattleUnit(SelectedUnitId);
                ok=clicked!=null&&(UseSpecialAttack&&attacker?.Snapshot?.SpecialAttackProfile!=null
                    ? manager.TryAttackUnitWithProfile(SelectedUnitId,clicked.UnitId,attacker.Snapshot.SpecialAttackProfile,out reason)
                    : manager.TryAttackUnitWithWeapon(SelectedUnitId,clicked.UnitId,SelectedWeaponIndex,out reason));
                break;
            case BattleInteractionMode.Deployment: ok = manager.TryDeployUnit(SelectedUnitId, cellIndex, out reason); break;
            case BattleInteractionMode.Retreat: ok = manager.TryRetreatUnit(SelectedUnitId, cellIndex, out reason); break;
            case BattleInteractionMode.Embark: ok = clicked != null && manager.TryEmbarkUnit(SelectedUnitId, clicked.UnitId, out reason); break;
            case BattleInteractionMode.Disembark: ok = manager.TryDisembarkFirstCargo(SelectedUnitId, cellIndex, out reason); break;
            case BattleInteractionMode.Launch: ok = manager.TryLaunchFirstAircraft(SelectedUnitId, cellIndex, out reason); break;
            case BattleInteractionMode.Recovery: ok = clicked != null && manager.TryRecoverAircraft(SelectedUnitId, clicked.UnitId, out reason); break;
            default:
                if (clicked != null && clicked.Side == manager.ActiveBattle.ActiveSide)
                    SelectedUnitId = clicked.UnitId;
                break;
        }
        if (!ok) RequestRejected?.Invoke(string.IsNullOrEmpty(reason) ? "invalid tactical selection" : reason);
        SelectionChanged?.Invoke();
    }

    public void ChangeDepth(BattleDepthBand depth)
    { string reason = "battle manager unavailable"; Submit(manager != null && manager.TryChangeDepth(SelectedUnitId, depth, out reason), reason); }
    public void ActiveScan()
    { string reason = "battle manager unavailable"; Submit(manager != null && manager.TryActiveDetection(SelectedUnitId, out reason), reason); }
    public void SwitchDomain(BattleDomain domain) { DomainFilter = domain; SelectionChanged?.Invoke(); }
    public void RestorePresentationState(BattleInteractionMode mode, int selectedUnitId, int selectedCellIndex, BattleDomain domain)
    { Mode=mode; SelectedUnitId=selectedUnitId; SelectedCellIndex=selectedCellIndex; DomainFilter=domain; SelectionChanged?.Invoke(); }
    public void Cancel() { Mode = BattleInteractionMode.Selection; SelectedCellIndex = -1; SelectionChanged?.Invoke(); }

    private void Update()
    {
        if (!IsActive) return;
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1)) Cancel();
        Vector2 pan = new(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        float rotate = (Input.GetKey(KeyCode.Q) ? -1f : 0f) + (Input.GetKey(KeyCode.E) ? 1f : 0f);
        cameraController?.RouteInput(pan, Input.mouseScrollDelta.y, rotate, Time.unscaledDeltaTime);
    }

    private void Submit(bool success, string reason)
    { if (!success) RequestRejected?.Invoke(string.IsNullOrEmpty(reason) ? "command rejected" : reason); SelectionChanged?.Invoke(); }
}
