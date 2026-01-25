using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class ArmyInfoPanel : MonoBehaviour
{
    [Header("UI Elements")]
    public GameObject panelRoot; // assign the root so we can toggle visibility
    public TextMeshProUGUI generalNameText;
    public TextMeshProUGUI soldierCountText;
    public TextMeshProUGUI movePointsText;

    [Header("Unit List")]
    public Transform unitListContainer; // parent for unit entry buttons
    public GameObject unitEntryPrefab;   // prefab should contain a Button, a TextMeshProUGUI for name, a TextMeshProUGUI for count, and an Image for icon

    private readonly List<GameObject> spawnedEntries = new List<GameObject>();
    
    [Header("Slide Animation")]
    public RectTransform panelRect; // the RectTransform that will slide (usually same as panelRoot)
    public float slideDuration = 0.22f;
    public Vector2 hiddenOffset = new Vector2(-380f, 0f); // offset from visible anchored position to hide

    private Vector2 visiblePos;
    private Vector2 hiddenPos;
    private Coroutine slideCoroutine;

    void Awake()
    {
        // Attempt to grab RectTransform from panelRoot if not assigned
        if (panelRect == null && panelRoot != null)
            panelRect = panelRoot.GetComponent<RectTransform>();

        if (panelRect != null)
        {
            visiblePos = panelRect.anchoredPosition;
            hiddenPos = visiblePos + hiddenOffset;

            // Start hidden
            panelRect.anchoredPosition = hiddenPos;
        }

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    public void ShowPanel(Army army)
    {
        if (army == null || panelRoot == null) return;
        // Ensure panel is active and positioned offscreen before sliding in
        panelRoot.SetActive(true);
        if (panelRect != null)
        {
            panelRect.anchoredPosition = hiddenPos;
            StartSlide(visiblePos);
        }
        else
        {
            // Fallback: immediately show
            if (panelRect == null && panelRoot != null)
                panelRoot.SetActive(true);
        }

        if (generalNameText != null)
        {
            if (army.general != null && army.general.data != null)
                generalNameText.text = army.general.data.unitName;
            else
                generalNameText.text = "(No General)";
        }

        if (soldierCountText != null)
        {
            soldierCountText.text = army.totalSoldierCount.ToString();
        }

        if (movePointsText != null)
        {
            movePointsText.text = army.currentMovePoints + " / " + army.baseMovePoints;
        }

        PopulateUnitList(army);
    }

    public void HidePanel()
    {
        if (panelRoot == null) return;
        // Slide out then deactivate and clear list
        if (panelRect != null)
        {
            StartSlide(hiddenPos, () =>
            {
                panelRoot.SetActive(false);
                ClearUnitList();
            });
        }
        else
        {
            panelRoot.SetActive(false);
            ClearUnitList();
        }
    }

    private void StartSlide(Vector2 target, System.Action onComplete = null)
    {
        if (slideCoroutine != null)
            StopCoroutine(slideCoroutine);
        slideCoroutine = StartCoroutine(SlideTo(target, onComplete));
    }

    private System.Collections.IEnumerator SlideTo(Vector2 target, System.Action onComplete = null)
    {
        if (panelRect == null)
        {
            onComplete?.Invoke();
            yield break;
        }

        Vector2 start = panelRect.anchoredPosition;
        float t = 0f;
        float dur = Mathf.Max(0.0001f, slideDuration);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float f = Mathf.SmoothStep(0f, 1f, t / dur);
            panelRect.anchoredPosition = Vector2.Lerp(start, target, f);
            yield return null;
        }
        panelRect.anchoredPosition = target;
        slideCoroutine = null;
        onComplete?.Invoke();
    }

    private void PopulateUnitList(Army army)
    {
        ClearUnitList();
        if (unitListContainer == null || unitEntryPrefab == null) return;

        foreach (var unit in army.units)
        {
            if (unit == null) continue;
            var entry = Instantiate(unitEntryPrefab, unitListContainer);
            spawnedEntries.Add(entry);

            // Find TMP fields and icon image in the prefab. We assume first TMP is name, second TMP is count.
            var tmpFields = entry.GetComponentsInChildren<TextMeshProUGUI>(true);
            TextMeshProUGUI nameTMP = tmpFields.Length > 0 ? tmpFields[0] : null;
            TextMeshProUGUI countTMP = tmpFields.Length > 1 ? tmpFields[1] : null;
            var iconImg = entry.GetComponentInChildren<Image>(true);
            var button = entry.GetComponent<Button>();

            if (nameTMP != null)
                nameTMP.text = unit.data != null ? unit.data.unitName : unit.UnitName;

            if (countTMP != null)
                countTMP.text = $"{unit.soldierCount} / {unit.maxSoldierCount}";

            if (iconImg != null && unit.data != null)
                iconImg.sprite = unit.data.icon;

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                var captured = unit; // capture for closure
                button.onClick.AddListener(() =>
                {
                    // Show unit details using existing UI, if available
                    if (UIManager.Instance != null)
                        UIManager.Instance.ShowUnitInfoPanelForUnit(captured);
                });
            }
        }
    }

    private void ClearUnitList()
    {
        for (int i = spawnedEntries.Count - 1; i >= 0; i--)
        {
            if (spawnedEntries[i] != null)
                Destroy(spawnedEntries[i]);
        }
        spawnedEntries.Clear();
    }
}
