using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class VictoryProgressPanel : MonoBehaviour
{
    [Serializable]
    private sealed class Row
    {
        public VictoryType type;
        public TMP_Text value;
        public RectTransform fill;

        public void Display(VictoryProgress progress, bool available)
        {
            string text = available ? $"{progress.Current} / {progress.Total}" : "—";
            if (value.text != text)
                value.text = text;
            fill.anchorMax = new Vector2(Mathf.Clamp01(progress.Fraction), 1f);
        }
    }

    [SerializeField] private GameObject panel;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Row[] rows;
    private float nextRefresh;

    private void OnDisable() => panel.SetActive(false);

    private void Update()
    {
        var game = GameManager.Instance;
        bool available = game != null && game.gameInProgress && !game.gamePaused &&
            !game.IsRestoringSave && !game.IsMatchConcluded &&
            GameInteractionStateService.Instance?.Mode == GameInteractionMode.Campaign &&
            VictoryManager.Instance != null && VictoryManager.Instance.IsInitialized &&
            CivilizationManager.Instance != null && CivilizationManager.Instance.playerCiv != null;
        if (openButton.gameObject.activeSelf != available)
            openButton.gameObject.SetActive(available);
        if (!available)
        {
            if (panel.activeSelf)
                panel.SetActive(false);
            return;
        }
        if (panel.activeSelf && Time.unscaledTime >= nextRefresh)
            Refresh();
    }

    public void Toggle()
    {
        if (panel.activeSelf)
        {
            Close();
            return;
        }
        panel.SetActive(true);
        Refresh();
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(closeButton.gameObject);
    }

    public void Close()
    {
        panel.SetActive(false);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(openButton.gameObject);
    }

    private void Refresh()
    {
        nextRefresh = Time.unscaledTime + 0.2f;
        var manager = VictoryManager.Instance;
        var player = CivilizationManager.Instance != null ? CivilizationManager.Instance.playerCiv : null;
        bool ready = manager != null && manager.IsInitialized && player != null;
        foreach (var row in rows)
            row.Display(ready ? manager.GetProgress(player, row.type) : default, ready);
    }
}
