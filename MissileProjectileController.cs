// Assets/Scripts Repo/MissileProjectileController.cs
using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Drives the parabolic arc animation for a missile flight prefab.
/// Attach this component to any missile flight prefab and MissileManager will call
/// <see cref="StartFlight"/> instead of running its built-in arc fallback, giving you
/// full AnimationCurve control over the trajectory in the prefab designer.
/// </summary>
public class MissileProjectileController : MonoBehaviour
{
    [Header("Arc Curve")]
    [Tooltip("When enabled, uses HeightCurve to drive vertical offset instead of the default parabola.")]
    public bool useCustomCurve = false;

    [Tooltip("Custom height curve. X axis = normalized flight time (0-1). Y axis = height multiplier applied to arcHeight. " +
             "Default parabola peaks at 0.5 with value 1. Ignored when useCustomCurve is false.")]
    public AnimationCurve heightCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));

    [Header("Rotation")]
    [Tooltip("If true, the projectile continuously rotates to face its direction of travel.")]
    public bool faceDirection = true;
    [Tooltip("Rotation speed used for smooth look-at interpolation (degrees/sec). 0 = instant snap.")]
    public float rotationSpeed = 720f;

    [Header("Effects")]
    [Tooltip("Particle systems to Play() when flight begins (e.g., engine exhaust, trail).")]
    public ParticleSystem[] engineEffects;

    [Tooltip("Particle systems to Stop() when the missile reaches impact (cleaned up before Destroy).")]
    public ParticleSystem[] impactStopEffects;

    // ─── Public API ──────────────────────────────────────────────────────────
    /// <summary>
    /// Begin the flight from <paramref name="from"/> to <paramref name="to"/>.
    /// <paramref name="onComplete"/> is invoked when the missile reaches the target and
    /// MissileManager should handle detonation. The GameObject is destroyed by MissileManager
    /// immediately after this callback fires.
    /// </summary>
    public void StartFlight(Vector3 from, Vector3 to, float arcHeight, float duration, Action onComplete)
    {
        StopAllCoroutines();
        StartCoroutine(FlyCoroutine(from, to, arcHeight, Mathf.Max(0.05f, duration), onComplete));
    }

    // ─── Internal ────────────────────────────────────────────────────────────
    private IEnumerator FlyCoroutine(Vector3 from, Vector3 to, float arcHeight, float duration, Action onComplete)
    {
        // Activate engine particles
        if (engineEffects != null)
            foreach (var fx in engineEffects)
                if (fx != null) fx.Play();

        transform.position = from;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // Position
            Vector3 pos = Vector3.Lerp(from, to, t);
            float h = useCustomCurve ? heightCurve.Evaluate(t) : 4f * t * (1f - t);
            pos.y += arcHeight * h;
            transform.position = pos;

            // Rotation – look toward next frame's position
            if (faceDirection)
            {
                float tNext = Mathf.Clamp01((elapsed + Time.deltaTime) / duration);
                Vector3 nextPos = Vector3.Lerp(from, to, tNext);
                float hNext = useCustomCurve ? heightCurve.Evaluate(tNext) : 4f * tNext * (1f - tNext);
                nextPos.y += arcHeight * hNext;

                Vector3 dir = nextPos - pos;
                if (dir.sqrMagnitude > 0.0001f)
                {
                    Quaternion target = Quaternion.LookRotation(dir.normalized);
                    transform.rotation = rotationSpeed <= 0f
                        ? target
                        : Quaternion.RotateTowards(transform.rotation, target, rotationSpeed * Time.deltaTime);
                }
            }

            yield return null;
        }

        // Ensure we end exactly at the target
        transform.position = to;

        // Stop/deactivate engine effects before the object is destroyed
        if (impactStopEffects != null)
            foreach (var fx in impactStopEffects)
                if (fx != null) fx.Stop();

        onComplete?.Invoke();
    }
}
