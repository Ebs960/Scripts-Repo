using System;
using UnityEngine;
using GameCombat;

/// <summary>
/// Lightweight in-flight visual for unit-combat projectiles. Projectile prefabs are expected to
/// use local +Z as their forward direction so rotation along velocity points them at the target.
/// </summary>
public class LaunchedProjectile : MonoBehaviour
{
    private ProjectileData data;
    private BaseUnit shooter;
    private BaseUnit targetUnit;
    private Vector3 targetPosition;
    private int damage;
    private Vector3 startPosition;
    private float elapsed;
    private float duration;
    private Action<LaunchedProjectile> onImpact;

    public ProjectileData ProjectileData => data;
    public BaseUnit Shooter => shooter;
    public BaseUnit TargetUnit => targetUnit;
    public Vector3 TargetPosition => targetPosition;
    public int Damage => damage;

    public void Initialize(
        ProjectileData projectileData,
        BaseUnit shooterUnit,
        BaseUnit target,
        Vector3 targetPos,
        int damageAmount,
        Vector3 launchVelocity,
        Action<LaunchedProjectile> impactCallback = null)
    {
        data = projectileData;
        shooter = shooterUnit;
        targetUnit = target;
        targetPosition = target != null ? target.transform.position : targetPos;
        damage = damageAmount;
        startPosition = transform.position;
        onImpact = impactCallback;

        if (launchVelocity.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(launchVelocity.normalized, Vector3.up);

        float distance = Vector3.Distance(startPosition, targetPosition);
        float speed = Mathf.Max(0.1f, data != null ? data.launchSpeed : 18f);
        float maxDuration = Mathf.Max(0.05f, data != null ? data.maxFlightDuration : 1.25f);
        duration = Mathf.Min(maxDuration, distance / speed);
        duration = Mathf.Max(0.05f, duration);
    }

    private void Update()
    {
        if (data == null)
        {
            Destroy(gameObject);
            return;
        }

        if (targetUnit != null)
            targetPosition = targetUnit.transform.position;

        elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(elapsed / duration);

        Vector3 previous = transform.position;
        Vector3 flat = Vector3.Lerp(startPosition, targetPosition, t);
        flat.y += Mathf.Sin(t * Mathf.PI) * data.flightArcHeight;

        transform.position = flat;

        if (data.rotateAlongVelocity)
        {
            Vector3 velocity = transform.position - previous;
            if (velocity.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up);
        }

        if (t >= 1f)
            Impact();
    }

    private void Impact()
    {
        if (data.impactSound != null)
            AudioSource.PlayClipAtPoint(data.impactSound, transform.position);

        if (data.impactVfxPrefab != null)
            Instantiate(data.impactVfxPrefab, transform.position, Quaternion.identity);

        onImpact?.Invoke(this);

        Destroy(gameObject);
    }
}
