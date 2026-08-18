using System.Collections.Generic;
using UnityEngine;

/// <summary>Visual-only tactical representation. It never owns or mutates campaign state.</summary>
public sealed class BattleUnitView : MonoBehaviour
{
    public int BattleUnitId { get; private set; }
    public BattleUnitSnapshot Snapshot { get; private set; }

    private readonly List<GameObject> figures = new();
    private Transform figureRoot;
    private GameObject selectionRing;
    private Vector3 targetPosition;
    private bool hasPosition;

    public void Initialize(BattleUnitState state)
    {
        BattleUnitId = state != null ? state.UnitId : -1;
        Snapshot = state?.Snapshot;
        figureRoot = new GameObject("Figures").transform;
        figureRoot.SetParent(transform, false);
        figureRoot.localRotation = Quaternion.Euler(0f,
            state != null && state.Side == BattleSide.Defender ? 180f : 0f, 0f);
        BattleUnitVisualFactory.Populate(this, state, figureRoot, figures);
        selectionRing = BattleUnitVisualFactory.CreateSelectionRing(transform, state?.Side ?? BattleSide.Attacker);
        selectionRing.SetActive(true);
    }

    public void Sync(BattleUnitState state, Vector3 worldPosition, bool visible, bool selected)
    {
        if (state == null)
        {
            gameObject.SetActive(false);
            return;
        }

        gameObject.SetActive(visible);
        if (!visible)
            return;

        targetPosition = worldPosition;
        if (!hasPosition)
        {
            transform.position = targetPosition;
            hasPosition = true;
        }

        int desiredFigures = state.CurrentHealth > 0 && state.Snapshot != null
            ? Mathf.Max(1, Mathf.CeilToInt(
                Mathf.Clamp01(state.CurrentHealth / (float)Mathf.Max(1, state.Snapshot.MaximumHealth))
                * Mathf.Max(1, state.Snapshot.TacticalFigureCount)))
            : 0;
        for (int i = 0; i < figures.Count; i++)
            if (figures[i] != null)
                figures[i].SetActive(i < desiredFigures);

        if (selectionRing != null)
            selectionRing.transform.localScale = selected
                ? new Vector3(0.62f, 0.014f, 0.62f)
                : new Vector3(0.48f, 0.01f, 0.48f);
    }

    private void Update()
    {
        if (!hasPosition || !gameObject.activeInHierarchy)
            return;

        Vector3 previous = transform.position;
        transform.position = Vector3.Lerp(previous, targetPosition, 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime));
        Vector3 movement = targetPosition - previous;
        movement.y = 0f;
        if (movement.sqrMagnitude > 0.0001f && figureRoot != null)
            figureRoot.rotation = Quaternion.Slerp(figureRoot.rotation,
                Quaternion.LookRotation(movement.normalized, Vector3.up),
                1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
    }
}
