using UnityEngine;

namespace GameCombat
{
    // Forwards collision/trigger events from a child physics object to the parent Projectile instance.
    public class ProjectileCollisionProxy : MonoBehaviour
    {
        public Projectile owner;

        void OnCollisionEnter(Collision collision)
        {
            if (owner != null) owner.OnProxyCollisionEnter(collision);
        }

        void OnTriggerEnter(Collider other)
        {
            if (owner != null) owner.OnProxyTriggerEnter(other);
        }
    }
}
