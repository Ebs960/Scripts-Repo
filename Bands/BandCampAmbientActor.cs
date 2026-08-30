using UnityEngine;

public enum BandCampIdleCategory { Campfire, Shelter, General }

/// <summary>One visual-only camp inhabitant. It initializes once whenever its camp appears.</summary>
public sealed class BandCampAmbientActor : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private BandCampIdleCategory idleCategory;
    [Min(1), SerializeField] private int compatibleVariantCount = 1;
    [SerializeField] private string categoryParameter = "CampIdleCategory";
    [SerializeField] private string variantParameter = "CampIdleVariant";
    [SerializeField] private Vector2 speedRange = new Vector2(.95f, 1.05f);

    private void OnEnable()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogWarning($"[BandCampAmbientActor] '{name}' has no Animator; the camp remains usable.", this);
            return;
        }

        SetIntegerIfPresent(categoryParameter, (int)idleCategory);
        SetIntegerIfPresent(variantParameter, Random.Range(0, Mathf.Max(1, compatibleVariantCount)));
        animator.speed = Random.Range(Mathf.Min(speedRange.x, speedRange.y), Mathf.Max(speedRange.x, speedRange.y));

        // Evaluate the parameter-driven initial state, then offset its loop without relying on a state name.
        animator.Update(0f);
        animator.Play(0, 0, Random.value);
    }

    private void SetIntegerIfPresent(string parameterName, int value)
    {
        if (string.IsNullOrEmpty(parameterName)) return;
        int hash = Animator.StringToHash(parameterName);
        foreach (var parameter in animator.parameters)
            if (parameter.nameHash == hash && parameter.type == AnimatorControllerParameterType.Int)
            {
                animator.SetInteger(hash, value);
                return;
            }
    }
}
