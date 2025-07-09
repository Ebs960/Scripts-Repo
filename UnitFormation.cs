using System.Linq;
using UnityEngine;

public class UnitFormation : MonoBehaviour
{
    private CombatUnit unit;
    private Animator rootAnimator;
    private Animator[] childAnimators;
    private int totalModels;

    /// <summary>
    /// Set up references and initial model count.
    /// </summary>
    public void Initialize(CombatUnit cu)
    {
        unit = cu;
        rootAnimator    = GetComponent<Animator>();
        childAnimators  = GetComponentsInChildren<Animator>();
        totalModels     = transform.childCount; 
        UpdateModels();
    }

    /// <summary>
    /// Fired whenever the unit's health changes.
    /// </summary>
    public void HandleHealthChanged(int newHealth, int maxHealth)
    {
        UpdateModels();
    }

    void UpdateModels()
    {
        float healthRatio = unit.currentHealth / (float)unit.MaxHealth;
        int toShow = Mathf.CeilToInt(totalModels * healthRatio);

        for (int i = 0; i < totalModels; i++)
        {
            transform.GetChild(i).gameObject.SetActive(i < toShow);
        }
    }

    /// <summary>
    /// Broadcast an animation trigger from the unit to all child animators.
    /// </summary>
    public void BroadcastAnimationTrigger(string triggerName)
    {
        rootAnimator.SetTrigger(triggerName);
        foreach (var anim in childAnimators)
            anim.SetTrigger(triggerName);
    }
} 