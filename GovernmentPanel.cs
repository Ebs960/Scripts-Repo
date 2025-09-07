// (duplicate removed) - file contains a single GovernmentPanel class above
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Unified Government & Policy panel. Shows available governments and available policies for a selected civilization.
/// Prefabs: policyEntryPrefab (PolicyEntry) and governorEntryPrefab (a simple row with Text + Button).
/// </summary>
public class GovernmentPanel : MonoBehaviour
{
    [Header("Root")]
    public GameObject panelRoot;
    public TextMeshProUGUI headerText;

    [Header("Governors Section")]
    public Transform governorsContentRoot;
    public GameObject governorEntryPrefab; // simple row with Text + Button

    [Header("Policies Section")]
    public Transform policiesContentRoot;
    public GameObject policyEntryPrefab; // should have PolicyEntry component

    Civilization civ;
    List<GameObject> spawned = new List<GameObject>();

    public void ShowForCivilization(Civilization civ)
    {
        this.civ = civ;
        if (panelRoot != null) panelRoot.SetActive(true);
        if (headerText != null)
            headerText.text = civ != null ? ( (civ.civData != null ? civ.civData.civName : civ.gameObject.name) + " - Government & Policies" ) : "Government & Policies";
        RefreshAll();
    }

    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    public void RefreshAll()
    {
        ClearSpawned();
        if (civ == null) return;

        // Governments
        if (governorsContentRoot != null && governorEntryPrefab != null && PolicyManager.Instance != null)
        {
            var govs = PolicyManager.Instance.GetAvailableGovernments(civ);
            if (govs != null)
            {
                foreach (var g in govs)
                {
                    var go = Instantiate(governorEntryPrefab, governorsContentRoot);
                    spawned.Add(go);
                    var txt = go.GetComponentInChildren<TextMeshProUGUI>();
                    var btn = go.GetComponentInChildren<Button>();
                    if (txt != null) txt.text = g.governmentName;
                    if (btn != null)
                    {
                        btn.onClick.RemoveAllListeners();
                        btn.onClick.AddListener(() => { TryChangeGovernment(g); });
                        btn.interactable = PolicyManager.Instance.GetAvailableGovernments(civ).Contains(g);
                    }
                }
            }
        }

        // Policies
        if (policiesContentRoot != null && policyEntryPrefab != null && PolicyManager.Instance != null)
        {
            var policies = PolicyManager.Instance.GetAvailablePolicies(civ);
            if (policies != null)
            {
                foreach (var p in policies)
                {
                    var go = Instantiate(policyEntryPrefab, policiesContentRoot);
                    spawned.Add(go);
                    var entry = go.GetComponent<PolicyEntry>();
                    if (entry != null) entry.Setup(p, civ);
                    else
                    {
                        var txt = go.GetComponentInChildren<TextMeshProUGUI>();
                        if (txt != null) txt.text = p.policyName;
                    }
                }
            }
        }
    }

    private void TryChangeGovernment(GovernmentData g)
    {
        if (civ == null || g == null || PolicyManager.Instance == null) return;
        var ok = PolicyManager.Instance.ChangeGovernment(civ, g);
        if (ok) RefreshAll();
    }

    private void ClearSpawned()
    {
        for (int i = spawned.Count - 1; i >= 0; i--)
            Destroy(spawned[i]);
        spawned.Clear();
    }

    void OnDisable() => ClearSpawned();
}
