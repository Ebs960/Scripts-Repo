using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Layout stack orchestrator for HUD dropdown prefabs.
/// Keeps dropdown expansion/collapse and parent VerticalLayoutGroup in sync.
/// </summary>
public class HudDropdownStackController : MonoBehaviour
{
    [SerializeField] private bool accordionMode = false;
    [SerializeField] private bool autoFindDropdowns = true;
    [SerializeField] private RectTransform layoutRoot;

    private readonly List<HudDropdownButton> dropdowns = new();
    private Coroutine endOfFrameRebuildRoutine;

    private void Awake()
    {
        if (layoutRoot == null)
            layoutRoot = transform as RectTransform;
    }

    private void OnEnable()
    {
        if (layoutRoot == null)
            layoutRoot = transform as RectTransform;

        if (autoFindDropdowns)
            RefreshDropdownList();
        else
            SubscribeToRegisteredDropdowns();

        RebuildLayout();
        ScheduleEndOfFrameRebuild();
    }

    private void OnDisable()
    {
        UnsubscribeFromAll();

        if (endOfFrameRebuildRoutine != null)
        {
            StopCoroutine(endOfFrameRebuildRoutine);
            endOfFrameRebuildRoutine = null;
        }
    }

    public void RegisterDropdown(HudDropdownButton dropdown)
    {
        if (dropdown == null || dropdowns.Contains(dropdown))
            return;

        dropdowns.Add(dropdown);
        dropdown.OnExpandedChanged += HandleDropdownExpandedChanged;
    }

    public void UnregisterDropdown(HudDropdownButton dropdown)
    {
        if (dropdown == null)
            return;

        dropdown.OnExpandedChanged -= HandleDropdownExpandedChanged;
        dropdowns.Remove(dropdown);
    }

    public void RefreshDropdownList()
    {
        UnsubscribeFromAll();

        var found = GetComponentsInChildren<HudDropdownButton>(true);
        for (int i = 0; i < found.Length; i++)
        {
            if (found[i] == null)
                continue;

            dropdowns.Add(found[i]);
            found[i].OnExpandedChanged += HandleDropdownExpandedChanged;
        }
    }

    public void RebuildLayout()
    {
        if (layoutRoot == null)
            layoutRoot = transform as RectTransform;

        if (layoutRoot == null)
            return;

        LayoutRebuilder.ForceRebuildLayoutImmediate(layoutRoot);

        var parent = layoutRoot.parent as RectTransform;
        while (parent != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
            parent = parent.parent as RectTransform;
        }
    }

    private void HandleDropdownExpandedChanged(HudDropdownButton dropdown, bool expanded)
    {
        if (accordionMode && expanded)
        {
            for (int i = 0; i < dropdowns.Count; i++)
            {
                var other = dropdowns[i];
                if (other == null || other == dropdown)
                    continue;

                other.CollapseBody();
            }
        }

        RebuildLayout();
        ScheduleEndOfFrameRebuild();
    }

    private void ScheduleEndOfFrameRebuild()
    {
        if (endOfFrameRebuildRoutine != null)
            StopCoroutine(endOfFrameRebuildRoutine);

        endOfFrameRebuildRoutine = StartCoroutine(RebuildAtEndOfFrame());
    }

    private IEnumerator RebuildAtEndOfFrame()
    {
        yield return new WaitForEndOfFrame();
        RebuildLayout();
        endOfFrameRebuildRoutine = null;
    }

    private void SubscribeToRegisteredDropdowns()
    {
        for (int i = 0; i < dropdowns.Count; i++)
        {
            if (dropdowns[i] == null)
                continue;

            dropdowns[i].OnExpandedChanged -= HandleDropdownExpandedChanged;
            dropdowns[i].OnExpandedChanged += HandleDropdownExpandedChanged;
        }
    }

    private void UnsubscribeFromAll()
    {
        for (int i = 0; i < dropdowns.Count; i++)
        {
            if (dropdowns[i] != null)
                dropdowns[i].OnExpandedChanged -= HandleDropdownExpandedChanged;
        }

        dropdowns.Clear();
    }
}
