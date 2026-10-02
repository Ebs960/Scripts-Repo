using UnityEngine;

/// <summary>
/// Base controller for Government detail screens. Controllers only read state and call manager/service APIs; they never mutate
/// simulation fields directly.
/// </summary>
public abstract class GovernmentTabBase : MonoBehaviour
{
    protected Civilization civ;
    protected GovernmentPanel panel;

    public Civilization BoundCivilization => civ;

    public void Bind(Civilization civilization) => Bind(civilization, panel);

    public void Bind(Civilization civilization, GovernmentPanel owner)
    {
        if (owner != null) panel = owner;
        if (civ == civilization) return;
        civ = civilization;
        OnCivilizationChanged();
    }

    /// <summary>Called when a different civilization is bound; clear selections that no longer apply.</summary>
    protected virtual void OnCivilizationChanged() { }

    public abstract void Refresh();
}
