using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public sealed class SaveLoadMenuUI : MonoBehaviour
{
    public enum BrowserMode { Save, Load }

    private const int SortingOrder = 1000;
    private static readonly Color BackdropColor = new Color(0.015f, 0.023f, 0.035f, 0.94f);
    private static readonly Color ShellColor = new Color(0.043f, 0.075f, 0.11f, 1f);
    private static readonly Color PanelColor = new Color(0.065f, 0.11f, 0.16f, 1f);
    private static readonly Color RowColor = new Color(0.09f, 0.14f, 0.19f, 1f);
    private static readonly Color Gold = new Color(0.72f, 0.52f, 0.21f, 1f);
    private static readonly Color TextColor = new Color(0.93f, 0.91f, 0.86f, 1f);
    private static readonly Color MutedText = new Color(0.70f, 0.76f, 0.79f, 1f);

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
    private readonly List<Button> rowButtons = new List<Button>();
    private SaveGameRepository.SaveEntry selected;
    private GameObject root;
    private RectTransform rowContainer;
    private TMP_Text status;
    private TMP_Text detailTitle;
    private TMP_Text detailText;
    private Button primaryButton;
    private Button deleteButton;
    private bool busy;

    public static bool IsVisible => current != null && current.root != null && current.root.activeSelf;

    public static void Show(BrowserMode browserMode, int manualCapacity, MenuUiSkin uiSkin,
        UnityEngine.Object hostOwner, Func<PauseMenuManager.GameSaveData> createSave,
        Action<SaveGameRepository.SaveEntry> onLoad, Action onClose)
    {
        if (current != null)
            current.Close(false);

        var host = new GameObject("Shared Save Load Browser");
        current = host.AddComponent<SaveLoadMenuUI>();
        current.mode = browserMode;
        current.capacity = SaveGameRepository.GetManualSlotCapacity(manualCapacity);
        current.skin = uiSkin;
        current.owner = hostOwner;
        current.ownerWasAssigned = !ReferenceEquals(hostOwner, null);
        current.captureSave = createSave;
        current.loadSave = onLoad;
        current.closed = onClose;
        current.Build();
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
        if (Keyboard.current != null && Keyboard.current[Key.Escape].wasPressedThisFrame
            && !ConfirmationDialogService.IsVisible)
            Close(true);
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.overrideSorting = true;
        canvas.sortingOrder = SortingOrder;
        gameObject.AddComponent<GraphicRaycaster>();
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        root = Rect("Backdrop", transform, Vector2.zero, Vector2.one);
        var backdrop = root.AddComponent<Image>();
        backdrop.color = BackdropColor;
        backdrop.raycastTarget = true;

        var shell = Rect("Save Load Shell", root.transform, new Vector2(0.04f, 0.05f), new Vector2(0.96f, 0.95f));
        var shellRect = shell.GetComponent<RectTransform>();
        shellRect.sizeDelta = new Vector2(-30, -20);
        var shellImage = shell.AddComponent<Image>();
        shellImage.color = ShellColor;
        ApplySprite(shellImage, skin != null ? skin.saveLoadShell : null);
        var shellOutline = shell.AddComponent<Outline>();
        shellOutline.effectColor = Gold;
        shellOutline.effectDistance = new Vector2(2, 2);
        shellOutline.useGraphicAlpha = false;
        var layout = shell.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(34, 34, 24, 22);
        layout.spacing = 16;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var header = Panel("Header", shell.transform, new Vector2(0, 78));
        var headerLayout = header.AddComponent<HorizontalLayoutGroup>();
        headerLayout.padding = new RectOffset(22, 18, 8, 8);
        headerLayout.childAlignment = TextAnchor.MiddleLeft;
        headerLayout.childControlHeight = true;
        headerLayout.childControlWidth = true;
        headerLayout.childForceExpandWidth = true;
        headerLayout.childForceExpandHeight = true;
        Label(header.transform, mode == BrowserMode.Save ? "SAVE GAME" : "LOAD GAME", 36, TextColor, FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft);

        var body = Panel("Browser Body", shell.transform, new Vector2(0, 0), true);
        var bodyLayout = body.AddComponent<HorizontalLayoutGroup>();
        bodyLayout.spacing = 20;
        bodyLayout.childControlHeight = true;
        bodyLayout.childControlWidth = true;
        bodyLayout.childForceExpandHeight = true;
        bodyLayout.childForceExpandWidth = false;

        var listPanel = Panel("Save List", body.transform, new Vector2(680, 0), true);
        listPanel.AddComponent<LayoutElement>().flexibleWidth = 6;
        var listLayout = listPanel.AddComponent<VerticalLayoutGroup>();
        listLayout.padding = new RectOffset(14, 14, 14, 14);
        listLayout.spacing = 10;
        listLayout.childControlWidth = true;
        listLayout.childControlHeight = false;
        listLayout.childForceExpandWidth = true;
        listLayout.childForceExpandHeight = false;
        var scrollObject = new GameObject("Scroll View", typeof(RectTransform), typeof(Image), typeof(ScrollRect));
        scrollObject.transform.SetParent(listPanel.transform, false);
        scrollObject.GetComponent<Image>().color = new Color(0, 0, 0, 0);
        scrollObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        var viewport = Rect("Viewport", scrollObject.transform, Vector2.zero, Vector2.one);
        var viewportImage = viewport.AddComponent<Image>();
        viewportImage.color = Color.white;
        viewportImage.raycastTarget = true;
        viewport.AddComponent<Mask>().showMaskGraphic = false;
        var contentObject = Rect("Content", viewport.transform, new Vector2(0, 1), new Vector2(1, 1));
        contentObject.GetComponent<RectTransform>().pivot = new Vector2(0.5f, 1f);
        rowContainer = contentObject.GetComponent<RectTransform>();
        var rowsLayout = contentObject.AddComponent<VerticalLayoutGroup>();
        rowsLayout.spacing = 8;
        rowsLayout.childControlWidth = true;
        rowsLayout.childControlHeight = true;
        rowsLayout.childForceExpandWidth = true;
        rowsLayout.childForceExpandHeight = false;
        contentObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        var scroll = scrollObject.GetComponent<ScrollRect>();
        scroll.viewport = viewport.GetComponent<RectTransform>();
        scroll.content = rowContainer;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;

        var details = Panel("Save Details", body.transform, new Vector2(500, 0), true);
        details.AddComponent<LayoutElement>().flexibleWidth = 4;
        var detailLayout = details.AddComponent<VerticalLayoutGroup>();
        detailLayout.padding = new RectOffset(25, 25, 24, 24);
        detailLayout.spacing = 18;
        detailLayout.childControlWidth = true;
        detailLayout.childControlHeight = false;
        detailLayout.childForceExpandWidth = true;
        detailTitle = Label(details.transform, "Select a save", 28, TextColor, FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft);
        detailTitle.gameObject.AddComponent<LayoutElement>().preferredHeight = 45;
        detailText = Label(details.transform, "Choose a save or an empty slot from the list.", 21, MutedText,
            FontStyles.Normal, TextAlignmentOptions.TopLeft);
        detailText.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1;
        detailText.enableWordWrapping = true;

        var footer = Panel("Footer", shell.transform, new Vector2(0, 90));
        var footerLayout = footer.AddComponent<HorizontalLayoutGroup>();
        footerLayout.spacing = 12;
        footerLayout.childAlignment = TextAnchor.MiddleRight;
        footerLayout.childControlHeight = true;
        footerLayout.childControlWidth = false;
        footerLayout.childForceExpandHeight = true;
        footerLayout.childForceExpandWidth = false;
        status = Label(footer.transform, "", 18, MutedText, FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        status.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1;
        deleteButton = AddButton(footer.transform, "DELETE", new Vector2(150, 58), new Color(0.17f, 0.19f, 0.19f, 1f),
            skin != null ? skin.cancelButton : null, DeleteSelected);
        primaryButton = AddButton(footer.transform, mode == BrowserMode.Save ? "SAVE" : "LOAD",
            new Vector2(170, 58), Gold, skin != null ? skin.genericButton : null, PrimaryAction);
        AddButton(footer.transform, "BACK", new Vector2(150, 58), new Color(0.08f, 0.13f, 0.19f, 1f),
            skin != null ? skin.cancelButton : null, () => Close(true));
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    private void Refresh(string statusMessage = null)
    {
        entries.Clear();
        entries.AddRange(SaveGameRepository.Discover(capacity));
        foreach (Transform child in rowContainer)
            Destroy(child.gameObject);
        rowButtons.Clear();
        selected = null;

        var preferredOrder = entries.OrderBy(e => e.IsAutosave ? 1 : 0).ThenBy(e => e.SlotIndex).ToList();
        foreach (var entry in preferredOrder)
            CreateRow(entry);

        status.text = statusMessage ?? $"{preferredOrder.Count(e => !e.IsAutosave && !e.IsEmpty)} manual saves, " +
            $"{preferredOrder.Count(e => e.IsAutosave)} autosaves";
        UpdateDetails();
        UpdateButtons();

        if (rowButtons.Count > 0 && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(rowButtons[0].gameObject);
    }

    private void CreateRow(SaveGameRepository.SaveEntry entry)
    {
        var row = Rect(entry.Id, rowContainer, Vector2.zero, Vector2.one);
        row.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 84);
        row.AddComponent<LayoutElement>().preferredHeight = 84;
        var image = row.AddComponent<Image>();
        image.color = entry.IsAutosave ? new Color(0.11f, 0.14f, 0.16f, 1f) : RowColor;
        ApplySprite(image, skin != null ? skin.saveSlotRow : null);
        var button = row.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        button.onClick.AddListener(() => Select(entry));
        var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.padding = new RectOffset(16, 16, 8, 8);
        rowLayout.spacing = 14;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        rowLayout.childControlHeight = true;
        rowLayout.childControlWidth = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = true;
        string tag = entry.IsAutosave ? "[AUTOSAVE] " : string.Empty;
        string subtitle = entry.IsEmpty
            ? "Empty manual slot"
            : $"{entry.Civilization}  |  Turn {entry.Turn}  |  {entry.Date}";
        Label(row.transform, tag + entry.SaveName + "\n" + subtitle, 18, entry.IsValid ? TextColor : MutedText,
            FontStyles.Normal, TextAlignmentOptions.MidlineLeft);
        rowButtons.Add(button);
        if (selected == null && !entry.IsEmpty && entry.IsValid)
            selected = entry;
    }

    private void Select(SaveGameRepository.SaveEntry entry)
    {
        selected = entry;
        for (int i = 0; i < rowButtons.Count; i++)
        {
            var image = rowButtons[i].targetGraphic as Image;
            if (image != null)
            {
                var row = entries.FirstOrDefault(e => e.Id == rowButtons[i].gameObject.name);
                bool isSelected = row != null && row.Id == selected.Id;
                image.color = isSelected
                    ? new Color(0.35f, 0.28f, 0.16f, 1f)
                    : row != null && row.IsAutosave ? new Color(0.11f, 0.14f, 0.16f, 1f) : RowColor;
                if (isSelected && skin != null && skin.selectedSaveSlotRow != null)
                    ApplySprite(image, skin.selectedSaveSlotRow);
                else
                    ApplySprite(image, skin != null ? skin.saveSlotRow : null);
            }
        }
        UpdateDetails();
        UpdateButtons();
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
            primaryButton.GetComponentInChildren<TMP_Text>().text = "SAVE";
        }
        else
        {
            primaryButton.interactable = hasSelection && !selected.IsEmpty && selected.CanLoad && !busy;
            primaryButton.GetComponentInChildren<TMP_Text>().text = "LOAD";
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

    private static GameObject Panel(string name, Transform parent, Vector2 preferredSize, bool flexibleHeight = false)
    {
        var panel = Rect(name, parent, Vector2.zero, Vector2.one);
        var image = panel.AddComponent<Image>();
        image.color = PanelColor;
        var element = panel.AddComponent<LayoutElement>();
        if (preferredSize.x > 0) element.preferredWidth = preferredSize.x;
        if (preferredSize.y > 0) element.preferredHeight = preferredSize.y;
        if (flexibleHeight) element.flexibleHeight = 1;
        return panel;
    }

    private static GameObject Rect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return go;
    }

    private TMP_Text Label(Transform parent, string value, float size, Color color,
        FontStyles style, TextAlignmentOptions alignment)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<TextMeshProUGUI>();
        text.text = value;
        text.font = MenuUiTypography.Body(skin);
        text.fontSize = size;
        text.color = color;
        text.fontStyle = style;
        text.alignment = alignment;
        text.enableWordWrapping = true;
        text.raycastTarget = false;
        return text;
    }

    private Button AddButton(Transform parent, string label, Vector2 size, Color color, Sprite sprite, Action action)
    {
        var go = Rect(label, parent, Vector2.zero, Vector2.zero);
        go.GetComponent<RectTransform>().sizeDelta = size;
        var image = go.AddComponent<Image>();
        image.color = color;
        ApplySprite(image, sprite);
        var button = go.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        button.onClick.AddListener(() => action());
        Label(go.transform, label, 21, TextColor, FontStyles.Bold, TextAlignmentOptions.Center);
        return button;
    }

    private static void ApplySprite(Image image, Sprite sprite)
    {
        if (image == null || sprite == null) return;
        image.sprite = sprite;
        image.type = Image.Type.Sliced;
    }
}
