using UnityEngine;

/// <summary>One visual-only camp inhabitant. It initializes once whenever its camp appears.</summary>
public sealed class BandCampAmbientActor : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private Vector2 speedRange = new Vector2(.95f, 1.05f);

    private void OnEnable()
    {
        if (animator == null) animator = GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogWarning($"[BandCampAmbientActor] '{name}' has no Animator; the camp remains usable.", this);
            return;
        }

        animator.speed = Random.Range(Mathf.Min(speedRange.x, speedRange.y), Mathf.Max(speedRange.x, speedRange.y));

        // Evaluate the controller's default state, then offset its loop without relying on a state name.
        animator.Update(0f);
        animator.Play(0, 0, Random.value);
    }
}
