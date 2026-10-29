using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CivilizationSelectionRow : MonoBehaviour
{
    [SerializeField] private Button selectButton;
    [SerializeField] private Image emblemImage;
    [SerializeField] private Image selectionHighlight;
    [SerializeField] private TMP_Text civilizationNameText;
    [SerializeField] private TMP_Text relationshipText;

    private Civilization civilization;
    private Action<Civilization> onSelected;
    private bool isWired;

    private void Awake()
    {
        if (selectButton == null) selectButton = GetComponent<Button>();
        if (selectButton != null && !isWired)
        {
            selectButton.onClick.AddListener(Select);
            isWired = true;
        }

        if (civilizationNameText == null)
            civilizationNameText = GetComponentInChildren<TMP_Text>(true);
        if (emblemImage == null)
        {
            var emblem = transform.Find("Emblem") ?? transform.Find("CivIcon");
            if (emblem != null) emblemImage = emblem.GetComponent<Image>();
        }
        if (selectionHighlight == null)
        {
            var highlight = transform.Find("SelectionHighlight");
            if (highlight != null) selectionHighlight = highlight.GetComponent<Image>();
        }
        if (relationshipText == null)
        {
            var relationship = transform.Find("RelationshipStatus");
            if (relationship != null) relationshipText = relationship.GetComponent<TMP_Text>();
        }
    }

    public void Bind(Civilization civ, string relationship, bool selected,
        Action<Civilization> selectionCallback, Sprite rowArtwork)
    {
        civilization = civ;
        onSelected = selectionCallback;

        if (civilizationNameText != null)
            civilizationNameText.text = civ != null ? civ.civData?.civName ?? civ.name : string.Empty;
        if (relationshipText != null)
            relationshipText.text = relationship ?? string.Empty;
        if (emblemImage != null)
        {
            emblemImage.sprite = civ != null ? civ.civData?.icon : null;
            emblemImage.gameObject.SetActive(emblemImage.sprite != null);
        }

        if (selectionHighlight != null)
            selectionHighlight.gameObject.SetActive(selected);
        if (selectButton != null)
        {
            selectButton.interactable = civ != null;
            if (rowArtwork != null && selectButton.targetGraphic is Image background)
                background.sprite = rowArtwork;
        }
    }

    private void Select()
    {
        if (civilization != null) onSelected?.Invoke(civilization);
    }
}