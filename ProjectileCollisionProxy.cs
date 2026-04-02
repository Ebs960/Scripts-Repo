using UnityEngine;

namespace GameCombat
{
    /// <summary>
    /// DEPRECATED: Projectiles no longer use physics collision.
    /// Kept as a stub to avoid missing-component errors on existing prefabs.
    /// Safe to remove once all projectile prefabs have been re-saved without this component.
    /// </summary>
    [System.Obsolete("Projectile collision system removed. Projectiles are now pure visual.")]
    public class ProjectileCollisionProxy : MonoBehaviour
    {
        [System.Obsolete("No longer used")]
        public Projectile owner;
    }
}
