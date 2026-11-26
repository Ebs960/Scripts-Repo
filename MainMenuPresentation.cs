using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-100)]
public class MainMenuPresentation : MonoBehaviour
{
    [SerializeField] private MainMenuManager menuManager;
    [SerializeField] private TMP_Text version;

    private Button[] buttons;
    private GameObject lastSelected;
    private bool focusPending;

    private void Awake()
    {
        if (menuManager == null) menuManager = GetComponentInParent<MainMenuManager>();
        if (menuManager == null)
        {
            Debug.LogError("[Main Menu] No menu manager assigned to the title screen.", this);
            enabled = false;
            return;
        }

        buttons = new[]
        {
            menuManager.newGameButton, menuManager.loadGameButton,
            menuManager.optionsButton, menuManager.quitGameButton
        };
        if (version != null) version.text = "v" + Application.version;
    }

    private void OnEnable() => focusPending = true;

    private void LateUpdate()
    {
        var events = EventSystem.current;
        if (events == null || buttons == null) return;

        if (!focusPending)
        {
            foreach (var button in buttons)
                if (button != null && events.currentSelectedGameObject == button.gameObject)
                    lastSelected = button.gameObject;
            return;
        }
        if (events.alreadySelecting) return;

        focusPending = false;
        Button first = null;
        foreach (var button in buttons)
        {
            if (button == null || !button.IsActive() || !button.IsInteractable()) continue;
            if (button.gameObject == lastSelected)
            {
                events.SetSelectedGameObject(lastSelected);
                return;
            }
            if (first == null) first = button;
        }
        if (first != null) events.SetSelectedGameObject(first.gameObject);
    }
}
