using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MissionSelectionPopupUI : MonoBehaviour
{
    [Serializable]
    public class OptionData
    {
        public string title;
        public string body;
        public Sprite splash;
        public bool interactable = true;
    }

    [Serializable]
    private class OptionSlot
    {
        public GameObject root;
        public TextMeshProUGUI titleText;
        public Image splashImage;
        public TextMeshProUGUI bodyText;
        public Button chooseButton;
    }

    [Header("UI References")]
    [SerializeField] private GameObject popupRoot;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI subtitleText;
    [SerializeField] private OptionSlot[] optionSlots = new OptionSlot[4];

    private Action<int> selectCallback;

    public bool IsVisible => popupRoot != null && popupRoot.activeSelf;

    private void Awake()
    {
        Hide();
    }

    public void Show(
        string title,
        string subtitle,
        IReadOnlyList<OptionData> options,
        Action<int> onSelected)
    {
        selectCallback = onSelected;

        if (titleText != null)
            titleText.text = string.IsNullOrWhiteSpace(title) ? string.Empty : title;

        if (subtitleText != null)
            subtitleText.text = string.IsNullOrWhiteSpace(subtitle) ? string.Empty : subtitle;

        // Defer/close option removed; player must choose one of the options.

        for (int i = 0; i < optionSlots.Length; i++)
        {
            var slot = optionSlots[i];
            var data = options != null && i < options.Count ? options[i] : null;
            ConfigureSlot(slot, data, i);
        }

        if (popupRoot != null)
            popupRoot.SetActive(true);
    }

    public void Hide()
    {
        selectCallback = null;
        

        foreach (var slot in optionSlots)
        {
            if (slot?.chooseButton != null)
                slot.chooseButton.onClick.RemoveAllListeners();
        }

        if (popupRoot != null)
            popupRoot.SetActive(false);
    }

    private void ConfigureSlot(OptionSlot slot, OptionData data, int index)
    {
        if (slot == null || slot.root == null) return;

        bool visible = data != null;
        slot.root.SetActive(visible);
        if (!visible) return;

        if (slot.titleText != null)
            slot.titleText.text = string.IsNullOrWhiteSpace(data.title) ? string.Empty : data.title;

        if (slot.bodyText != null)
            slot.bodyText.text = string.IsNullOrWhiteSpace(data.body) ? string.Empty : data.body;

        if (slot.splashImage != null)
        {
            slot.splashImage.sprite = data.splash;
            slot.splashImage.gameObject.SetActive(data.splash != null);
        }

        // Do not override per-option choose button text here; set it manually on the prefab.

        if (slot.chooseButton != null)
        {
            slot.chooseButton.interactable = data.interactable;
            slot.chooseButton.onClick.RemoveAllListeners();
            slot.chooseButton.onClick.AddListener(() => HandleSelected(index));
        }
    }

    private void HandleSelected(int index)
    {
        var callback = selectCallback;
        callback?.Invoke(index);
    }
}