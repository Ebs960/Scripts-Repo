using UnityEngine;

namespace GameCombat
{
    public enum ProjectileArcType { Straight, Parabolic, Homing }

    [CreateAssetMenu(fileName = "NewProjectileData", menuName = "Data/Projectile Data")]
    public class ProjectileData : ScriptableObject
    {
    [Header("Visuals")]
    public GameObject projectilePrefab;
    public float scale = 1f;
    public TrailRenderer trailEffect;
    public ParticleSystem impactEffect;

    [Header("Trajectory")]
    public ProjectileArcType arcType = ProjectileArcType.Parabolic;
    public float speed = 10f;
    public float gravity = 9.81f; // Used for parabolic
    public bool useGravity = true;
    public float homingStrength = 0f; // Used for homing

    [Header("Damage & Effects")]
    public float damage = 10f;
    public float areaOfEffectRadius = 0f; // 0 = single target
    public bool explodeOnImpact = false;
    public float explosionForce = 0f;
    public float statusEffectDuration = 0f;
    public string statusEffectName;

    [Header("Audio")]
    public AudioClip launchSound;
    public AudioClip impactSound;
    }
}
