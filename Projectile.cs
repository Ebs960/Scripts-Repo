using UnityEngine;
using System.Collections;

namespace GameCombat
{
    /// <summary>
    /// Pure visual projectile — flies from spawn point to target using coroutine interpolation.
    /// No Rigidbody, no Collider, no physics. Damage is handled by the combat system
    /// (PerformAttack / ApplyDamage) before or after spawning the projectile visual.
    /// </summary>
    public class Projectile : MonoBehaviour
    {
    public ProjectileData data;
    public Transform target;
    public Vector3 targetPoint;
    public GameObject owner;
    private bool initialized = false;

    // Object pooling support
    private float lifetime = 0f;
    private const float maxLifetime = 10f;

    /// <summary>
    /// Initialize the projectile visual. Starts the flight coroutine immediately.
    /// overrideDamage is kept for API compatibility but is not used — damage is
    /// applied through the combat system, not by projectile collision.
    /// </summary>
    public void Initialize(ProjectileData projectileData, Vector3 start, Vector3 end, GameObject ownerObj, Transform targetTransform = null, int overrideDamage = -1)
    {
        data = projectileData;
        owner = ownerObj;
        target = targetTransform;
        targetPoint = end;
        transform.position = start;
        initialized = true;
        lifetime = 0f;

        if (data.trailEffect != null)
        {
            Instantiate(data.trailEffect, transform);
        }
        if (data.launchSound != null)
        {
            AudioSource.PlayClipAtPoint(data.launchSound, start);
        }

        // Start flight coroutine based on arc type
        StartCoroutine(FlyToTarget(start, end));
    }

    /// <summary>
    /// Coroutine-driven flight from start to end. Supports Straight, Parabolic, and Homing arcs.
    /// On arrival, plays impact effects and returns to pool.
    /// </summary>
    private IEnumerator FlyToTarget(Vector3 start, Vector3 end)
    {
        float speed = data != null ? data.speed : 10f;
        if (speed <= 0f) speed = 10f;

        float distance = Vector3.Distance(start, end);
        float duration = distance / speed;
        if (duration <= 0f) duration = 0.1f;

        float elapsed = 0f;
        Vector3 lastPos = start;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);

            // If homing and target is still alive, update end position
            Vector3 currentEnd = end;
            if (data != null && data.arcType == ProjectileArcType.Homing && target != null)
                currentEnd = target.position;

            Vector3 pos;
            if (data != null && data.arcType == ProjectileArcType.Parabolic)
            {
                // Parabolic arc: linear XZ + parabolic Y
                pos = Vector3.Lerp(start, currentEnd, t);
                float arcHeight = distance * 0.25f; // arc peaks at 25% of distance
                pos.y += arcHeight * 4f * t * (1f - t); // parabola centered at t=0.5
            }
            else
            {
                // Straight or Homing: linear interpolation
                pos = Vector3.Lerp(start, currentEnd, t);
            }

            // Face movement direction
            Vector3 dir = pos - lastPos;
            if (dir.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);

            transform.position = pos;
            lastPos = pos;

            yield return null;
        }

        // Snap to final position
        transform.position = (target != null) ? target.position : end;

        // Impact effects
        HandleImpactEffects();

        // Return to pool
        ReturnToPool();
    }

    /// <summary>
    /// Play impact VFX and SFX at the current position.
    /// </summary>
    private void HandleImpactEffects()
    {
        if (data == null) return;

        if (data.impactEffect != null)
        {
            Instantiate(data.impactEffect, transform.position, Quaternion.identity);
        }
        if (data.impactSound != null)
        {
            AudioSource.PlayClipAtPoint(data.impactSound, transform.position);
        }
    }

    void Update()
    {
        if (!initialized) return;

        lifetime += Time.deltaTime;

        // Safety: auto-return to pool if flight coroutine somehow stalls
        if (lifetime >= maxLifetime)
        {
            ReturnToPool();
        }
    }

    /// <summary>
    /// Return this projectile to the object pool.
    /// </summary>
    public void ReturnToPool()
    {
        initialized = false;
        StopAllCoroutines();

        if (SimpleObjectPool.Instance != null)
        {
            SimpleObjectPool.Instance.Return(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Reset the projectile for reuse from pool.
    /// </summary>
    public void Reset()
    {
        lifetime = 0f;
        initialized = false;
        data = null;
        target = null;
        targetPoint = Vector3.zero;
        owner = null;
        StopAllCoroutines();
    }
    }
}
