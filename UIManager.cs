using UnityEngine;
using System.Collections.Generic;
using TMPro;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("UI Panels")]
    public GameObject notificationPanel;
    public GameObject cityPanel;
    public GameObject techPanel;
    public GameObject culturePanel;
    public GameObject religionPanel;
    public GameObject tradePanel;
    public GameObject diplomacyPanel;
    public GameObject unitInfoPanel;
    public GameObject playerUI;

    [Header("Notification Settings")]
    public float notificationDuration = 3f;
    private Coroutine notificationCoroutine;

    private Dictionary<string, GameObject> panelDict;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        panelDict = new Dictionary<string, GameObject>
        {
            { "NotificationPanel", notificationPanel },
            { "notificationPanel", notificationPanel },
            { "CityPanel", cityPanel },
            { "cityPanel", cityPanel },
            { "TechPanel", techPanel },
            { "techPanel", techPanel },
            { "CulturePanel", culturePanel },
            { "culturePanel", culturePanel },
            { "ReligionPanel", religionPanel },
            { "religionPanel", religionPanel },
            { "TradePanel", tradePanel },
            { "tradePanel", tradePanel },
            { "DiplomacyPanel", diplomacyPanel },
            { "diplomacyPanel", diplomacyPanel },
            { "UnitInfoPanel", unitInfoPanel },
            { "unitInfoPanel", unitInfoPanel },
            { "PlayerUI", playerUI },
            { "playerUI", playerUI }
        };
        HideAllPanels();
        
        // Keep PlayerUI active - it should be visible at game start
        if (playerUI != null) playerUI.SetActive(true);
    }

    /// <summary>
    /// Show a panel by name (e.g. "CityPanel"). Hides all others first.
    /// </summary>
    public void ShowPanel(string name)
    {
        HideAllPanels();
        if (!panelDict.TryGetValue(name, out var panel))
            panelDict.TryGetValue(name.ToLowerInvariant(), out panel);
        if (panel != null)
            panel.SetActive(true);
    }

    /// <summary>
    /// Hide a panel by name.
    /// </summary>
    public void HidePanel(string name)
    {
        if (!panelDict.TryGetValue(name, out var panel))
            panelDict.TryGetValue(name.ToLowerInvariant(), out panel);
        if (panel != null)
            panel.SetActive(false);
    }

    /// <summary>
    /// Hide all panels managed by the UIManager.
    /// </summary>
    public void HideAllPanels()
    {
        foreach (var panel in panelDict.Values)
        {
            if (panel != null && panel != playerUI)
                panel.SetActive(false);
        }

        // Always keep the main PlayerUI visible
        if (playerUI != null)
            playerUI.SetActive(true);
    }

    /// <summary>
    /// Get a panel GameObject by name.
    /// </summary>
    public GameObject GetPanel(string name)
    {
        if (!panelDict.TryGetValue(name, out var panel))
            panelDict.TryGetValue(name.ToLowerInvariant(), out panel);
        return panel;
    }

    /// <summary>
    /// Show a notification message to the player. Displays the notificationPanel and auto-hides after duration.
    /// </summary>
    public void ShowNotification(string message)
    {
        if (notificationPanel == null)
        {
            Debug.LogWarning("UIManager: notificationPanel is not assigned!");
            return;
        }
        // Try to find a TextMeshProUGUI or Text component on the panel
        var tmpText = notificationPanel.GetComponentInChildren<TextMeshProUGUI>();
        if (tmpText != null)
            tmpText.text = message;
        else
        {
            var uiText = notificationPanel.GetComponentInChildren<Text>();
            if (uiText != null)
                uiText.text = message;
        }
        notificationPanel.SetActive(true);
        if (notificationCoroutine != null)
            StopCoroutine(notificationCoroutine);
        notificationCoroutine = StartCoroutine(HideNotificationAfterDelay());
    }

    private System.Collections.IEnumerator HideNotificationAfterDelay()
    {
        yield return new WaitForSeconds(notificationDuration);
        if (notificationPanel != null)
            notificationPanel.SetActive(false);
    }

    // --- Additional helper methods previously handled by GameManager ---

    public void ShowTechPanel(Civilization civ)
    {
        if (techPanel == null) return;
        var techUI = techPanel.GetComponent<TechUI>();
        if (techUI != null)
            techUI.Show(civ);
        else
            ShowPanel("TechPanel");
    }

    public void HideTechPanel()
    {
        HidePanel("TechPanel");
    }

    public void ShowCulturePanel(Civilization civ)
    {
        if (culturePanel == null) return;
        var cultureUI = culturePanel.GetComponent<CultureUI>();
        if (cultureUI != null)
            cultureUI.Show(civ);
        else
            ShowPanel("CulturePanel");
    }

    public void HideCulturePanel()
    {
        HidePanel("CulturePanel");
    }

    public void ShowTradePanel(Civilization civ)
    {
        if (tradePanel == null) return;
        var tradeUI = tradePanel.GetComponent<TradePanel>();
        if (tradeUI != null)
            tradeUI.Show(civ);
        ShowPanel("TradePanel");
    }

    public void ShowUnitInfoPanelForUnit(object unit)
    {
        if (unitInfoPanel == null || unit == null) return;
        var infoUI = unitInfoPanel.GetComponent<UnitInfoPanel>();
        if (infoUI != null)
            infoUI.ShowPanel(unit);
        ShowPanel("UnitInfoPanel");
    }

    public void HideUnitInfoPanel()
    {
        if (unitInfoPanel == null) return;
        var infoUI = unitInfoPanel.GetComponent<UnitInfoPanel>();
        if (infoUI != null)
            infoUI.HidePanel();
        HidePanel("UnitInfoPanel");
    }

    /// <summary>
    /// Deselect any currently selected unit and hide the unit info panel.
    /// Convenience wrapper so UI buttons can deselect via UIManager.
    /// </summary>
    public void DeselectUnit()
    {
        if (UnitSelectionManager.Instance != null)
            UnitSelectionManager.Instance.DeselectUnit();
        HideUnitInfoPanel();
    }
}