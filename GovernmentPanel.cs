// (duplicate removed) - file contains a single GovernmentPanel class above
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Unified Government & Policy panel. Shows available governments and available policies for a selected civilization.
/// Creates UI elements dynamically without prefabs.
/// </summary>
public class GovernmentPanel : MonoBehaviour
{
    public static GovernmentPanel Instance { get; private set; }
    [Header("Root")]
    public GameObject panelRoot;
    public TextMeshProUGUI headerText;

    [Header("Governments Section")]
    public Transform governmentsContentRoot;
    public TextMeshProUGUI governmentsHeaderText; // Section header for available governments

    [Header("Runtime Prefabs (optional)")]
    // Instead of prefabs we now expose Inspector-assignable UI elements for designers:
    [Tooltip("Optional: assign a Close Button from the scene (Designer can place it in the panel). If assigned, it will be used instead of creating a runtime button.")]
    public Button closeButton;

    [Tooltip("Optional: root GameObject for the confirm dialog (used to Show/Hide). Assign a simple dialog that contains a TextMeshProUGUI for the message and OK/Cancel Buttons.")]
    public GameObject confirmDialogRoot;
    [Tooltip("Optional: assign the TextMeshProUGUI that will display the confirm message (if not part of the dialog root).")]
    public TextMeshProUGUI confirmMessageText;
    [Tooltip("Optional: assign the OK button for the confirm dialog.")]
    public Button confirmOkButton;
    [Tooltip("Optional: assign the Cancel button for the confirm dialog.")]
    public Button confirmCancelButton;
    [Tooltip("Optional: a transform under the confirm dialog to populate effect lines into.")]
    public Transform confirmEffectsContainer;
    [Tooltip("Optional: an Image under the confirm dialog to show an icon for policies.")]
    public Image confirmIconImage;

    [Header("Policies Section")]
    public Transform policiesContentRoot;
    public TextMeshProUGUI policiesHeaderText; // Add this for section header

    Civilization civ;
    List<GameObject> spawned = new List<GameObject>();
    // Runtime UI pieces (gameobjects we may create at runtime if inspector fields are empty)
    private GameObject autoCloseButton; // created runtime if no closeButton assigned
    private GameObject confirmDialog; // root GameObject used at runtime (either confirmDialogRoot or created)
    private GovernmentData pendingGovernment;
    private PolicyData pendingPolicy;
    // note: confirmMessageText, confirmOkButton, confirmCancelButton, confirmEffectsContainer and confirmIconImage
    // are exposed as public inspector fields above so designers can wire them; runtime creation will assign
    // those public fields when creating the fallback dialog if they aren't already assigned.

    public void ShowForCivilization(Civilization civ)
    {
        this.civ = civ;
        // Use UIManager to show panel so other panels are hidden consistently
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowPanel("governmentPanel");
        }
        else if (panelRoot != null)
            panelRoot.SetActive(true);

        EnsureRuntimeUI();
        if (headerText != null)
            headerText.text = civ != null ? ( (civ.civData != null ? civ.civData.civName : civ.gameObject.name) + " - Government & Policies" ) : "Government & Policies";
        RefreshAll();
    }

    private void Awake()
    {
        // Singleton convenience so other UIs can open the panel easily
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Start hidden - only show when user requests via UI
        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void Update()
    {
        // Close on Escape when panel is active
        if (panelRoot != null && panelRoot.activeSelf && Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
        }
    }

    private void EnsureRuntimeUI()
    {
        if (panelRoot == null) return;

        // Create an X close button in the top-right if missing
        if (autoCloseButton == null)
        {
                if (closeButton != null)
                {
                    // Use the inspector-assigned close button
                    autoCloseButton = closeButton.gameObject;
                    // Ensure listener is set
                    closeButton.onClick.RemoveAllListeners();
                    closeButton.onClick.AddListener(() => {
                        // Prefer UIManager hide so panels restore consistently
                        if (UIManager.Instance != null) UIManager.Instance.HidePanel("governmentPanel");
                        else Close();
                    });
                    // Ensure click sound wiring
                    if (UIManager.Instance != null) UIManager.Instance.WireUIInteractions(closeButton.gameObject);
                }
                else
                {
                    // Create a minimal runtime close button if none provided
                    autoCloseButton = new GameObject("CloseButton");
                    autoCloseButton.transform.SetParent(panelRoot.transform, false);
                    var btn = autoCloseButton.AddComponent<Button>();
                    var txt = autoCloseButton.AddComponent<TextMeshProUGUI>();
                    txt.text = "X";
                    txt.fontSize = 20;
                    txt.color = Color.white;
                    var rt = autoCloseButton.AddComponent<RectTransform>();
                    rt.anchorMin = new Vector2(1f, 1f);
                    rt.anchorMax = new Vector2(1f, 1f);
                    rt.pivot = new Vector2(1f, 1f);
                    rt.anchoredPosition = new Vector2(-10f, -10f);
                    rt.sizeDelta = new Vector2(30, 30);
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() => Close());
                    // Wire click sounds for runtime-created close button
                    if (UIManager.Instance != null) UIManager.Instance.WireUIInteractions(autoCloseButton);
                }
        }

        // Create a simple confirm dialog if missing
        if (confirmDialog == null)
        {
            // Prefer using an inspector-assigned dialog root when available
            if (confirmDialogRoot != null)
            {
                confirmDialog = confirmDialogRoot;
            }
            else
            {
                // Build a minimal runtime confirm dialog (keeps text + OK/Cancel) so functionality works without inspector wiring
                confirmDialog = new GameObject("ConfirmDialog");
                confirmDialog.transform.SetParent(panelRoot.transform, false);
                var rt = confirmDialog.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(320, 200);
                var image = confirmDialog.AddComponent<Image>();
                image.color = new Color(0f, 0f, 0f, 0.85f);

                // Message text
                var msgGO = new GameObject("Message");
                msgGO.transform.SetParent(confirmDialog.transform, false);
                var msg = msgGO.AddComponent<TextMeshProUGUI>();
                msg.text = "Confirm?";
                msg.alignment = TextAlignmentOptions.Center;
                msg.fontSize = 16;
                var msgRt = msgGO.AddComponent<RectTransform>();
                msgRt.anchorMin = new Vector2(0f, 0.7f);
                msgRt.anchorMax = new Vector2(1f, 1f);
                msgRt.offsetMin = new Vector2(8, 8);
                msgRt.offsetMax = new Vector2(-8, -8);

                // Effects container placeholder
                var effectsGO = new GameObject("EffectsContainer");
                effectsGO.transform.SetParent(confirmDialog.transform, false);
                var effectsRt = effectsGO.AddComponent<RectTransform>();
                effectsRt.anchorMin = new Vector2(0f, 0.35f);
                effectsRt.anchorMax = new Vector2(1f, 0.7f);
                effectsRt.offsetMin = new Vector2(8, 4);
                effectsRt.offsetMax = new Vector2(-8, -4);

                // Buttons container
                var btnContainer = new GameObject("Buttons");
                btnContainer.transform.SetParent(confirmDialog.transform, false);
                var btnRt = btnContainer.AddComponent<RectTransform>();
                btnRt.anchorMin = new Vector2(0f, 0f);
                btnRt.anchorMax = new Vector2(1f, 0.3f);
                btnRt.offsetMin = new Vector2(8, 8);
                btnRt.offsetMax = new Vector2(-8, -8);

                // Confirm (OK) button
                var okGO = new GameObject("OK");
                okGO.transform.SetParent(btnContainer.transform, false);
                var okBtn = okGO.AddComponent<Button>();
                var okTxt = okGO.AddComponent<TextMeshProUGUI>();
                okTxt.text = "Confirm";
                okTxt.alignment = TextAlignmentOptions.Center;
                okTxt.color = Color.white;
                var okRt = okGO.AddComponent<RectTransform>();
                okRt.anchorMin = new Vector2(0f, 0f);
                okRt.anchorMax = new Vector2(0.5f, 1f);
                okRt.offsetMin = new Vector2(4, 4);
                okRt.offsetMax = new Vector2(-4, -4);

                // Cancel button
                var cancelGO = new GameObject("Cancel");
                cancelGO.transform.SetParent(btnContainer.transform, false);
                var cancelBtn = cancelGO.AddComponent<Button>();
                var cancelTxt = cancelGO.AddComponent<TextMeshProUGUI>();
                cancelTxt.text = "Cancel";
                cancelTxt.alignment = TextAlignmentOptions.Center;
                cancelTxt.color = Color.white;
                var cancelRt = cancelGO.AddComponent<RectTransform>();
                cancelRt.anchorMin = new Vector2(0.5f, 0f);
                cancelRt.anchorMax = new Vector2(1f, 1f);
                cancelRt.offsetMin = new Vector2(4, 4);
                cancelRt.offsetMax = new Vector2(-4, -4);

                // Default behavior (hide dialog)
                okBtn.onClick.AddListener(() => { confirmDialog.SetActive(false); });
                cancelBtn.onClick.AddListener(() => { pendingGovernment = null; pendingPolicy = null; confirmDialog.SetActive(false); });

                // Assign runtime-created references if inspector ones weren't provided
                if (confirmMessageText == null) confirmMessageText = msg;
                if (confirmOkButton == null) confirmOkButton = okBtn;
                if (confirmCancelButton == null) confirmCancelButton = cancelBtn;
                if (confirmEffectsContainer == null) confirmEffectsContainer = effectsGO.transform;
            }

            if (confirmDialog != null)
                confirmDialog.SetActive(false);
            // Ensure dialog buttons have click sounds wired via UIManager
            if (UIManager.Instance != null && confirmDialog != null)
                UIManager.Instance.WireUIInteractions(confirmDialog);
        }
    }

    public void Hide()
    {
        // Prefer using UIManager so other panels (e.g. unit info) are restored consistently
        if (UIManager.Instance != null)
        {
            UIManager.Instance.HidePanel("governmentPanel");
        }
        else if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    public void RefreshAll()
    {
        ClearSpawned();
        if (civ == null) return;

        // Set section headers
        if (governmentsHeaderText != null)
        {
            governmentsHeaderText.text = "Available Governments";
            governmentsHeaderText.gameObject.SetActive(true);
        }
        if (policiesHeaderText != null)
        {
            policiesHeaderText.text = "Available Policies";
            policiesHeaderText.gameObject.SetActive(true);
        }

        // Governments
    if (governmentsContentRoot != null && PolicyManager.Instance != null)
    {
        var govs = PolicyManager.Instance.GetAvailableGovernments(civ);
            if (govs != null && govs.Count > 0)
            {
                foreach (var g in govs)
                {
                    // Create a simple row: Text + Button
            var rowGO = new GameObject($"Government_{g.governmentName}");
            rowGO.transform.SetParent(governmentsContentRoot, false);
                    spawned.Add(rowGO);

                    // Add Horizontal Layout Group
                    var layout = rowGO.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                    layout.childControlWidth = true;
                    layout.childControlHeight = true;
                    layout.childForceExpandWidth = false;
                    layout.childForceExpandHeight = false;

                    // Add Text
                    var textGO = new GameObject("Text");
                    textGO.transform.SetParent(rowGO.transform, false);
                    var txt = textGO.AddComponent<TextMeshProUGUI>();
                    txt.text = g.governmentName + "\n" + g.description;
                    txt.fontSize = 18;
                    txt.color = Color.white;
                    var textRect = textGO.AddComponent<RectTransform>();
                    textRect.sizeDelta = new Vector2(200, 60); // Increased height for description

                    // Add Button
                    var btnGO = new GameObject("Button");
                    btnGO.transform.SetParent(rowGO.transform, false);
                    var btn = btnGO.AddComponent<Button>();
                    var btnTxt = btnGO.AddComponent<TextMeshProUGUI>();
                    btnTxt.text = "Adopt";
                    btnTxt.fontSize = 16;
                    btnTxt.color = Color.black;
                    btnTxt.alignment = TextAlignmentOptions.Center;
                    var btnRect = btnGO.AddComponent<RectTransform>();
                    btnRect.sizeDelta = new Vector2(80, 30);

                    // Wire button to confirmation dialog
                    btn.interactable = PolicyManager.Instance.GetAvailableGovernments(civ).Contains(g);
                    btn.onClick.AddListener(() => {
                        pendingGovernment = g;
                        pendingPolicy = null;
                        EnsureRuntimeUI();
                        if (confirmMessageText != null) confirmMessageText.text = $"Adopt government '{g.governmentName}'? Cost: {g.policyPointCost} policy points.";
                        // Populate an effects breakdown in the confirm dialog when available
                        PopulateConfirmDialogEffects(g, null);
                        if (confirmOkButton != null)
                        {
                            confirmOkButton.onClick.RemoveAllListeners();
                            confirmOkButton.onClick.AddListener(() => {
                                TryChangeGovernment(pendingGovernment);
                                pendingGovernment = null;
                                confirmDialog.SetActive(false);
                            });
                        }
                        if (confirmCancelButton != null)
                        {
                            confirmCancelButton.onClick.RemoveAllListeners();
                            confirmCancelButton.onClick.AddListener(() => { pendingGovernment = null; confirmDialog.SetActive(false); });
                        }
                        confirmDialog.SetActive(true);
                    });
                }
            }
            else
            {
                // No governments available
                if (governmentsHeaderText != null) governmentsHeaderText.text = "No Governments Available";
            }
        }

        // Policies
    if (policiesContentRoot != null && PolicyManager.Instance != null)
        {
            var policies = PolicyManager.Instance.GetAvailablePolicies(civ);
            if (policies != null && policies.Count > 0)
            {
                foreach (var p in policies)
                {
                    // Create a simple row: Text + Button
                    var rowGO = new GameObject($"Policy_{p.policyName}");
                    rowGO.transform.SetParent(policiesContentRoot, false);
                    spawned.Add(rowGO);

                    // Add Horizontal Layout Group
                    var layout = rowGO.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                    layout.childControlWidth = true;
                    layout.childControlHeight = true;
                    layout.childForceExpandWidth = false;
                    layout.childForceExpandHeight = false;

                    // Add Text
                    var textGO = new GameObject("Text");
                    textGO.transform.SetParent(rowGO.transform, false);
                    var txt = textGO.AddComponent<TextMeshProUGUI>();
                    txt.text = p.policyName + "\n" + p.description;
                    txt.fontSize = 18;
                    txt.color = Color.white;
                    var textRect = textGO.AddComponent<RectTransform>();
                    textRect.sizeDelta = new Vector2(200, 60); // Increased height for description

                    // Add Button
                    var btnGO = new GameObject("Button");
                    btnGO.transform.SetParent(rowGO.transform, false);
                    var btn = btnGO.AddComponent<Button>();
                    var btnTxt = btnGO.AddComponent<TextMeshProUGUI>();
                    btnTxt.text = "Adopt";
                    btnTxt.fontSize = 16;
                    btnTxt.color = Color.black;
                    btnTxt.alignment = TextAlignmentOptions.Center;
                    var btnRect = btnGO.AddComponent<RectTransform>();
                    btnRect.sizeDelta = new Vector2(80, 30);

                    // Wire button to confirmation dialog
                    btn.interactable = PolicyManager.Instance.GetAvailablePolicies(civ).Contains(p);
                    btn.onClick.AddListener(() => {
                        pendingPolicy = p;
                        pendingGovernment = null;
                        EnsureRuntimeUI();
                        if (confirmMessageText != null) confirmMessageText.text = $"Adopt policy '{p.policyName}'? Cost: {p.policyPointCost} policy points.\n{p.description}";
                        // Populate an effects breakdown in the confirm dialog when available
                        PopulateConfirmDialogEffects(null, p);
                        if (confirmOkButton != null)
                        {
                            confirmOkButton.onClick.RemoveAllListeners();
                            confirmOkButton.onClick.AddListener(() => {
                                TryAdoptPolicy(pendingPolicy);
                                pendingPolicy = null;
                                confirmDialog.SetActive(false);
                            });
                        }
                        if (confirmCancelButton != null)
                        {
                            confirmCancelButton.onClick.RemoveAllListeners();
                            confirmCancelButton.onClick.AddListener(() => { pendingPolicy = null; confirmDialog.SetActive(false); });
                        }
                        confirmDialog.SetActive(true);
                    });
                }
            }
            else
            {
                // No policies available
                if (policiesHeaderText != null) policiesHeaderText.text = "No Policies Available";
            }
        }
    }

    private void TryChangeGovernment(GovernmentData g)
    {
        if (civ == null || g == null || PolicyManager.Instance == null) return;
        var ok = PolicyManager.Instance.ChangeGovernment(civ, g);
        if (ok) RefreshAll();
    }

    private void TryAdoptPolicy(PolicyData p)
    {
        if (civ == null || p == null || PolicyManager.Instance == null) return;
        var ok = PolicyManager.Instance.AdoptPolicy(civ, p);
        if (ok) RefreshAll();
    }

    /// <summary>
    /// Close/hide the government panel and clear spawned content.
    /// </summary>
    public void Close()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        ClearSpawned();
        civ = null;
    }

    private void ClearSpawned()
    {
        for (int i = spawned.Count - 1; i >= 0; i--)
            Destroy(spawned[i]);
        spawned.Clear();

        // Hide headers when clearing
    if (governmentsHeaderText != null) governmentsHeaderText.gameObject.SetActive(false);
        if (policiesHeaderText != null) policiesHeaderText.gameObject.SetActive(false);
    }

    void OnDisable() => ClearSpawned();

    /// <summary>
    /// Populate the confirm dialog's effects container and icon based on the pending government or policy.
    /// If a prefab provides an EffectsContainer or Icon, those will be used; otherwise this will do nothing.
    /// </summary>
    private void PopulateConfirmDialogEffects(GovernmentData gov, PolicyData pol)
    {
        if (confirmDialog == null) return;

        // Clear previous effect entries
        if (confirmEffectsContainer != null)
        {
            for (int i = confirmEffectsContainer.childCount - 1; i >= 0; i--)
                Destroy(confirmEffectsContainer.GetChild(i).gameObject);
        }

        // If an icon is available and dialog contains an image placeholder, set it
        if (confirmIconImage != null)
        {
            Sprite icon = pol != null ? pol.icon : null;
            confirmIconImage.gameObject.SetActive(icon != null);
            if (icon != null) confirmIconImage.sprite = icon;
        }

        // Build a small human-readable list of bonuses
        if (confirmEffectsContainer == null) return;

        System.Action<string> addLine = (text) => {
            var line = new GameObject("EffectLine");
            line.transform.SetParent(confirmEffectsContainer, false);
            var t = line.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = 14;
            t.color = Color.white;
            var rt = line.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(280, 20);
        };

        if (gov != null)
        {
            addLine($"Attack Bonus: {gov.attackBonus:+0.##;-0.##;0}");
            addLine($"Defense Bonus: {gov.defenseBonus:+0.##;-0.##;0}");
            addLine($"Movement Bonus: {gov.movementBonus:+0.##;-0.##;0}");
            addLine($"Food: {gov.foodModifier:+0.##;-0.##;0}%");
            addLine($"Production: {gov.productionModifier:+0.##;-0.##;0}%");
            addLine($"Gold: {gov.goldModifier:+0.##;-0.##;0}%");
            addLine($"Science: {gov.scienceModifier:+0.##;-0.##;0}%");
            addLine($"Culture: {gov.cultureModifier:+0.##;-0.##;0}%");
            addLine($"Faith: {gov.faithModifier:+0.##;-0.##;0}%");
        }
        else if (pol != null)
        {
            addLine($"Attack Bonus: {pol.attackBonus:+0.##;-0.##;0}");
            addLine($"Defense Bonus: {pol.defenseBonus:+0.##;-0.##;0}");
            addLine($"Movement Bonus: {pol.movementBonus:+0.##;-0.##;0}");
            addLine($"Food: {pol.foodModifier:+0.##;-0.##;0}%");
            addLine($"Production: {pol.productionModifier:+0.##;-0.##;0}%");
            addLine($"Gold: {pol.goldModifier:+0.##;-0.##;0}%");
            addLine($"Science: {pol.scienceModifier:+0.##;-0.##;0}%");
            addLine($"Culture: {pol.cultureModifier:+0.##;-0.##;0}%");
            addLine($"Faith: {pol.faithModifier:+0.##;-0.##;0}%");
        }
    }
}
