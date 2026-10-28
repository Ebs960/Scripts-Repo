using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Authors the tactical battle UI prefabs (status bar, command dock, roster card, confrontation, result).
/// Missing prefabs are created automatically once; existing prefabs are never overwritten except through the
/// explicit menu command. Placeholder Image backgrounds stand in for final art.
/// </summary>
[InitializeOnLoad]
public static class BattleUIPrefabBuilder
{
    private const string Folder = "Assets/Scripts Repo/Battle/UI/Resources/TacticalBattleUI";
    private const string CardPath = Folder + "/TacticalRosterCard.prefab";
    private const string CanvasPath = Folder + "/TacticalBattleCanvas.prefab";
    private const string ConfrontationPath = Folder + "/BattleConfrontationPanel.prefab";
    private const string ResultPath = Folder + "/BattleResultPanel.prefab";

    private static readonly Color Navy = new(0.035f, 0.06f, 0.12f, 0.96f);
    private static readonly Color NavyLight = new(0.07f, 0.11f, 0.2f, 0.98f);
    private static readonly Color NavyDark = new(0.02f, 0.035f, 0.08f, 0.9f);
    private static readonly Color Gold = new(0.93f, 0.76f, 0.28f, 1f);
    private static readonly Color GoldDim = new(0.62f, 0.5f, 0.2f, 1f);
    private static readonly Color TextColor = new(0.93f, 0.91f, 0.82f, 1f);
    private static readonly Color Muted = new(0.62f, 0.66f, 0.74f, 1f);
    private static readonly Color ButtonBase = new(0.17f, 0.24f, 0.4f, 1f);
    private static readonly Color ButtonPrimary = new(0.74f, 0.58f, 0.2f, 1f);
    private static readonly Color Good = new(0.4f, 0.85f, 0.45f, 1f);

    private static readonly List<BattleHoverTip> tips = new();

    static BattleUIPrefabBuilder() => EditorApplication.delayCall += EnsureMissingPrefabs;

    [MenuItem("Tools/Battle UI/Build Tactical Battle Prefabs")]
    private static void RebuildAll()
    {
        bool anyExists = AssetDatabase.LoadAssetAtPath<GameObject>(CanvasPath) != null
            || AssetDatabase.LoadAssetAtPath<GameObject>(ConfrontationPath) != null
            || AssetDatabase.LoadAssetAtPath<GameObject>(ResultPath) != null
            || AssetDatabase.LoadAssetAtPath<GameObject>(CardPath) != null;
        if (anyExists && !EditorUtility.DisplayDialog("Rebuild tactical battle prefabs",
                "This regenerates all tactical battle UI prefabs and discards manual edits to them. Continue?", "Rebuild", "Cancel"))
            return;
        Build(true);
    }

    private static void EnsureMissingPrefabs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
        bool missing = AssetDatabase.LoadAssetAtPath<GameObject>(CardPath) == null
            || AssetDatabase.LoadAssetAtPath<GameObject>(CanvasPath) == null
            || AssetDatabase.LoadAssetAtPath<GameObject>(ConfrontationPath) == null
            || AssetDatabase.LoadAssetAtPath<GameObject>(ResultPath) == null;
        if (missing) Build(false);
    }

    private static void Build(bool overwrite)
    {
        EnsureFolder("Assets/Scripts Repo/Battle/UI/Resources");
        EnsureFolder(Folder);
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            if (overwrite || Missing(CardPath)) BuildCard(scene);
            if (overwrite || Missing(CanvasPath)) BuildCanvas(scene);
            if (overwrite || Missing(ConfrontationPath)) BuildConfrontation(scene);
            if (overwrite || Missing(ResultPath)) BuildResult(scene);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[BattleUIPrefabBuilder] Tactical battle UI prefabs are in " + Folder);
    }

    private static bool Missing(string path) => AssetDatabase.LoadAssetAtPath<GameObject>(path) == null;

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }

    // ------------------------------------------------------------- helpers

    private static RectTransform Root(string name, Scene scene)
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
        SceneManager.MoveGameObjectToScene(go, scene);
        return (RectTransform)go.transform;
    }

    private static RectTransform Node(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform)) { layer = LayerMask.NameToLayer("UI") };
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Place(RectTransform r, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 pos, Vector2 size)
    {
        r.anchorMin = anchorMin; r.anchorMax = anchorMax; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
    }

    private static void BL(RectTransform r, float x, float y, float w, float h) => Place(r, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(x, y), new Vector2(w, h));
    private static void TL(RectTransform r, float x, float y, float w, float h) => Place(r, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(w, h));
    private static void TopCenter(RectTransform r, float y, float w, float h) => Place(r, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -y), new Vector2(w, h));
    private static void BottomCenter(RectTransform r, float x, float y, float w, float h) => Place(r, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(x, y), new Vector2(w, h));
    private static void Middle(RectTransform r, float w, float h) => Place(r, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, h));

    private static void Fill(RectTransform r, float left = 0, float bottom = 0, float right = 0, float top = 0)
    {
        r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.pivot = new Vector2(0.5f, 0.5f);
        r.offsetMin = new Vector2(left, bottom); r.offsetMax = new Vector2(-right, -top);
    }

    private static Image Panel(RectTransform r, Color color, bool outline = false, bool raycast = true)
    {
        var image = r.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        if (outline)
        {
            var o = r.gameObject.AddComponent<Outline>();
            o.effectColor = GoldDim;
            o.effectDistance = new Vector2(1.5f, -1.5f);
        }
        return image;
    }

    private static TextMeshProUGUI Text(Transform parent, string name, string text, float size, TextAlignmentOptions align,
        Color color, FontStyles style = FontStyles.Normal, bool wrap = false)
    {
        var r = Node(name, parent);
        var t = r.gameObject.AddComponent<TextMeshProUGUI>();
        t.font = TMP_Settings.defaultFontAsset;
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = color;
        t.fontStyle = style;
        t.raycastTarget = false;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    private static GameObject Frame(Transform parent, string name, float thickness, Color color)
    {
        var root = Node(name, parent);
        Fill(root);
        MakeEdge(root, "Top", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(0, thickness), color);
        MakeEdge(root, "Bottom", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, thickness), color);
        MakeEdge(root, "Left", new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), new Vector2(thickness, 0), color);
        MakeEdge(root, "Right", new Vector2(1, 0), new Vector2(1, 1), new Vector2(1, 0.5f), new Vector2(thickness, 0), color);
        root.gameObject.SetActive(false);
        return root.gameObject;
    }

    private static void MakeEdge(Transform parent, string name, Vector2 min, Vector2 max, Vector2 pivot, Vector2 size, Color color)
    {
        var r = Node(name, parent);
        Place(r, min, max, pivot, Vector2.zero, size);
        Panel(r, color, false, false);
    }

    private static Button MakeButton(Transform parent, string name, string label, float fontSize = 14, bool primary = false, bool frame = true)
    {
        var r = Node(name, parent);
        var image = Panel(r, primary ? ButtonPrimary : ButtonBase, true);
        var button = r.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        var colors = button.colors;
        colors.normalColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.68f, 0.68f, 0.68f, 1f);
        colors.selectedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
        colors.disabledColor = new Color(0.4f, 0.4f, 0.4f, 0.7f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        var text = Text(r, "Label", label, fontSize, TextAlignmentOptions.Center, primary ? new Color(0.08f, 0.06f, 0.02f) : TextColor, FontStyles.Bold, true);
        Fill(text.rectTransform, 3, 2, 3, 2);
        if (frame) Frame(r, "ActiveFrame", 2f, Gold);
        tips.Add(r.gameObject.AddComponent<BattleHoverTip>());
        return button;
    }

    private static TextMeshProUGUI LabelOf(Button button) => button.transform.Find("Label").GetComponent<TextMeshProUGUI>();

    private static void Set(Object target, string field, object value)
    {
        var so = new SerializedObject(target);
        var p = so.FindProperty(field);
        if (p == null)
        {
            Debug.LogError($"[BattleUIPrefabBuilder] {target.GetType().Name} has no serialized field '{field}'.");
            return;
        }
        switch (value)
        {
            case null: p.objectReferenceValue = null; break;
            case bool b: p.boolValue = b; break;
            case Object o: p.objectReferenceValue = o; break;
            default: Debug.LogError($"[BattleUIPrefabBuilder] Unsupported value for {field}."); break;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Canvas AddCanvas(RectTransform root, int sortingOrder)
    {
        var canvas = root.gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = root.gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        root.gameObject.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    private static void Save(RectTransform root, string path)
    {
        PrefabUtility.SaveAsPrefabAsset(root.gameObject, path);
    }

    // ------------------------------------------------------------ roster card

    private static void BuildCard(Scene scene)
    {
        var root = Root("TacticalRosterCard", scene);
        Middle(root, 56, 68);
        var background = Panel(root, NavyLight, true);
        var button = root.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        var colors = button.colors;
        colors.normalColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        colors.highlightedColor = Color.white;
        colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
        colors.selectedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        button.colors = colors;
        var layout = root.gameObject.AddComponent<LayoutElement>();
        layout.minWidth = layout.preferredWidth = 56;
        layout.minHeight = layout.preferredHeight = 68;

        var iconRect = Node("UnitIcon", root);
        Fill(iconRect, 4, 14, 4, 4);
        var icon = iconRect.gameObject.AddComponent<Image>();
        icon.preserveAspect = true;
        icon.raycastTarget = false;

        var fallback = Text(root, "FallbackLabel", "??", 18, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
        Fill(fallback.rectTransform, 4, 14, 4, 4);

        var dim = Node("DimOverlay", root);
        Fill(dim);
        Panel(dim, new Color(0f, 0f, 0f, 0.5f), false, false);
        dim.gameObject.SetActive(false);

        var healthBack = Node("HealthBar", root);
        Place(healthBack, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), new Vector2(0, 5), new Vector2(-8, 5));
        Panel(healthBack, new Color(0.01f, 0.02f, 0.04f, 1f), false, false);
        var healthFill = Node("Fill", healthBack);
        Fill(healthFill);
        var fillImage = Panel(healthFill, Good, false, false);

        var dot = Node("StatusIndicator", root);
        Place(dot, Vector2.one, Vector2.one, Vector2.one, new Vector2(-3, -3), new Vector2(10, 10));
        var dotImage = Panel(dot, Good, false, false);

        var selection = Frame(root, "SelectionIndicator", 3f, Gold);
        var tip = root.gameObject.AddComponent<BattleHoverTip>();
        var card = root.gameObject.AddComponent<TacticalRosterCard>();
        Set(card, "button", button);
        Set(card, "icon", icon);
        Set(card, "fallbackLabel", fallback);
        Set(card, "selectionIndicator", selection);
        Set(card, "healthFill", healthFill);
        Set(card, "healthFillImage", fillImage);
        Set(card, "statusDot", dotImage);
        Set(card, "dimOverlay", dim.gameObject);
        Set(card, "hoverTip", tip);
        Save(root, CardPath);
    }

    // ----------------------------------------------------------------- canvas

    private static void BuildCanvas(Scene scene)
    {
        tips.Clear();
        var root = Root("TacticalBattleCanvas", scene);
        AddCanvas(root, 510);
        var hud = root.gameObject.AddComponent<BattleHUD>();

        // ---- Top status bar
        var bar = Node("BattleTopBar", root);
        Place(bar, new Vector2(0, 1), new Vector2(1, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 54));
        var barBackground = Node("BattleTopBarArtwork", bar);
        Fill(barBackground);
        Panel(barBackground, Navy, true);
        var barEdge = Node("BottomEdge", bar);
        Place(barEdge, new Vector2(0, 0), new Vector2(1, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(0, 3));
        Panel(barEdge, Gold, false, false);

        var statusView = bar.gameObject.AddComponent<BattleStatusBarView>();
        BuildSideSection(bar, statusView, true);
        BuildSideSection(bar, statusView, false);

        var center = Node("CenterSection", bar);
        Place(center, new Vector2(0.5f, 0), new Vector2(0.5f, 1), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(520, 0));
        var round = Text(center, "RoundText", "Round 1 / 1", 20, TextAlignmentOptions.Center, Gold, FontStyles.Bold);
        TopCenter(round.rectTransform, 4, 520, 24);
        var phase = Text(center, "PhaseText", "Deployment", 15, TextAlignmentOptions.Right, Muted);
        Place(phase.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(1, 0), new Vector2(-6, 6), new Vector2(200, 20));
        var activeSide = Text(center, "ActiveSideText", string.Empty, 15, TextAlignmentOptions.Left, TextColor);
        Place(activeSide.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(6, 6), new Vector2(300, 20));
        Set(statusView, "roundText", round);
        Set(statusView, "phaseText", phase);
        Set(statusView, "activeSideText", activeSide);

        // ---- Objective indicator
        var objective = Node("ObjectiveIndicator", root);
        TopCenter(objective, 62, 460, 28);
        Panel(objective, Navy, true, false);
        var objectiveText = Text(objective, "ObjectiveText", string.Empty, 14, TextAlignmentOptions.Center, TextColor);
        Fill(objectiveText.rectTransform, 6, 0, 6, 0);
        objective.gameObject.SetActive(false);

        // ---- Camera controls
        var camera = Node("CameraControls", root);
        Place(camera, Vector2.one, Vector2.one, Vector2.one, new Vector2(-16, -66), new Vector2(190, 38));
        var zoomOut = MakeButton(camera, "ZoomOutButton", "-", 20, false, false);
        Place((RectTransform)zoomOut.transform, Vector2.one, Vector2.one, Vector2.one, Vector2.zero, new Vector2(38, 38));
        var zoomIn = MakeButton(camera, "ZoomInButton", "+", 20, false, false);
        Place((RectTransform)zoomIn.transform, Vector2.one, Vector2.one, Vector2.one, new Vector2(-42, 0), new Vector2(38, 38));
        var layer = MakeButton(camera, "LayerButton", "Layer: All", 14, false, false);
        Place((RectTransform)layer.transform, Vector2.one, Vector2.one, Vector2.one, new Vector2(-84, 0), new Vector2(106, 38));

        // ---- Command dock
        var dock = Node("BattleBottomBar", root);
        Place(dock, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 10), new Vector2(1200, 180));
        Panel(dock, Navy, true);
        var dockEdge = Node("TopEdge", dock);
        Place(dockEdge, new Vector2(0, 1), Vector2.one, new Vector2(0.5f, 1), Vector2.zero, new Vector2(0, 3));
        Panel(dockEdge, GoldDim, false, false);

        // Selected unit panel
        var selected = Node("SelectedUnitPanel", dock);
        BL(selected, 10, 10, 300, 160);
        Panel(selected, NavyLight);
        var iconFrame = Node("UnitIconFrame", selected);
        BL(iconFrame, 6, 86, 68, 68);
        Panel(iconFrame, NavyDark, false, false);
        var unitIcon = Node("UnitIcon", selected);
        BL(unitIcon, 8, 88, 64, 64);
        var unitIconImage = unitIcon.gameObject.AddComponent<Image>();
        unitIconImage.preserveAspect = true;
        unitIconImage.raycastTarget = false;
        var unitName = Text(selected, "UnitName", "No unit selected", 20, TextAlignmentOptions.Left, Gold, FontStyles.Bold);
        BL(unitName.rectTransform, 80, 118, 166, 30);
        var healthBack = Node("HealthBar", selected);
        BL(healthBack, 80, 104, 210, 8);
        Panel(healthBack, NavyDark, false, false);
        var healthFill = Node("Fill", healthBack);
        Fill(healthFill);
        Panel(healthFill, Good, false, false);
        var previousUnit = MakeButton(selected, "PreviousUnitButton", "<", 14, false, false);
        BL((RectTransform)previousUnit.transform, 250, 128, 22, 22);
        var nextUnit = MakeButton(selected, "NextUnitButton", ">", 14, false, false);
        BL((RectTransform)nextUnit.transform, 274, 128, 22, 22);
        var unitStats = Text(selected, "UnitStats", string.Empty, 14, TextAlignmentOptions.TopLeft, TextColor, FontStyles.Normal, true);
        BL(unitStats.rectTransform, 8, 34, 284, 60);
        var unitStatus = Text(selected, "UnitStatus", string.Empty, 14, TextAlignmentOptions.Left, TextColor);
        BL(unitStatus.rectTransform, 8, 8, 284, 24);

        // Center column
        var centerColumn = Node("CenterColumn", dock);
        BL(centerColumn, 320, 10, 620, 160);

        var combat = Node("CombatControls", centerColumn);
        BL(combat, 0, 86, 620, 74);
        string[] labels = { "Move", "Attack", "Defense Mode", "Wait", "Retreat" };
        var primary = new Button[5];
        for (int i = 0; i < labels.Length; i++)
        {
            primary[i] = MakeButton(combat, labels[i].Replace(" ", string.Empty) + "Button", labels[i]);
            BL((RectTransform)primary[i].transform, i * 100, 40, 94, 34);
        }
        var hint = Text(combat, "ContextHint", string.Empty, 14, TextAlignmentOptions.Left, Muted, FontStyles.Italic);
        BL(hint.rectTransform, 0, 2, 480, 32);
        var attackModeSelector = Node("AttackModeSelector", combat);
        BL(attackModeSelector, 0, 2, 204, 32);
        var melee = MakeButton(attackModeSelector, "MeleeButton", "Melee", 13);
        BL((RectTransform)melee.transform, 0, 0, 64, 32);
        var ranged = MakeButton(attackModeSelector, "RangedButton", "Ranged", 13);
        BL((RectTransform)ranged.transform, 70, 0, 64, 32);
        var special = MakeButton(attackModeSelector, "SpecialButton", "Special", 13);
        BL((RectTransform)special.transform, 140, 0, 64, 32);
        var weapon = MakeButton(combat, "WeaponButton", "Weapon", 13, false, false);
        BL((RectTransform)weapon.transform, 212, 2, 230, 32);
        var more = MakeButton(combat, "MoreActionsButton", "More...", 13, false, false);
        BL((RectTransform)more.transform, 500, 2, 94, 32);

        // Deployment controls
        var deploy = Node("DeploymentControls", centerColumn);
        BL(deploy, 0, 86, 620, 74);
        var deployedTab = MakeButton(deploy, "DeployedTab", "Deployed", 14);
        BL((RectTransform)deployedTab.transform, 0, 40, 130, 34);
        var reservesTab = MakeButton(deploy, "ReservesTab", "Reserves", 14);
        BL((RectTransform)reservesTab.transform, 136, 40, 130, 34);
        var deploymentCount = Text(deploy, "DeploymentCount", string.Empty, 15, TextAlignmentOptions.Left, TextColor);
        BL(deploymentCount.rectTransform, 276, 40, 200, 34);
        var reset = MakeButton(deploy, "ResetButton", "Reset Deployment", 14, false, false);
        BL((RectTransform)reset.transform, 484, 40, 110, 34);
        var swap = MakeButton(deploy, "SwapReserveButton", "Swap Reserve", 13, false, false);
        BL((RectTransform)swap.transform, 0, 2, 150, 32);
        var validation = Text(deploy, "ValidationMessage", string.Empty, 14, TextAlignmentOptions.Left, TextColor, FontStyles.Normal, true);
        BL(validation.rectTransform, 158, 2, 436, 32);
        deploy.gameObject.SetActive(false);

        // Tactical roster
        var roster = Node("TacticalRoster", centerColumn);
        BL(roster, 0, 0, 620, 80);
        Panel(roster, NavyDark);
        var scroll = roster.gameObject.AddComponent<ScrollRect>();
        var viewport = Node("Viewport", roster);
        Fill(viewport, 4, 4, 4, 4);
        viewport.gameObject.AddComponent<RectMask2D>();
        var content = Node("Content", viewport);
        Place(content, new Vector2(0, 0), new Vector2(0, 1), new Vector2(0, 0.5f), Vector2.zero, Vector2.zero);
        var layout = content.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 4;
        layout.padding = new RectOffset(2, 2, 2, 2);
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = true;
        scroll.vertical = false;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 25f;

        // Right column
        var right = Node("RightColumn", dock);
        BL(right, 950, 10, 240, 160);
        Panel(right, NavyLight);
        var sideTurn = Node("SideTurnControls", right);
        Fill(sideTurn);
        var ready = Text(sideTurn, "ReadyUnitCount", string.Empty, 18, TextAlignmentOptions.Center, TextColor, FontStyles.Bold, true);
        BL(ready.rectTransform, 10, 100, 220, 50);
        var endSide = MakeButton(sideTurn, "EndSideButton", "END TURN", 20, true, false);
        BL((RectTransform)endSide.transform, 10, 14, 220, 56);
        var confirmRoot = Node("DeploymentConfirmControls", right);
        Fill(confirmRoot);
        var deployingSide = Text(confirmRoot, "DeployingSideText", string.Empty, 16, TextAlignmentOptions.Center, TextColor, FontStyles.Bold, true);
        BL(deployingSide.rectTransform, 10, 100, 220, 50);
        var confirm = MakeButton(confirmRoot, "ConfirmDeploymentButton", "CONFIRM DEPLOYMENT", 17, true, false);
        BL((RectTransform)confirm.transform, 10, 14, 220, 56);
        confirmRoot.gameObject.SetActive(false);

        // ---- Advanced actions popover
        var popover = Node("AdvancedActionsPopover", root);
        Place(popover, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(267, 196), new Vector2(300, 170));
        Panel(popover, Navy, true);
        var grid = popover.gameObject.AddComponent<GridLayoutGroup>();
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.cellSize = new Vector2(140, 34);
        grid.spacing = new Vector2(6, 6);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 2;
        var popoverFit = popover.gameObject.AddComponent<ContentSizeFitter>();
        popoverFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var embark = MakeButton(popover, "EmbarkButton", "Embark", 13, false, false);
        var disembark = MakeButton(popover, "DisembarkButton", "Disembark", 13, false, false);
        var launch = MakeButton(popover, "LaunchAircraftButton", "Launch Aircraft", 13, false, false);
        var recover = MakeButton(popover, "RecoverAircraftButton", "Recover Aircraft", 13, false, false);
        var dive = MakeButton(popover, "DiveButton", "Dive", 13, false, false);
        var shallow = MakeButton(popover, "ShallowButton", "Shallow", 13, false, false);
        var scan = MakeButton(popover, "ActiveScanButton", "Active Scan", 13, false, false);
        popover.gameObject.SetActive(false);

        // ---- Combat preview tooltip
        var preview = Node("CombatPreviewTooltip", root);
        Place(preview, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, new Vector2(290, 10));
        Panel(preview, Navy, true, false);
        var previewLayout = preview.gameObject.AddComponent<VerticalLayoutGroup>();
        previewLayout.padding = new RectOffset(10, 10, 8, 8);
        previewLayout.spacing = 4;
        previewLayout.childControlWidth = previewLayout.childControlHeight = true;
        previewLayout.childForceExpandWidth = previewLayout.childForceExpandHeight = false;
        var previewFit = preview.gameObject.AddComponent<ContentSizeFitter>();
        previewFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var previewTitle = Text(preview, "Title", string.Empty, 16, TextAlignmentOptions.Left, Gold, FontStyles.Bold, true);
        var previewBody = Text(preview, "Body", string.Empty, 14, TextAlignmentOptions.TopLeft, TextColor, FontStyles.Normal, true);
        var previewView = preview.gameObject.AddComponent<BattleCombatPreviewView>();
        Set(previewView, "canvasRect", root);
        Set(previewView, "panel", preview);
        Set(previewView, "dock", dock);
        Set(previewView, "title", previewTitle);
        Set(previewView, "body", previewBody);
        preview.gameObject.SetActive(false);

        // ---- Notifications
        var notice = Node("BattleNotifications", root);
        TopCenter(notice, 100, 560, 36);
        Panel(notice, new Color(0.02f, 0.035f, 0.08f, 0.85f), true, false);
        var noticeGroup = notice.gameObject.AddComponent<CanvasGroup>();
        noticeGroup.alpha = 0f;
        noticeGroup.interactable = false;
        noticeGroup.blocksRaycasts = false;
        var noticeText = Text(notice, "Message", string.Empty, 18, TextAlignmentOptions.Center, TextColor);
        Fill(noticeText.rectTransform, 8, 0, 8, 0);
        var noticeView = notice.gameObject.AddComponent<BattleNotificationView>();
        Set(noticeView, "group", noticeGroup);
        Set(noticeView, "label", noticeText);

        // ---- Shared hover tooltip (created last so it draws on top)
        var hover = Node("HoverTooltip", root);
        Place(hover, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0), Vector2.zero, new Vector2(260, 10));
        Panel(hover, Navy, true, false);
        var hoverLayout = hover.gameObject.AddComponent<VerticalLayoutGroup>();
        hoverLayout.padding = new RectOffset(10, 10, 6, 6);
        hoverLayout.childControlWidth = hoverLayout.childControlHeight = true;
        hoverLayout.childForceExpandWidth = hoverLayout.childForceExpandHeight = false;
        var hoverFit = hover.gameObject.AddComponent<ContentSizeFitter>();
        hoverFit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var hoverText = Text(hover, "Label", string.Empty, 14, TextAlignmentOptions.TopLeft, TextColor, FontStyles.Normal, true);
        var tooltipView = hover.gameObject.AddComponent<BattleTooltipView>();
        Set(tooltipView, "canvasRect", root);
        Set(tooltipView, "panel", hover);
        Set(tooltipView, "label", hoverText);
        hover.gameObject.SetActive(false);

        // ---- Wire tooltip references into every hover hook
        foreach (var tip in tips) Set(tip, "tooltip", tooltipView);
        foreach (var tip in bar.GetComponentsInChildren<BattleHoverTip>(true))
        {
            Set(tip, "tooltip", tooltipView);
            Set(tip, "below", true);
        }

        // ---- HUD references
        Set(hud, "statusBar", statusView);
        Set(hud, "objectiveIndicator", objective.gameObject);
        Set(hud, "objectiveText", objectiveText);
        Set(hud, "layerButton", layer);
        Set(hud, "layerLabel", LabelOf(layer));
        Set(hud, "zoomInButton", zoomIn);
        Set(hud, "zoomOutButton", zoomOut);
        Set(hud, "dock", dock);
        Set(hud, "unitIcon", unitIconImage);
        Set(hud, "unitName", unitName);
        Set(hud, "unitStats", unitStats);
        Set(hud, "unitStatus", unitStatus);
        Set(hud, "unitHealthFill", healthFill);
        Set(hud, "previousUnitButton", previousUnit);
        Set(hud, "nextUnitButton", nextUnit);
        Set(hud, "combatControls", combat.gameObject);
        Set(hud, "moveButton", primary[0]);
        Set(hud, "attackButton", primary[1]);
        Set(hud, "defendButton", primary[2]);
        Set(hud, "waitButton", primary[3]);
        Set(hud, "retreatButton", primary[4]);
        Set(hud, "contextHint", hint);
        Set(hud, "attackModeSelector", attackModeSelector.gameObject);
        Set(hud, "meleeModeButton", melee);
        Set(hud, "rangedModeButton", ranged);
        Set(hud, "specialModeButton", special);
        Set(hud, "weaponButton", weapon);
        Set(hud, "weaponLabel", LabelOf(weapon));
        Set(hud, "moreActionsButton", more);
        Set(hud, "advancedPopover", popover.gameObject);
        Set(hud, "embarkButton", embark);
        Set(hud, "disembarkButton", disembark);
        Set(hud, "launchButton", launch);
        Set(hud, "recoverButton", recover);
        Set(hud, "diveButton", dive);
        Set(hud, "shallowButton", shallow);
        Set(hud, "activeScanButton", scan);
        Set(hud, "rosterScroll", scroll);
        Set(hud, "rosterContent", content);
        Set(hud, "rosterCardPrefab", AssetDatabase.LoadAssetAtPath<TacticalRosterCard>(CardPath));
        Set(hud, "deploymentControls", deploy.gameObject);
        Set(hud, "deployedTab", deployedTab);
        Set(hud, "deployedTabLabel", LabelOf(deployedTab));
        Set(hud, "reservesTab", reservesTab);
        Set(hud, "reservesTabLabel", LabelOf(reservesTab));
        Set(hud, "deploymentCount", deploymentCount);
        Set(hud, "resetButton", reset);
        Set(hud, "swapReserveButton", swap);
        Set(hud, "validationMessage", validation);
        Set(hud, "deploymentConfirmControls", confirmRoot.gameObject);
        Set(hud, "deployingSideText", deployingSide);
        Set(hud, "confirmDeploymentButton", confirm);
        Set(hud, "sideTurnControls", sideTurn.gameObject);
        Set(hud, "readyUnitCount", ready);
        Set(hud, "endSideButton", endSide);
        Set(hud, "tooltip", tooltipView);
        Set(hud, "combatPreview", previewView);
        Set(hud, "notifications", noticeView);

        Save(root, CanvasPath);
    }

    private static void BuildSideSection(RectTransform bar, BattleStatusBarView view, bool attacker)
    {
        string prefix = attacker ? "Attacker" : "Defender";
        var section = Node(prefix + "Section", bar);
        Vector2 edge = attacker ? new Vector2(0, 0) : new Vector2(1, 0);
        Place(section, edge, new Vector2(edge.x, 1), new Vector2(edge.x, 0.5f), new Vector2(attacker ? 12 : -12, 0), new Vector2(650, 0));
        Panel(section, new Color(0, 0, 0, 0), false, true);
        var tip = section.gameObject.AddComponent<BattleHoverTip>();

        float side = attacker ? 1f : -1f;
        TextAlignmentOptions align = attacker ? TextAlignmentOptions.Left : TextAlignmentOptions.Right;
        Vector2 anchor = new Vector2(edge.x, 0.5f);

        var emblem = Node(prefix + "Emblem", section);
        Place(emblem, anchor, anchor, anchor, Vector2.zero, new Vector2(40, 40));
        var emblemImage = emblem.gameObject.AddComponent<Image>();
        emblemImage.preserveAspect = true;
        emblemImage.raycastTarget = false;
        emblemImage.enabled = false;

        var name = Text(section, prefix + "Name", prefix, 24, align, TextColor, FontStyles.Bold);
        Place(name.rectTransform, anchor, anchor, anchor, new Vector2(50 * side, 0), new Vector2(300, 36));
        var count = Text(section, prefix + "UnitCount", string.Empty, 18, align, Muted);
        Place(count.rectTransform, anchor, anchor, anchor, new Vector2(360 * side, 0), new Vector2(280, 36));

        Set(view, attacker ? "attackerName" : "defenderName", name);
        Set(view, attacker ? "attackerUnitCount" : "defenderUnitCount", count);
        Set(view, attacker ? "attackerEmblem" : "defenderEmblem", emblemImage);
        Set(view, attacker ? "attackerTip" : "defenderTip", tip);
    }

    // ------------------------------------------------------- confrontation

    private static void BuildConfrontation(Scene scene)
    {
        var root = Root("BattleConfrontationPanel", scene);
        AddCanvas(root, 500);
        var view = root.gameObject.AddComponent<BattlePreviewPanel>();

        var backdrop = Node("Backdrop", root);
        Fill(backdrop);
        Panel(backdrop, new Color(0.015f, 0.02f, 0.025f, 0.82f));
        var panel = Node("BattleConfrontationShellArtwork", root);
        Middle(panel, 1280, 960);
        Panel(panel, Navy, true);

        var title = Text(panel, "Title", "BATTLE / ENGAGEMENT", 30, TextAlignmentOptions.Center, Gold, FontStyles.Bold);
        TopCenter(title.rectTransform, 16, 800, 44);

        var attackerView = BuildConfrontationSide(panel, true);
        var defenderView = BuildConfrontationSide(panel, false);

        var battlefield = Text(panel, "Battlefield", string.Empty, 17, TextAlignmentOptions.Center, TextColor, FontStyles.Normal, true);
        TopCenter(battlefield.rectTransform, 546, 1160, 54);

        var forecastHeader = Text(panel, "ForecastHeader", "ESTIMATED AUTO-RESOLVE ODDS", 16, TextAlignmentOptions.Center, Gold, FontStyles.Bold);
        TopCenter(forecastHeader.rectTransform, 606, 900, 22);
        var odds = Text(panel, "Odds", string.Empty, 32, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
        TopCenter(odds.rectTransform, 628, 900, 44);
        var advantage = Text(panel, "Advantage", string.Empty, 16, TextAlignmentOptions.Center, TextColor);
        TopCenter(advantage.rectTransform, 672, 900, 22);
        var losses = Text(panel, "Losses", string.Empty, 15, TextAlignmentOptions.Center, Muted);
        TopCenter(losses.rectTransform, 694, 1100, 22);
        var factors = Text(panel, "Factors", string.Empty, 14, TextAlignmentOptions.Top, Muted, FontStyles.Normal, true);
        TopCenter(factors.rectTransform, 716, 1100, 52);

        var selector = Text(panel, "CommanderSelector", string.Empty, 15, TextAlignmentOptions.Center, TextColor, FontStyles.Normal, true);
        BottomCenter(selector.rectTransform, 0, 116, 760, 56);
        var previous = MakeButton(panel, "PreviousCommanderButton", "<", 16, false, false);
        BottomCenter((RectTransform)previous.transform, -165, 76, 50, 36);
        var next = MakeButton(panel, "NextCommanderButton", ">", 16, false, false);
        BottomCenter((RectTransform)next.transform, -105, 76, 50, 36);
        var role = MakeButton(panel, "RoleButton", "ROLE", 14, false, false);
        BottomCenter((RectTransform)role.transform, 0, 76, 120, 36);
        var assign = MakeButton(panel, "AssignButton", "ASSIGN", 14, false, false);
        BottomCenter((RectTransform)assign.transform, 135, 76, 130, 36);

        var back = MakeButton(panel, "BackButton", "Back", 16, false, false);
        Place((RectTransform)back.transform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(40, 16), new Vector2(110, 48));
        var retreat = MakeButton(panel, "RetreatButton", "RETREAT", 16, false, false);
        BottomCenter((RectTransform)retreat.transform, -240, 16, 200, 48);
        var auto = MakeButton(panel, "AutoResolveButton", "AUTO-RESOLVE", 16, false, false);
        BottomCenter((RectTransform)auto.transform, 0, 16, 230, 48);
        var fight = MakeButton(panel, "FightButton", "FIGHT", 18, true, false);
        BottomCenter((RectTransform)fight.transform, 240, 16, 200, 48);

        BuildModalTooltip(root);

        Set(view, "attackerSide", attackerView);
        Set(view, "defenderSide", defenderView);
        Set(view, "battlefieldText", battlefield);
        Set(view, "oddsText", odds);
        Set(view, "advantageText", advantage);
        Set(view, "lossesText", losses);
        Set(view, "factorsText", factors);
        Set(view, "commanderSelectorText", selector);
        Set(view, "fightButton", fight);
        Set(view, "autoResolveButton", auto);
        Set(view, "retreatButton", retreat);
        Set(view, "cancelButton", back);
        Set(view, "previousCommanderButton", previous);
        Set(view, "nextCommanderButton", next);
        Set(view, "roleButton", role);
        Set(view, "assignButton", assign);
        Save(root, ConfrontationPath);
    }

    private static BattleConfrontationSideView BuildConfrontationSide(RectTransform panel, bool attacker)
    {
        var column = Node(attacker ? "AttackerSide" : "DefenderSide", panel);
        Place(column, new Vector2(attacker ? 0 : 1, 1), new Vector2(attacker ? 0 : 1, 1), new Vector2(attacker ? 0 : 1, 1),
            new Vector2(attacker ? 40 : -40, -70), new Vector2(560, 470));
        Panel(column, NavyLight, false, false);
        var view = column.gameObject.AddComponent<BattleConfrontationSideView>();

        var sideLabel = Text(column, "SideLabel", string.Empty, 18, TextAlignmentOptions.Left, Muted, FontStyles.Bold);
        TL((RectTransform)sideLabel.transform, 14, 8, 300, 26);
        var emblemRect = Node("Emblem", column);
        TL(emblemRect, 14, 38, 52, 52);
        var emblem = emblemRect.gameObject.AddComponent<Image>();
        emblem.preserveAspect = true;
        emblem.raycastTarget = false;
        emblem.enabled = false;
        var civ = Text(column, "CivName", string.Empty, 28, TextAlignmentOptions.Left, Gold, FontStyles.Bold);
        TL(civ.rectTransform, 76, 42, 470, 44);
        var commander = Text(column, "Commander", string.Empty, 16, TextAlignmentOptions.TopLeft, TextColor, FontStyles.Normal, true);
        TL(commander.rectTransform, 14, 100, 532, 96);
        var soldiers = Text(column, "Soldiers", string.Empty, 26, TextAlignmentOptions.Left, TextColor, FontStyles.Bold);
        TL(soldiers.rectTransform, 14, 202, 532, 36);
        var units = Text(column, "Units", string.Empty, 17, TextAlignmentOptions.Left, Muted);
        TL(units.rectTransform, 14, 240, 532, 24);
        var compositionHeader = Text(column, "CompositionHeader", "ARMY COMPOSITION", 15, TextAlignmentOptions.Left, Gold, FontStyles.Bold);
        TL(compositionHeader.rectTransform, 14, 274, 532, 22);
        var composition = Text(column, "Composition", string.Empty, 16, TextAlignmentOptions.TopLeft, TextColor, FontStyles.Normal, true);
        TL(composition.rectTransform, 14, 298, 532, 96);
        var reinforcementsHeader = Text(column, "ReinforcementsHeader", "REINFORCEMENTS", 15, TextAlignmentOptions.Left, Gold, FontStyles.Bold);
        TL(reinforcementsHeader.rectTransform, 14, 398, 532, 22);
        var reinforcements = Text(column, "Reinforcements", string.Empty, 16, TextAlignmentOptions.TopLeft, TextColor, FontStyles.Normal, true);
        TL(reinforcements.rectTransform, 14, 420, 532, 46);

        Set(view, "sideLabel", sideLabel);
        Set(view, "civName", civ);
        Set(view, "emblem", emblem);
        Set(view, "commander", commander);
        Set(view, "soldiers", soldiers);
        Set(view, "units", units);
        Set(view, "composition", composition);
        Set(view, "reinforcements", reinforcements);
        return view;
    }

    // -------------------------------------------------------------- result

    private static void BuildResult(Scene scene)
    {
        var root = Root("BattleResultPanel", scene);
        AddCanvas(root, 530);
        var view = root.gameObject.AddComponent<BattleResultPanel>();

        var backdrop = Node("Backdrop", root);
        Fill(backdrop);
        Panel(backdrop, new Color(0.01f, 0.015f, 0.02f, 0.6f));
        var panel = Node("BattleResultsShellArtwork", root);
        Middle(panel, 640, 440);
        Panel(panel, Navy, true);

        var title = Text(panel, "Title", "BATTLE RESULT", 40, TextAlignmentOptions.Center, Gold, FontStyles.Bold);
        TopCenter(title.rectTransform, 24, 600, 56);
        var winner = Text(panel, "Winner", string.Empty, 20, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
        TopCenter(winner.rectTransform, 88, 600, 28);
        var resolution = Text(panel, "Resolution", string.Empty, 18, TextAlignmentOptions.Center, Muted, FontStyles.Normal, true);
        TopCenter(resolution.rectTransform, 118, 600, 40);
        var round = Text(panel, "FinalRound", string.Empty, 18, TextAlignmentOptions.Center, Muted);
        TopCenter(round.rectTransform, 160, 600, 26);

        string[] headers = { "Units Lost", "Retreated", "Survivors" };
        for (int i = 0; i < headers.Length; i++)
        {
            var header = Text(panel, "Header" + headers[i].Replace(" ", string.Empty), headers[i], 15, TextAlignmentOptions.Center, Gold, FontStyles.Bold);
            TL(header.rectTransform, 260 + i * 120, 190, 110, 26);
        }

        var attackerName = ResultRow(panel, "Attacker", 226, out var aLost, out var aRetreated, out var aSurvivors);
        var defenderName = ResultRow(panel, "Defender", 270, out var dLost, out var dRetreated, out var dSurvivors);

        var note = Text(panel, "Note", "Counts are whole tactical units, not individual soldier casualties.", 14, TextAlignmentOptions.Center, Muted, FontStyles.Italic, true);
        TopCenter(note.rectTransform, 326, 580, 40);

        var continueButton = MakeButton(panel, "ContinueButton", "CONTINUE", 20, true, false);
        BottomCenter((RectTransform)continueButton.transform, 0, 24, 220, 52);
        BuildModalTooltip(root);

        Set(view, "titleText", title);
        Set(view, "winnerText", winner);
        Set(view, "resolutionText", resolution);
        Set(view, "roundText", round);
        Set(view, "attackerName", attackerName);
        Set(view, "attackerLost", aLost);
        Set(view, "attackerRetreated", aRetreated);
        Set(view, "attackerSurvivors", aSurvivors);
        Set(view, "defenderName", defenderName);
        Set(view, "defenderLost", dLost);
        Set(view, "defenderRetreated", dRetreated);
        Set(view, "defenderSurvivors", dSurvivors);
        Set(view, "continueButton", continueButton);
        Save(root, ResultPath);
    }

    private static void BuildModalTooltip(RectTransform root)
    {
        var tooltipPanel = Node("TacticalHoverTooltipArtwork", root);
        Place(tooltipPanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0), Vector2.zero, new Vector2(260, 10));
        Panel(tooltipPanel, Navy, true, false);
        var layout = tooltipPanel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 6, 6);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = tooltipPanel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var label = Text(tooltipPanel, "TooltipText", string.Empty, 14, TextAlignmentOptions.TopLeft, TextColor, FontStyles.Normal, true);
        var tooltip = tooltipPanel.gameObject.AddComponent<BattleTooltipView>();
        Set(tooltip, "canvasRect", root);
        Set(tooltip, "panel", tooltipPanel);
        Set(tooltip, "label", label);
        tooltipPanel.gameObject.SetActive(false);

        foreach (var tip in root.GetComponentsInChildren<BattleHoverTip>(true))
        {
            Set(tip, "tooltip", tooltip);
            Set(tip, "below", false);
        }
    }

    private static TextMeshProUGUI ResultRow(RectTransform panel, string prefix, float y,
        out TextMeshProUGUI lost, out TextMeshProUGUI retreated, out TextMeshProUGUI survivors)
    {
        var name = Text(panel, prefix + "Name", prefix, 18, TextAlignmentOptions.Left, TextColor, FontStyles.Bold);
        TL(name.rectTransform, 30, y, 220, 36);
        lost = Text(panel, prefix + "Lost", "0", 20, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
        TL(lost.rectTransform, 260, y, 110, 36);
        retreated = Text(panel, prefix + "Retreated", "0", 20, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
        TL(retreated.rectTransform, 380, y, 110, 36);
        survivors = Text(panel, prefix + "Survivors", "0", 20, TextAlignmentOptions.Center, TextColor, FontStyles.Bold);
        TL(survivors.rectTransform, 500, y, 110, 36);
        return name;
    }
}
