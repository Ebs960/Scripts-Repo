using GameCombat;
using UnityEngine;

public enum BattleProjectileTravelType { Direct, BallisticArc, Beam, Tracer, None }

public readonly struct BattleProjectileVisual
{
    public readonly GameObject Prefab, ImpactPrefab;
    public readonly BattleProjectileTravelType TravelType;
    public readonly float Speed, ArcHeight;
    public readonly Vector3 Scale;
    public BattleProjectileVisual(GameObject prefab, GameObject impactPrefab, BattleProjectileTravelType travelType, float speed, float arcHeight, Vector3 scale)
    { Prefab=prefab; ImpactPrefab=impactPrefab; TravelType=travelType; Speed=speed; ArcHeight=arcHeight; Scale=scale; }
}

/// <summary>Resolves stable snapshot weapon data into presentation-only flight settings.</summary>
public static class BattleProjectileVisualResolver
{
    public static BattleProjectileVisual Resolve(BattleUnitSnapshot snapshot, int weaponIndex, BattleAttackProfile specialProfile=null)
    {
        TacticalWeaponProfile weapon = snapshot != null && weaponIndex >= 0 && weaponIndex < snapshot.Weapons.Count ? snapshot.Weapons[weaponIndex] : null;
        ProjectileData snapshottedProjectile=snapshot!=null&&weaponIndex>=0&&weaponIndex<snapshot.WeaponProjectiles.Count?snapshot.WeaponProjectiles[weaponIndex]:null;
        return ResolveForWeapon(weapon,specialProfile!=null,snapshottedProjectile,specialProfile);
    }

    public static BattleProjectileVisual ResolveForWeapon(TacticalWeaponProfile weapon,bool special,ProjectileData projectileOverride=null,BattleAttackProfile specialProfile=null)
    {
        ProjectileData projectile = projectileOverride!=null?projectileOverride:weapon?.equipment?.projectileData;
        GameObject prefab = specialProfile?.projectilePrefab!=null?specialProfile.projectilePrefab:weapon?.tacticalProjectilePrefab != null ? weapon.tacticalProjectilePrefab : projectile?.projectilePrefab;
        GameObject impact = specialProfile?.impactVfxPrefab!=null?specialProfile.impactVfxPrefab:weapon?.tacticalImpactPrefab != null ? weapon.tacticalImpactPrefab : projectile?.impactVfxPrefab;
        ProjectileCategory? category = projectile != null ? projectile.category : null;
        BattleProjectileTravelType travel = specialProfile!=null&&specialProfile.projectileTravelType!=BattleProjectileTravelType.None?specialProfile.projectileTravelType:weapon?.usesIndirectFire == true || category == ProjectileCategory.Shell
            ? BattleProjectileTravelType.BallisticArc
            : category == ProjectileCategory.Laser ? BattleProjectileTravelType.Beam
            : prefab != null ? BattleProjectileTravelType.Direct : BattleProjectileTravelType.Tracer;
        float speed = specialProfile!=null&&specialProfile.projectileSpeed>0f?specialProfile.projectileSpeed:weapon != null && weapon.tacticalProjectileSpeed > 0f ? weapon.tacticalProjectileSpeed : Mathf.Max(1f, projectile?.launchSpeed ?? 8f);
        float arc = specialProfile!=null&&specialProfile.projectileArcHeight>0f?specialProfile.projectileArcHeight:weapon != null && weapon.tacticalArcHeight > 0f ? weapon.tacticalArcHeight : Mathf.Max(.2f, projectile?.flightArcHeight ?? .8f);
        Vector3 scale = specialProfile!=null?specialProfile.projectileScale:weapon != null ? weapon.tacticalProjectileScale : Vector3.one;
        if (scale == Vector3.zero) scale = Vector3.one;
        if (special) scale *= 1.6f;
        return new BattleProjectileVisual(prefab, impact, travel, speed, arc, scale);
    }
}
