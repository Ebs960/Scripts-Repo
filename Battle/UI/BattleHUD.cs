using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BattleHUD : MonoBehaviour
{
    private enum AttackMode { Melee, Ranged, Special }
    private BattleManager manager;
    private GameObject root;
    private TextMeshProUGUI status;
    private TextMeshProUGUI selected;
    private TextMeshProUGUI unitSelector;
    private TextMeshProUGUI targetSelector;
    private int selectedUnitId = -1;
    private int selectedUnitIndex;
    private int selectedTargetIndex;
    private int selectedWeaponIndex;
    private int selectedReserveIndex;
    private AttackMode selectedAttackMode = AttackMode.Melee;
    private Button meleeAttackButton;
    private Button rangedAttackButton;
    private Button specialAttackButton;
    private BattlePresenter presenter;

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
        // Direct cell entry was a development aid. Battlefield actions are mouse-driven.

        meleeAttackButton = CreateButton(panel.transform, "Melee", new Vector2(16f, -420f), SetMeleeMode, 96f);
        rangedAttackButton = CreateButton(panel.transform, "Ranged", new Vector2(122f, -420f), SetRangedMode, 96f);
        specialAttackButton = CreateButton(panel.transform, "Special", new Vector2(228f, -420f), SetSpecialMode, 96f);
        specialAttackButton.interactable = false;

        CreateButton(panel.transform, "Move", new Vector2(16f, -470f), Move);
        CreateButton(panel.transform, "Attack", new Vector2(142f, -470f), Attack);
        CreateButton(panel.transform, "Defend", new Vector2(268f, -470f), Defend);
        CreateButton(panel.transform, "Wait", new Vector2(16f, -520f), Wait);
        CreateButton(panel.transform, "End Unit", new Vector2(142f, -520f), EndUnit);
        CreateButton(panel.transform, "Retreat", new Vector2(268f, -520f), Retreat);
        CreateButton(panel.transform, "Confirm Deployment", new Vector2(16f, -570f), ConfirmDeployment, 230f);
        CreateButton(panel.transform, "End Side", new Vector2(252f, -570f), EndSide, 112f);
        CreateButton(panel.transform, "Embark", new Vector2(16f, -620f), Embark);
        CreateButton(panel.transform, "Disembark", new Vector2(142f, -620f), Disembark);
        CreateButton(panel.transform, "Launch", new Vector2(268f, -620f), Launch);
        CreateButton(panel.transform, "Recover", new Vector2(16f, -670f), Recover);
        CreateButton(panel.transform, "Dive", new Vector2(142f, -670f), Dive);
        CreateButton(panel.transform, "Shallow", new Vector2(268f, -670f), Shallow);
        CreateButton(panel.transform, "Active Scan", new Vector2(16f, -720f), ActiveScan, 112f);
        CreateButton(panel.transform, "Next Reserve", new Vector2(142f, -720f), NextReserve, 112f);
        CreateButton(panel.transform, "Deploy Reserve", new Vector2(268f, -720f), DeployReserve, 112f);
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
            manager.TacticalInput?.SelectUnit(selectedUnitId);
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
        manager.TacticalInput?.SelectUnit(selectedUnitId);
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
        manager.TacticalInput?.SelectUnit(selectedUnitId);
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
        manager?.TacticalInput?.SetAttackSelection(selectedWeaponIndex,selectedAttackMode==AttackMode.Special);
        ShowSelected();
        RefreshBoardOverlays();
    }

    private void SetMeleeMode()
    {
        selectedAttackMode = AttackMode.Melee;
        ApplyAttackModeSelection();
    }

    private void SetRangedMode()
    {
        selectedAttackMode = AttackMode.Ranged;
        ApplyAttackModeSelection();
    }

    private void SetSpecialMode()
    {
        selectedAttackMode = AttackMode.Special;
        ApplyAttackModeSelection();
    }

    private void ApplyAttackModeSelection()
    {
        var unit = FindSelectedUnit();
        if (unit != null)
            selectedWeaponIndex = ResolveWeaponIndexForMode(unit, selectedAttackMode);
        manager?.TacticalInput?.SetAttackSelection(selectedWeaponIndex,selectedAttackMode==AttackMode.Special);
        UpdateAttackModeButtons();
        ShowSelected();
        RefreshBoardOverlays();
    }

    private void UpdateAttackModeButtons()
    {
        var unit = FindSelectedUnit();
        bool canUseRanged = CanUseRangedAttack(unit);
        bool canUseSpecial = CanUseSpecialAttack(unit);

        if (meleeAttackButton != null)
            meleeAttackButton.interactable = unit != null;
        if (rangedAttackButton != null)
            rangedAttackButton.interactable = canUseRanged;
        if (specialAttackButton != null)
            specialAttackButton.interactable = canUseSpecial;
    }

    private static bool CanUseRangedAttack(BattleUnitState unit)
    {
        if (unit?.Snapshot?.Weapons == null || unit.Snapshot.Weapons.Count == 0)
            return false;
        for (int i = 0; i < unit.Snapshot.Weapons.Count; i++)
            if (unit.Snapshot.Weapons[i] != null && unit.Snapshot.Weapons[i].usesRangedAttack)
                return true;
        return unit.Snapshot.Range > 1f;
    }

    private static bool CanUseSpecialAttack(BattleUnitState unit)
    {
        return unit?.Snapshot?.SpecialAttackProfile != null;
    }

    private static int ResolveWeaponIndexForMode(BattleUnitState unit, AttackMode mode)
    {
        if (unit?.Snapshot?.Weapons == null || unit.Snapshot.Weapons.Count == 0)
            return 0;

        if (mode == AttackMode.Ranged)
        {
            for (int i = 0; i < unit.Snapshot.Weapons.Count; i++)
                if (unit.Snapshot.Weapons[i] != null && unit.Snapshot.Weapons[i].usesRangedAttack)
                    return i;
            return 0;
        }

        if (mode == AttackMode.Melee)
        {
            for (int i = 0; i < unit.Snapshot.Weapons.Count; i++)
                if (unit.Snapshot.Weapons[i] != null && !unit.Snapshot.Weapons[i].usesRangedAttack)
                    return i;
            return 0;
        }

        return 0;
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
        if (unit == null)
        {
            selected.text = "No unit selected";
            UpdateAttackModeButtons();
            return;
        }

        int weaponCount = unit.Snapshot?.Weapons?.Count ?? 0;
        if (weaponCount > 0)
            selectedWeaponIndex = Mathf.Clamp(selectedWeaponIndex, 0, weaponCount - 1);
        else
            selectedWeaponIndex = 0;

        var weapon = weaponCount > 0 ? unit.Snapshot.Weapons[selectedWeaponIndex] : null;
        string weaponName = weapon?.equipment != null ? weapon.equipment.equipmentName : weapon != null ? "Built-in weapon" : "Unarmed";
        string modeName = selectedAttackMode == AttackMode.Ranged ? "Ranged" : selectedAttackMode == AttackMode.Special ? "Special" : "Melee";
        if (selectedAttackMode == AttackMode.Special && unit.Snapshot?.SpecialAttackProfile != null)
            weaponName = unit.Snapshot.SpecialAttackProfile.attackName;
        string abilities = BuildAbilitySummary(unit);
        selected.text = $"{unit.Snapshot?.SourceUnit?.data?.unitName ?? $"Unit {unit.UnitId}"}\n" +
            $"HP {unit.CurrentHealth}/{unit.Snapshot?.MaximumHealth ?? 1} | Attack {unit.Snapshot?.MeleeAttack ?? 0}/{unit.Snapshot?.RangedAttack ?? 0} | Defense {unit.Snapshot?.Defense ?? 0}\n" +
            $"Range {unit.Snapshot?.Range ?? 0:F0} | Move {unit.CurrentMovePoints} | AP {unit.CurrentActionPoints}\n" +
            $"Cell {unit.CellIndex} | {weaponName} ({modeName}) [{selectedWeaponIndex + 1}/{Mathf.Max(1, weaponCount)}]\n" +
            $"Abilities: {abilities}";

        UpdateAttackModeButtons();
    }

    private static string BuildAbilitySummary(BattleUnitState unit)
    {
        if (unit?.Snapshot?.TacticalProfile == null)
            return "No special abilities";

        var profile = unit.Snapshot.TacticalProfile;
        var abilities = new System.Collections.Generic.List<string>();
        if (profile.canMoveAfterAttacking) abilities.Add("Move after attack");
        if (profile.canAttackAfterMoving) abilities.Add("Attack after move");
        if (profile.exertsZoneOfControl) abilities.Add("Zone of control");
        if (profile.usesIndirectFire) abilities.Add("Indirect fire");
        if (profile.isTransport) abilities.Add("Transport");
        if (profile.isCarrier) abilities.Add("Carrier");
        if (profile.canCrossCliffs) abilities.Add("Cliff crossing");
        if (profile.ignoresRiverPenalty) abilities.Add("River ignore");
        if (profile.ignoresForestMovementPenalty) abilities.Add("Forest ignore");
        return abilities.Count > 0 ? string.Join(", ", abilities) : "No special abilities";
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
        manager?.TacticalInput?.SetMode(BattleInteractionMode.Movement);
        Notify("Select a highlighted destination hex.");
    }

    private void Attack()
    {
        manager?.TacticalInput?.SetMode(BattleInteractionMode.Attack);
        Notify("Select a highlighted detected target.");
    }

    private void Defend() { Submit(manager.TryDefendUnit(selectedUnitId, out string reason), reason); }
    private void Wait() { Submit(manager.TryWaitUnit(selectedUnitId, out string reason), reason); }
    private void EndUnit() { Submit(manager.EndUnitActivation(selectedUnitId, out string reason), reason); }
    private void Retreat()
    {
        manager?.TacticalInput?.SetMode(BattleInteractionMode.Retreat);
        Notify("Select a highlighted friendly battlefield-edge exit.");
    }
    private void Embark()
    {
        manager?.TacticalInput?.SetMode(BattleInteractionMode.Embark);
        Notify("Select an adjacent friendly transport.");
    }
    private void Disembark()
    {
        manager?.TacticalInput?.SetMode(BattleInteractionMode.Disembark);
        Notify("Select a beach, port, or valid adjacent destination.");
    }
    private void Launch()
    {
        manager?.TacticalInput?.SetMode(BattleInteractionMode.Launch);
        Notify("Select an adjacent aircraft launch cell.");
    }
    private void Recover()
    {
        manager?.TacticalInput?.SetMode(BattleInteractionMode.Recovery);
        Notify("Select an adjacent friendly carrier.");
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

    private static Button CreateButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action, float width = 112f)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button)); go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>(); rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f); rect.pivot = new Vector2(0f, 1f); rect.anchoredPosition = position; rect.sizeDelta = new Vector2(width, 36f);
        go.GetComponent<Image>().color = new Color(0.72f, 0.67f, 0.52f, 1f); go.GetComponent<Button>().onClick.AddListener(action);
        var text = CreateText(go.transform, "Label", new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width - 8f, 30f), 13f); text.alignment = TextAlignmentOptions.Center; text.color = Color.black; text.text = label;
        return go.GetComponent<Button>();
    }
}
