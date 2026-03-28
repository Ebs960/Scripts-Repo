using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MissionNarrativePopupUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private GameObject popupRoot;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private Image splashImage;
    [SerializeField] private TextMeshProUGUI bodyText;
    [SerializeField] private Button okButton;

    [Header("Optional Reward Section")]
    [SerializeField] private GameObject rewardSection;
    [SerializeField] private Image rewardImage;
    [SerializeField] private TextMeshProUGUI rewardTitleText;
    [SerializeField] private TextMeshProUGUI rewardBodyText;

    private Action closeCallback;

    public bool IsVisible => popupRoot != null && popupRoot.activeSelf;

    private void Awake()
    {
        Hide();
    }

    private void OnEnable()
    {
        if (okButton != null)
            okButton.onClick.AddListener(HandleOkClicked);
    }

    private void OnDisable()
    {
        if (okButton != null)
            okButton.onClick.RemoveListener(HandleOkClicked);
    }

    public void Show(
        string title,
        string body,
        Sprite splash,
        string rewardTitle,
        string rewardBody,
        Sprite rewardSprite,
        Action onClosed,
        string buttonLabel = "OK")
    {
        closeCallback = onClosed;

        if (titleText != null)
            titleText.text = string.IsNullOrWhiteSpace(title) ? string.Empty : title;

        if (bodyText != null)
            bodyText.text = string.IsNullOrWhiteSpace(body) ? string.Empty : body;

        if (splashImage != null)
        {
            splashImage.sprite = splash;
            splashImage.gameObject.SetActive(splash != null);
        }

        // Intentionally do not override the OK button's text here so designers
        // can set it manually on the prefab in the editor.

        bool hasReward = !string.IsNullOrWhiteSpace(rewardTitle)
            || !string.IsNullOrWhiteSpace(rewardBody)
            || rewardSprite != null;

        if (rewardSection != null)
            rewardSection.SetActive(hasReward);

        if (rewardImage != null)
        {
            rewardImage.sprite = rewardSprite;
            rewardImage.gameObject.SetActive(rewardSprite != null);
        }

        if (rewardTitleText != null)
            rewardTitleText.text = string.IsNullOrWhiteSpace(rewardTitle) ? string.Empty : rewardTitle;

        if (rewardBodyText != null)
            rewardBodyText.text = string.IsNullOrWhiteSpace(rewardBody) ? string.Empty : rewardBody;

        if (popupRoot != null)
            popupRoot.SetActive(true);
    }

    public void Hide()
    {
        closeCallback = null;
        if (rewardSection != null)
            rewardSection.SetActive(false);
        if (rewardImage != null)
            rewardImage.gameObject.SetActive(false);
        if (splashImage != null)
            splashImage.gameObject.SetActive(false);
        if (popupRoot != null)
            popupRoot.SetActive(false);
    }

    private void HandleOkClicked()
    {
        var callback = closeCallback;
        closeCallback = null;

        if (popupRoot != null)
            popupRoot.SetActive(false);

        callback?.Invoke();
    }
}