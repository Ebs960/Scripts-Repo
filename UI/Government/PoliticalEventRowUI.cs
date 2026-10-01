using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Authored political event card. Option buttons are instantiated from an authored button prefab.</summary>
public class PoliticalEventRowUI : MonoBehaviour
{
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private TMP_Text expiryText;
    [SerializeField] private Transform optionsRoot;
    [SerializeField] private Button optionButtonPrefab;

    private readonly List<Button> optionButtons = new List<Button>();

    public void Bind(PoliticalEventRecord record, Action<int, int> onOption)
    {
        GovernmentUiUtil.SetText(titleText, record.title);
        GovernmentUiUtil.SetText(bodyText, record.body);
        GovernmentUiUtil.SetText(expiryText, $"Expires turn {record.expiryTurn}");

        GovernmentUiUtil.FillList(optionsRoot, optionButtonPrefab, optionButtons, record.options, (button, option) =>
        {
            GovernmentUiUtil.SetText(button.GetComponentInChildren<TMP_Text>(true), option.label);
            int index = record.options.IndexOf(option);
            int eventId = record.id;
            GovernmentUiUtil.SetClick(button, () => onOption?.Invoke(eventId, index));
        });
    }
}
