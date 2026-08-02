using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BattleHUD : MonoBehaviour
{
    private enum CellActionMode { None, Retreat, Embark, Disembark, Launch, Recover }
    private BattleManager manager;
    private GameObject root;
    private TextMeshProUGUI status;
    private TextMeshProUGUI selected;
    private TextMeshProUGUI unitSelector;
    private TMP_InputField cellInput;
    private TextMeshProUGUI targetSelector;
    private int selectedUnitId = -1;
    private int selectedUnitIndex;
    private int selectedTargetIndex;
    private int selectedWeaponIndex;
    private int selectedReserveIndex;
    private BattlePresenter presenter;
    private CellActionMode cellActionMode;

    public static BattleHUD GetOrCreate(BattleManager manager)
    {
        var existing = manager.GetComponent<BattleHUD>();
        return existing != null ? existing : manager.gameObject.AddComponent<BattleHUD>();
    }

    public void Bind(BattleManager battleManager)
    {
        manager = battleManager;
        Build();
        presenter = BattlePresenter.GetOrCreate(battleManager);
        presenter.CellClicked += OnCellClicked;
        manager.BattleStarted += OnBattleStarted;
        manager.BattleStateChanged += OnBattleStateChanged;
        manager.BattlePreviewClosed += Refresh;
        root.SetActive(manager.ActiveBattle != null);
    }

    private void OnDestroy()
    {
        if (manager != null)
        {
            manager.BattleStarted -= OnBattleStarted;
            manager.BattleStateChanged -= OnBattleStateChanged;
            manager.BattlePreviewClosed -= Refresh;
        }
        if (presenter != null)
            presenter.CellClicked -= OnCellClicked;
    }

    public void Bind(BattleSession session)
    {
        Build();
        root.SetActive(session != null);
        Refresh();
    }

    private void OnBattleStarted(BattleSession session) => Bind(session);
    private void OnBattleStateChanged(BattleSession session) => Refresh();

    private void Build()
    {
        if (root != null)
            return;

        root = new GameObject("Battle HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 510;
        root.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        root.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1920f, 1080f);
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(root.transform, false);
        var panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0f, 0f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 0.5f);
        panelRect.sizeDelta = new Vector2(380f, 0f);
        panel.GetComponent<Image>().color = new Color(0.06f, 0.08f, 0.1f, 0.96f);

        status = CreateText(panel.transform, "Status", new Vector2(0f, 1f), new Vector2(16f, -16f), new Vector2(348f, 90f), 18f);
        selected = CreateText(panel.transform, "Selected", new Vector2(0f, 1f), new Vector2(16f, -115f), new Vector2(348f, 90f), 15f);
        unitSelector = CreateText(panel.transform, "Unit Selector", new Vector2(0f, 1f), new Vector2(16f, -215f), new Vector2(348f, 30f), 15f);
        CreateButton(panel.transform, "Prev Unit", new Vector2(16f, -250f), PreviousUnit, 112f);
        CreateButton(panel.transform, "Next Unit", new Vector2(142f, -250f), NextUnit, 112f);
        targetSelector = CreateText(panel.transform, "Target Selector", new Vector2(0f, 1f), new Vector2(16f, -295f), new Vector2(348f, 30f), 15f);
        CreateButton(panel.transform, "Prev Target", new Vector2(16f, -330f), PreviousTarget, 112f);
        CreateButton(panel.transform, "Next Target", new Vector2(142f, -330f), NextTarget, 112f);
        CreateButton(panel.transform, "Next Weapon", new Vector2(268f, -330f), NextWeapon, 96f);
        CreateButton(panel.transform, "Layer", new Vector2(268f, -250f), CycleLayer, 96f);
        CreateButton(panel.transform, "Zoom +", new Vector2(268f, -285f), ZoomIn, 46f);
        CreateButton(panel.transform, "Zoom -", new Vector2(318f, -285f), ZoomOut, 46f);
        cellInput = CreateInput(panel.transform, "Cell", new Vector2(16f, -375f));

        CreateButton(panel.transform, "Move", new Vector2(16f, -425f), Move);
        CreateButton(panel.transform, "Attack", new Vector2(142f, -425f), Attack);
        CreateButton(panel.transform, "Defend", new Vector2(268f, -425f), Defend);
        CreateButton(panel.transform, "Wait", new Vector2(16f, -475f), Wait);
        CreateButton(panel.transform, "End Unit", new Vector2(142f, -475f), EndUnit);
        CreateButton(panel.transform, "Retreat", new Vector2(268f, -475f), Retreat);
        CreateButton(panel.transform, "Confirm Deployment", new Vector2(16f, -525f), ConfirmDeployment, 230f);
        CreateButton(panel.transform, "End Side", new Vector2(252f, -525f), EndSide, 112f);
        CreateButton(panel.transform, "Embark", new Vector2(16f, -575f), Embark);
        CreateButton(panel.transform, "Disembark", new Vector2(142f, -575f), Disembark);
        CreateButton(panel.transform, "Launch", new Vector2(268f, -575f), Launch);
        CreateButton(panel.transform, "Recover", new Vector2(16f, -625f), Recover);
        CreateButton(panel.transform, "Dive", new Vector2(142f, -625f), Dive);
        CreateButton(panel.transform, "Shallow", new Vector2(268f, -625f), Shallow);
        CreateButton(panel.transform, "Active Scan", new Vector2(16f, -675f), ActiveScan, 112f);
        CreateButton(panel.transform, "Next Reserve", new Vector2(142f, -675f), NextReserve, 112f);
        CreateButton(panel.transform, "Deploy Reserve", new Vector2(268f, -675f), DeployReserve, 112f);
        root.SetActive(false);
    }

    private void Refresh()
    {
        if (manager == null || manager.ActiveBattle == null)
        {
            if (root != null) root.SetActive(false);
            return;
        }

        root.SetActive(true);
        var session = manager.ActiveBattle;
        status.text = $"{session.Theater}\nRound {session.CurrentRound} | {session.ActiveSide}\nPhase: {session.Phase}";
        var units = manager.GetUnitsForActiveSide();
        if (units.Count > 0)
        {
            selectedUnitIndex = Mathf.Clamp(selectedUnitIndex, 0, units.Count - 1);
            for (int i = 0; i < units.Count; i++)
                if (units[i].UnitId == selectedUnitId) selectedUnitIndex = i;
            selectedUnitId = units[selectedUnitIndex].UnitId;
            unitSelector.text = $"Unit: {units[selectedUnitIndex].UnitId} ({units[selectedUnitIndex].Domain}, HP {units[selectedUnitIndex].CurrentHealth})";
        }
        else { selectedUnitId = -1; unitSelector.text = "No active units"; }
        RefreshTargets();
        ShowSelected();
        RefreshBoardOverlays();
    }

    private void PreviousUnit()
    {
        var units = manager?.GetUnitsForActiveSide();
        if (units == null || units.Count == 0)
            return;
        selectedUnitIndex = (selectedUnitIndex - 1 + units.Count) % units.Count;
        selectedUnitId = units[selectedUnitIndex].UnitId;
        RefreshTargets();
        ShowSelected();
        RefreshBoardOverlays();
    }

    private void NextUnit()
    {
        var units = manager?.GetUnitsForActiveSide();
        if (units == null || units.Count == 0) return;
        selectedUnitIndex = (selectedUnitIndex + 1) % units.Count;
        selectedUnitId = units[selectedUnitIndex].UnitId;
        RefreshTargets();
        ShowSelected();
        RefreshBoardOverlays();
    }

    private void RefreshTargets()
    {
        var targets = manager?.GetVisibleEnemyUnits(selectedUnitId);
        if (targets == null || targets.Count == 0) { selectedTargetIndex = 0; targetSelector.text = "No detected targets"; return; }
        selectedTargetIndex = Mathf.Clamp(selectedTargetIndex, 0, targets.Count - 1);
        targetSelector.text = $"Target: {targets[selectedTargetIndex].UnitId} ({targets[selectedTargetIndex].Domain}, HP {targets[selectedTargetIndex].CurrentHealth})";
    }

    private void PreviousTarget() { CycleTarget(-1); }
    private void NextTarget() { CycleTarget(1); }
    private void CycleTarget(int direction)
    {
        var targets = manager?.GetVisibleEnemyUnits(selectedUnitId);
        if (targets == null || targets.Count == 0) return;
        selectedTargetIndex = (selectedTargetIndex + direction + targets.Count) % targets.Count;
        RefreshTargets();
        RefreshBoardOverlays();
    }

    private void NextWeapon()
    {
        var unit = FindSelectedUnit();
        int count = unit?.Snapshot?.Weapons?.Count ?? 0;
        if (count == 0) return;
        selectedWeaponIndex = (selectedWeaponIndex + 1) % count;
        ShowSelected();
        RefreshBoardOverlays();
    }

    private void OnCellClicked(int cellIndex)
    {
        if (manager?.ActiveBattle == null) return;
        var clickedUnit = presenter != null ? presenter.GetDisplayedUnitAtCell(cellIndex) : manager.GetUnitAtCell(cellIndex);
        if (cellActionMode != CellActionMode.None)
        {
            bool success; string actionReason;
            switch (cellActionMode)
            {
                case CellActionMode.Retreat: success = manager.TryRetreatUnit(selectedUnitId, cellIndex, out actionReason); break;
                case CellActionMode.Embark:
                    success = clickedUnit != null && manager.TryEmbarkUnit(selectedUnitId, clickedUnit.UnitId, out actionReason);
                    if (clickedUnit == null) actionReason = "select a friendly transport";
                    break;
                case CellActionMode.Disembark: success = manager.TryDisembarkFirstCargo(selectedUnitId, cellIndex, out actionReason); break;
                case CellActionMode.Launch: success = manager.TryLaunchFirstAircraft(selectedUnitId, cellIndex, out actionReason); break;
                case CellActionMode.Recover:
                    success = clickedUnit != null && manager.TryRecoverAircraft(selectedUnitId, clickedUnit.UnitId, out actionReason);
                    if (clickedUnit == null) actionReason = "select a friendly carrier";
                    break;
                default: success = false; actionReason = "no tactical action selected"; break;
            }
            if (success) cellActionMode = CellActionMode.None;
            Submit(success, actionReason); return;
        }
        if (clickedUnit != null && clickedUnit.Side == manager.ActiveBattle.ActiveSide)
        {
            var active = manager.GetUnitsForActiveSide();
            for (int i = 0; i < active.Count; i++)
                if (active[i].UnitId == clickedUnit.UnitId) { selectedUnitIndex = i; break; }
            selectedUnitId = clickedUnit.UnitId;
            RefreshTargets(); ShowSelected(); RefreshBoardOverlays();
            return;
        }

        if (selectedUnitId < 0) return;
        if (manager.ActiveBattle.Phase == BattlePhase.Deployment)
        {
            Submit(manager.TryDeployUnit(selectedUnitId, cellIndex, out string deployReason), deployReason);
            return;
        }
        if (clickedUnit != null)
        {
            var targets = manager.GetVisibleEnemyUnits(selectedUnitId);
            for (int i = 0; i < targets.Count; i++)
                if (targets[i].UnitId == clickedUnit.UnitId)
                {
                    selectedTargetIndex = i;
                    Submit(manager.TryAttackUnitWithWeapon(selectedUnitId, clickedUnit.UnitId, selectedWeaponIndex, out string attackReason), attackReason);
                    return;
                }
        }
        Submit(manager.TryMoveUnit(selectedUnitId, cellIndex, out string moveReason), moveReason);
    }

    private void CycleLayer() { presenter?.CycleLayer(); Notify($"Tactical layer: {presenter?.VisibleLayerName}"); }
    private void ZoomIn() => presenter?.AdjustZoom(.1f);
    private void ZoomOut() => presenter?.AdjustZoom(-.1f);

    private void RefreshBoardOverlays()
    {
        if (presenter == null || manager?.ActiveBattle == null) return;
        var moves = new System.Collections.Generic.List<int>();
        var attacks = new System.Collections.Generic.List<int>();
        var unit = FindSelectedUnit();
        if (unit != null && manager.ActiveBattle.Phase != BattlePhase.Deployment)
        {
            for (int cell = 0; cell < manager.ActiveBattle.Map.CellCount; cell++)
                if (cell != unit.CellIndex && manager.TryGetMovePath(unit.UnitId, cell, out _)) moves.Add(cell);
            var targets = manager.GetVisibleEnemyUnits(unit.UnitId);
            for (int i = 0; i < targets.Count; i++)
            {
                int distance = manager.ActiveBattle.MapDistance(unit.CellIndex, targets[i].CellIndex);
                var weapon = BattleTargetingService.GetWeapon(unit, selectedWeaponIndex);
                if (weapon != null && distance >= weapon.minimumRange && distance <= weapon.maximumRange
                    && (weapon.targetDomains & BattleDomainResolver.ToMask(targets[i].Domain)) != 0)
                    attacks.Add(targets[i].CellIndex);
            }
        }
        else if (unit != null)
        {
            for (int cell = 0; cell < manager.ActiveBattle.Map.CellCount; cell++)
            {
                var mapCell = manager.ActiveBattle.Map.GetCell(cell);
                if (mapCell != null && mapCell.DeploymentOwner == unit.Side && mapCell.Supports(unit.Domain)) moves.Add(cell);
            }
        }
        presenter.SetOverlays(unit?.CellIndex ?? -1, moves, attacks);
    }

    private void ShowSelected()
    {
        var unit = FindSelectedUnit();
        if (unit == null) { selected.text = "No unit selected"; return; }
        int weaponCount = unit.Snapshot?.Weapons?.Count ?? 0;
        selectedWeaponIndex = weaponCount > 0 ? Mathf.Clamp(selectedWeaponIndex, 0, weaponCount - 1) : 0;
        var weapon = weaponCount > 0 ? unit.Snapshot.Weapons[selectedWeaponIndex] : null;
        string weaponName = weapon?.equipment != null ? weapon.equipment.equipmentName : weapon != null ? "Built-in weapon" : "Unarmed";
        selected.text = $"Unit {unit.UnitId} | {unit.Domain}\nHP {unit.CurrentHealth}/{unit.Snapshot.MaximumHealth} | Move {unit.CurrentMovePoints} | AP {unit.CurrentActionPoints}\n" +
            $"Cell {unit.CellIndex} | {weaponName} [{selectedWeaponIndex + 1}/{Mathf.Max(1, weaponCount)}]";
    }

    private BattleUnitState FindSelectedUnit()
    {
        var units = manager?.GetUnitsForActiveSide();
        if (units == null) return null;
        for (int i = 0; i < units.Count; i++) if (units[i].UnitId == selectedUnitId) return units[i];
        return null;
    }

    private void Move()
    {
        if (int.TryParse(cellInput.text, out int cell))
            Submit(manager.TryMoveUnit(selectedUnitId, cell, out string reason), reason);
        else Notify("Enter a destination cell index.");
    }

    private void Attack()
    {
        var targets = manager?.GetVisibleEnemyUnits(selectedUnitId);
        if (targets == null || selectedTargetIndex < 0 || selectedTargetIndex >= targets.Count) { Notify("Select a detected target."); return; }
        Submit(manager.TryAttackUnitWithWeapon(selectedUnitId, targets[selectedTargetIndex].UnitId, selectedWeaponIndex, out string reason), reason);
    }

    private void Defend() { Submit(manager.TryDefendUnit(selectedUnitId, out string reason), reason); }
    private void Wait() { Submit(manager.TryWaitUnit(selectedUnitId, out string reason), reason); }
    private void EndUnit() { Submit(manager.EndUnitActivation(selectedUnitId, out string reason), reason); }
    private void Retreat()
    {
        cellActionMode = CellActionMode.Retreat; Notify("Select a highlighted friendly battlefield-edge exit.");
    }
    private void Embark()
    {
        cellActionMode = CellActionMode.Embark; Notify("Select an adjacent friendly transport.");
    }
    private void Disembark()
    {
        cellActionMode = CellActionMode.Disembark; Notify("Select a beach, port, or valid adjacent destination.");
    }
    private void Launch()
    {
        cellActionMode = CellActionMode.Launch; Notify("Select an adjacent aircraft launch cell.");
    }
    private void Recover()
    {
        cellActionMode = CellActionMode.Recover; Notify("Select an adjacent friendly carrier.");
    }
    private void Dive() { Submit(manager.TryChangeDepth(selectedUnitId, BattleDepthBand.Deep, out string reason), reason); }
    private void Shallow() { Submit(manager.TryChangeDepth(selectedUnitId, BattleDepthBand.Shallow, out string reason), reason); }
    private void ActiveScan() { Submit(manager.TryActiveDetection(selectedUnitId, out string reason), reason); }
    private void NextReserve()
    {
        if (manager?.ActiveBattle == null) return;
        var reserves = manager.GetDeploymentReserves(manager.ActiveBattle.ActiveSide);
        if (reserves.Count == 0) { Notify("No deployment reserves."); return; }
        selectedReserveIndex = (selectedReserveIndex + 1) % reserves.Count;
        Notify($"Reserve: {reserves[selectedReserveIndex].Snapshot?.SourceUnit?.UnitName ?? $"Unit {reserves[selectedReserveIndex].UnitId}"}");
    }
    private void DeployReserve()
    {
        if (manager?.ActiveBattle == null) return;
        var reserves = manager.GetDeploymentReserves(manager.ActiveBattle.ActiveSide);
        if (reserves.Count == 0) { Notify("No deployment reserves."); return; }
        selectedReserveIndex = Mathf.Clamp(selectedReserveIndex, 0, reserves.Count - 1);
        Submit(manager.TrySwapDeploymentReserve(selectedUnitId, reserves[selectedReserveIndex].UnitId, out string reason), reason);
    }
    private void ConfirmDeployment() { Submit(manager.ConfirmDeployment(out string reason), reason); }
    private void EndSide() { manager?.EndPlayerSideTurn(); Refresh(); }

    private void Submit(bool success, string reason)
    {
        if (!success) Notify(reason);
        Refresh();
    }
    private static void Notify(string text) => UIManager.Instance?.ShowNotification(text);

    private static TextMeshProUGUI CreateText(Transform parent, string name, Vector2 anchor, Vector2 position, Vector2 size, float fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = anchor; rect.pivot = anchor; rect.anchoredPosition = position; rect.sizeDelta = size;
        var text = go.GetComponent<TextMeshProUGUI>(); text.font = TMP_Settings.defaultFontAsset; text.fontSize = fontSize; text.color = Color.white; return text;
    }

    private static TMP_InputField CreateInput(Transform parent, string label, Vector2 position)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(TMP_InputField)); go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = position; rect.sizeDelta = new Vector2(348f, 36f);
        go.GetComponent<Image>().color = new Color(0.2f, 0.25f, 0.3f, 1f);
        var text = CreateText(go.transform, "Text", new Vector2(0f, 0.5f), new Vector2(10f, 0f), new Vector2(320f, 30f), 15f); text.alignment = TextAlignmentOptions.Left;
        var input = go.GetComponent<TMP_InputField>(); input.textComponent = text; input.contentType = TMP_InputField.ContentType.IntegerNumber; input.placeholder = text; text.text = "Cell index"; return input;
    }

    private static void CreateButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action, float width = 112f)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = position; rect.sizeDelta = new Vector2(width, 36f);
        go.GetComponent<Image>().color = new Color(0.72f, 0.67f, 0.52f, 1f); go.GetComponent<Button>().onClick.AddListener(action);
        var text = CreateText(go.transform, "Label", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width - 8f, 30f), 13f); text.alignment = TextAlignmentOptions.Center; text.color = Color.black; text.text = label;
    }
}
