using UnityEngine;
using System.Collections;

namespace GameCombat
{
    public class Projectile : MonoBehaviour
    {
    public ProjectileData data;
    public Transform target;
    public Vector3 targetPoint;
    public GameObject owner;
    private Rigidbody rb;
    private bool initialized = false;
    private Vector3 startVelocity;
    private ProjectileCollisionProxy collisionProxy;
    
    // Object pooling support
    private float lifetime = 0f;
    private const float maxLifetime = 10f; // Auto-return to pool after 10 seconds

    // overrideDamage: if >=0, this damage value will be applied on hit instead of ProjectileData.damage
    public void Initialize(ProjectileData projectileData, Vector3 start, Vector3 end, GameObject ownerObj, Transform targetTransform = null, int overrideDamage = -1)
    {
        data = projectileData;
        owner = ownerObj;
        target = targetTransform;
        targetPoint = end;
        transform.position = start;
        // Try to find an existing Rigidbody on this object or its children; many weapon prefabs keep the physical collider on a child.
        rb = GetComponentInChildren<Rigidbody>();
        if (rb == null)
        {
            // Add a Rigidbody to the root if none exists
            rb = gameObject.AddComponent<Rigidbody>();
        }
        // If the Rigidbody is on a child, add a collision proxy component on that child so collision callbacks forward here
        if (rb.gameObject != this.gameObject)
        {
            collisionProxy = rb.gameObject.GetComponent<ProjectileCollisionProxy>();
            if (collisionProxy == null) collisionProxy = rb.gameObject.AddComponent<ProjectileCollisionProxy>();
            collisionProxy.owner = this;
        }
        else
        {
            // Ensure this root object has a proxy so OnCollisionEnter still runs here
            collisionProxy = GetComponent<ProjectileCollisionProxy>();
            if (collisionProxy == null) collisionProxy = gameObject.AddComponent<ProjectileCollisionProxy>();
            collisionProxy.owner = this;
        }
        rb.useGravity = data.useGravity && data.arcType == ProjectileArcType.Parabolic;
        rb.isKinematic = false;
        initialized = true;
        this.overrideDamage = overrideDamage;
        if (data.arcType == ProjectileArcType.Parabolic)
        {
            startVelocity = CalculateParabolicVelocity(start, end, data.speed);
            rb.linearVelocity = startVelocity;
        }
        else if (data.arcType == ProjectileArcType.Straight)
        {
            rb.linearVelocity = (end - start).normalized * data.speed;
        }
        else if (data.arcType == ProjectileArcType.Homing && target != null)
        {
            rb.linearVelocity = (target.position - start).normalized * data.speed;
        }
        if (data.trailEffect != null)
        {
            var trail = Instantiate(data.trailEffect, transform);
        }
        if (data.launchSound != null)
        {
            AudioSource.PlayClipAtPoint(data.launchSound, start);
        }
    }

    // Called by ProjectileCollisionProxy when a child collides
    public void OnProxyCollisionEnter(Collision collision)
    {
        HandleImpact(collision.collider);
    }

    // Called by proxy for trigger-based collisions
    public void OnProxyTriggerEnter(Collider other)
    {
        HandleImpact(other);
    }

    // Centralized impact handling (works for both collision and trigger events)
    private void HandleImpact(Collider collider)
    {
        if (!initialized) return;
        if (data.explodeOnImpact && data.areaOfEffectRadius > 0f)
        {
            Explode();
        }
        else
        {
            ApplyDamage(collider);
        }
        if (data.impactEffect != null)
        {
            Instantiate(data.impactEffect, transform.position, Quaternion.identity);
        }
        if (data.impactSound != null)
        {
            AudioSource.PlayClipAtPoint(data.impactSound, transform.position);
        }

        // Return to pool if available, otherwise destroy
        if (SimpleObjectPool.Instance != null)
            SimpleObjectPool.Instance.Return(this.gameObject);
        else
            Destroy(gameObject);
    }

    void FixedUpdate()
    {
        if (!initialized) return;
        if (data.arcType == ProjectileArcType.Homing && target != null)
        {
            Vector3 direction = (target.position - transform.position).normalized;
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, direction * data.speed, data.homingStrength * Time.fixedDeltaTime);
        }
    }

    // OnCollisionEnter is handled via proxy on the physics child; keeping empty handler to avoid Unity warnings if proxy not used
    void OnCollisionEnter(Collision collision) { }

    void Explode()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, data.areaOfEffectRadius);
        foreach (var hit in hits)
        {
            ApplyDamage(hit);
            if (data.explosionForce > 0f && hit.attachedRigidbody != null)
            {
                hit.attachedRigidbody.AddExplosionForce(data.explosionForce, transform.position, data.areaOfEffectRadius);
            }
        }
    }

    void ApplyDamage(Collider collider)
    {
        // Prefer CombatUnit when present
        var unit = collider.GetComponent<CombatUnit>();
        int dmg = (overrideDamage >= 0) ? overrideDamage : Mathf.RoundToInt(data.damage);
        var attackerUnit = owner != null ? owner.GetComponent<CombatUnit>() : null;
        if (unit != null)
        {
            bool died = unit.ApplyDamage(dmg, attackerUnit, false);
            // Attacker award handled inside ApplyDamage/RegisterKillFromProjectile
            return;
        }
    }

    private int overrideDamage = -1;

    Vector3 CalculateParabolicVelocity(Vector3 start, Vector3 end, float speed)
    {
        Vector3 toTarget = end - start;
        float y = toTarget.y;
        toTarget.y = 0;
        float xz = toTarget.magnitude;
        float t = xz / speed;
        float vy = (y / t) + 0.5f * Physics.gravity.y * t;
        Vector3 result = toTarget.normalized * speed;
        result.y = vy;
        return result;
    }

    void Update()
    {
        if (!initialized) return;

        lifetime += Time.deltaTime;
        
        // Auto-return to pool if lifetime exceeded
        if (lifetime >= maxLifetime)
        {
            ReturnToPool();
        }
    }

    /// <summary>
    /// Return this projectile to the object pool
    /// </summary>
    public void ReturnToPool()
    {
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
    /// Reset the projectile for reuse
    /// </summary>
    public void Reset()
    {
        lifetime = 0f;
        initialized = false;
        data = null;
        target = null;
        targetPoint = Vector3.zero;
        owner = null;
        overrideDamage = -1;
        
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }
    }
}
