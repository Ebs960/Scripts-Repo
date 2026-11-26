using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Shared save browser for the Main Menu and the Pause Menu. The layout is saved in the
/// Save Load Browser prefab; this fills in the save list and handles the buttons.
/// </summary>
public sealed class SaveLoadMenuUI : MonoBehaviour
{
    public enum BrowserMode { Save, Load }

    private const string PrefabPath = "MenuUI/Save Load Browser";
    private static readonly Color TextColor = new Color(0.93f, 0.91f, 0.86f, 1f);
    private static readonly Color MutedText = new Color(0.70f, 0.76f, 0.79f, 1f);
    private static readonly Color SelectedTint = new Color(1f, 0.8f, 0.46f, 1f);
    private static readonly Color AutosaveTint = new Color(0.74f, 0.78f, 0.84f, 1f);

    [SerializeField] private GameObject root;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private RectTransform rowContainer;
    [SerializeField] private SaveSlotRowView rowTemplate;
    [SerializeField] private TMP_Text status;
    [SerializeField] private TMP_Text detailTitle;
    [SerializeField] private TMP_Text detailText;
    [SerializeField] private Button primaryButton;
    [SerializeField] private TMP_Text primaryLabel;
    [SerializeField] private Button deleteButton;
    [SerializeField] private Button backButton;

    private static SaveLoadMenuUI current;
    private BrowserMode mode;
    private int capacity;
    private MenuUiSkin skin;
    private UnityEngine.Object owner;
    private bool ownerWasAssigned;
    private Func<PauseMenuManager.GameSaveData> captureSave;
    private Action<SaveGameRepository.SaveEntry> loadSave;
    private Action closed;
    private readonly List<SaveGameRepository.SaveEntry> entries = new List<SaveGameRepository.SaveEntry>();
    private readonly List<SaveSlotRowView> rows = new List<SaveSlotRowView>();
    private SaveGameRepository.SaveEntry selected;
    private bool busy;

    public static bool IsVisible => current != null && current.root != null && current.root.activeSelf;

    private bool IsConfigured => root != null && titleText != null && rowContainer != null && rowTemplate != null
        && rowTemplate.button != null && rowTemplate.art != null && rowTemplate.title != null && rowTemplate.subtitle != null
        && status != null && detailTitle != null && detailText != null
        && primaryButton != null && primaryLabel != null && deleteButton != null && backButton != null;

    public static void Show(BrowserMode browserMode, int manualCapacity, MenuUiSkin uiSkin,
        UnityEngine.Object hostOwner, Func<PauseMenuManager.GameSaveData> createSave,
        Action<SaveGameRepository.SaveEntry> onLoad, Action onClose)
    {
        if (current != null)
            current.Close(false);

        var prefab = Resources.Load<GameObject>(PrefabPath);
        if (prefab == null || !prefab.TryGetComponent<SaveLoadMenuUI>(out var view) || !view.IsConfigured)
        {
            Debug.LogError($"Missing or misconfigured save browser prefab at Resources/{PrefabPath}.");
            onClose?.Invoke();
            return;
        }

        current = Instantiate(view);
        current.name = "Shared Save Load Browser";
        current.mode = browserMode;
        current.capacity = SaveGameRepository.GetManualSlotCapacity(manualCapacity);
        current.skin = uiSkin;
        current.owner = hostOwner;
        current.ownerWasAssigned = !ReferenceEquals(hostOwner, null);
        current.captureSave = createSave;
        current.loadSave = onLoad;
        current.closed = onClose;
        current.Bind();
        current.Refresh();
    }

    public static void CloseCurrent()
    {
        if (current != null)
            current.Close(true);
    }

    public static void CloseForTransition()
    {
        if (current != null)
            current.Close(false);
    }

    private void Update()
    {
        if (current == this && Keyboard.current != null && Keyboard.current[Key.Escape].wasPressedThisFrame
            && !ConfirmationDialogService.IsVisible)
            Close(true);
    }

    private void Bind()
    {
        titleText.text = mode == BrowserMode.Save ? "SAVE GAME" : "LOAD GAME";
        rowTemplate.gameObject.SetActive(false);
        deleteButton.onClick.AddListener(DeleteSelected);
        primaryButton.onClick.AddListener(PrimaryAction);
        backButton.onClick.AddListener(() => Close(true));
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void Refresh(string statusMessage = null)
    {
        entries.Clear();
        entries.AddRange(SaveGameRepository.Discover(capacity));
        foreach (var row in rows)
            if (row != null) Destroy(row.gameObject);
        rows.Clear();
        selected = null;

        var preferredOrder = entries.OrderBy(e => e.IsAutosave ? 1 : 0).ThenBy(e => e.SlotIndex).ToList();
        foreach (var entry in preferredOrder)
            CreateRow(entry);

        status.text = statusMessage ?? $"{preferredOrder.Count(e => !e.IsAutosave && !e.IsEmpty)} manual saves, " +
            $"{preferredOrder.Count(e => e.IsAutosave)} autosaves";
        PaintRows();
        UpdateDetails();
        UpdateButtons();

        if (rows.Count > 0 && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(rows[0].button.gameObject);
    }

    private void CreateRow(SaveGameRepository.SaveEntry entry)
    {
        var row = Instantiate(rowTemplate, rowContainer);
        row.name = entry.Id;
        row.gameObject.SetActive(true);
        string tag = entry.IsAutosave ? "[AUTOSAVE] " : string.Empty;
        row.title.text = tag + entry.SaveName;
        row.title.color = entry.IsValid ? TextColor : MutedText;
        row.subtitle.text = entry.IsEmpty
            ? "Empty manual slot"
            : $"{entry.Civilization}  |  Turn {entry.Turn}  |  {entry.Date}";
        row.button.onClick.AddListener(() => Select(entry));
        rows.Add(row);
        if (selected == null && !entry.IsEmpty && entry.IsValid)
            selected = entry;
    }

    private void Select(SaveGameRepository.SaveEntry entry)
    {
        selected = entry;
        PaintRows();
        UpdateDetails();
        UpdateButtons();
    }

    private void PaintRows()
    {
        foreach (var row in rows)
        {
            if (row == null) continue;
            var entry = entries.FirstOrDefault(e => e.Id == row.name);
            bool isSelected = entry != null && selected != null && entry.Id == selected.Id;
            row.art.color = isSelected ? SelectedTint : entry != null && entry.IsAutosave ? AutosaveTint : Color.white;
        }
    }
    private void UpdateDetails()
    {
        if (selected == null)
        {
            detailTitle.text = "Select a save";
            detailText.text = "Choose a save or an empty slot from the list.";
            return;
        }
        if (selected.IsEmpty)
        {
            detailTitle.text = $"SAVE SLOT {selected.SlotIndex + 1}";
            detailText.text = "Empty manual slot\n\nSelect SAVE to create a campaign save here.";
            return;
        }

        string planet = GameManager.Instance != null && selected.Metadata != null
            ? GameManager.Instance.GetPlanetNameForSave(selected.Metadata.currentPlanetIndex)
            : selected.Planet;
        detailTitle.text = selected.SaveName;
        detailText.text = $"Civilization: {selected.Civilization}\n" +
                          $"Turn: {selected.Turn}\n" +
                          $"Map size: {selected.MapSize}\n" +
                          $"Current planet: {planet}\n" +
                          $"Saved: {selected.Date}\n" +
                          $"Type: {(selected.IsAutosave ? "Autosave" : "Manual save")}\n\n" +
                          (selected.IsValid ? selected.CanLoad ? "Status: Ready to load" : "Status: " + selected.Error
                              : "Status: " + selected.Error);
    }

    private void UpdateButtons()
    {
        bool hasSelection = selected != null;
        deleteButton.interactable = hasSelection && !selected.IsEmpty;
        if (mode == BrowserMode.Save)
        {
            primaryButton.interactable = hasSelection && !selected.IsAutosave && !busy;
            primaryLabel.text = "SAVE";
        }
        else
        {
            primaryButton.interactable = hasSelection && !selected.IsEmpty && selected.CanLoad && !busy;
            primaryLabel.text = "LOAD";
        }
    }

    private void PrimaryAction()
    {
        if (busy || selected == null) return;
        if (mode == BrowserMode.Save)
        {
            if (!selected.IsEmpty)
            {
                var overwritten = selected;
                ConfirmationDialogService.ShowConfirmation("OVERWRITE SAVE?",
                    "This will replace the selected save. Continue?", () => WriteSelected(overwritten),
                    confirmLabel: "OVERWRITE", cancelLabel: "CANCEL", owner: this, skin: skin);
            }
            else
                WriteSelected(selected);
            return;
        }

        if (selected.CanLoad)
        {
            try
            {
                SaveGameRepository.Load(selected);
                if (!selected.CanLoad)
                {
                    status.text = selected.Error ?? "This save cannot be restored.";
                    UpdateDetails();
                    UpdateButtons();
                    return;
                }
                loadSave?.Invoke(selected);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[SaveLoadMenuUI] Load validation failed: {ex}");
                status.text = "Load failed: " + ex.Message;
            }
        }
    }

    private void WriteSelected(SaveGameRepository.SaveEntry entry)
    {
        if (busy || entry == null || entry.IsAutosave || OwnerWasDestroyed()) return;
        busy = true;
        UpdateButtons();
        try
        {
            if (captureSave == null)
                throw new InvalidOperationException("The active game cannot create a save.");
            var data = captureSave();
            if (data == null)
                throw new InvalidOperationException("No campaign data was produced.");
            if (string.IsNullOrWhiteSpace(data.saveName))
                data.saveName = $"Campaign - Turn {data.currentTurn}";
            SaveGameRepository.WriteManualSlot(entry.SlotIndex, data);
            Refresh($"Saved successfully to slot {entry.SlotIndex + 1}.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveLoadMenuUI] Save failed: {ex}");
            status.text = "Save failed: " + ex.Message;
        }
        finally
        {
            busy = false;
            UpdateButtons();
        }
    }

    private void DeleteSelected()
    {
        if (busy || selected == null || selected.IsEmpty || !selected.IsValid) return;
        var deleting = selected;
        ConfirmationDialogService.ShowConfirmation("DELETE SAVE?",
            "This save will be permanently deleted.", () =>
            {
                try
                {
                    SaveGameRepository.Delete(deleting);
                    Refresh("Save deleted.");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[SaveLoadMenuUI] Delete failed: {ex}");
                    status.text = "Delete failed: " + ex.Message;
                }
            }, confirmLabel: "DELETE", cancelLabel: "CANCEL", destructive: true, owner: this, skin: skin);
    }

    private void Close(bool invokeCallback)
    {
        if (current == this)
            current = null;
        Action callback = invokeCallback && !OwnerWasDestroyed() ? closed : null;
        closed = null;
        captureSave = null;
        loadSave = null;
        owner = null;
        ownerWasAssigned = false;
        Destroy(gameObject);
        callback?.Invoke();
    }

    private bool OwnerWasDestroyed()
    {
        return ownerWasAssigned && owner == null;
    }

    private void OnDestroy()
    {
        if (current == this)
            current = null;
    }
}
