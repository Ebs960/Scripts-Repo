using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Shows and hides the pause panel saved in the Pause Menu prefab.</summary>
public sealed class PauseMenuPresentation : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text status;
    [SerializeField] private Button saveButton;
    [SerializeField] private Button resumeButton;

    public void Show(bool canSave)
    {
        if (root == null) return;
        root.SetActive(true);
        if (saveButton != null) saveButton.interactable = canSave;
        if (status != null)
            status.text = canSave ? string.Empty : "Saving is unavailable during tactical encounters.";
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(resumeButton != null ? resumeButton.gameObject : null);
    }

    public void Hide()
    {
        if (root != null)
            root.SetActive(false);
    }
}
