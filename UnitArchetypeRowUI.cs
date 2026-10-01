using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class UnitArchetypeRowUI : MonoBehaviour
{
    [SerializeField] private Image icon;
    [SerializeField] private TMP_Text unitName;
    [SerializeField] private TMP_Text category;
    [SerializeField] private TMP_Text fieldedCount;
    [SerializeField] private GameObject selectedState;
    [SerializeField] private Button button;
    private ScriptableObject data;
    public event Action<ScriptableObject> Clicked;
    private void Awake() { if (button != null) button.onClick.AddListener(OnClick); }
    private void OnDestroy() { if (button != null) button.onClick.RemoveListener(OnClick); }
    private void OnClick() => Clicked?.Invoke(data);
    public void Bind(ScriptableObject archetype, Sprite sprite, string label, string type, int count, bool selected)
    {
        data = archetype; if (icon != null) { icon.sprite = sprite; icon.enabled = sprite != null; }
        if (unitName != null) unitName.text = label; if (category != null) category.text = type;
        if (fieldedCount != null) fieldedCount.text = count.ToString(); if (selectedState != null) selectedState.SetActive(selected);
    }
}
