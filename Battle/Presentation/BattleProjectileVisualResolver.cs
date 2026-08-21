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
    public static BattleProjectileVisual Resolve(BattleUnitSnapshot snapshot, int weaponIndex, bool special)
    {
        TacticalWeaponProfile weapon = snapshot != null && weaponIndex >= 0 && weaponIndex < snapshot.Weapons.Count ? snapshot.Weapons[weaponIndex] : null;
        return ResolveForWeapon(weapon,special);
    }

    public static BattleProjectileVisual ResolveForWeapon(TacticalWeaponProfile weapon,bool special)
    {
        ProjectileData projectile = weapon?.equipment?.projectileData;
        GameObject prefab = weapon?.tacticalProjectilePrefab != null ? weapon.tacticalProjectilePrefab : projectile?.projectilePrefab;
        GameObject impact = weapon?.tacticalImpactPrefab != null ? weapon.tacticalImpactPrefab : projectile?.impactVfxPrefab;
        ProjectileCategory? category = projectile != null ? projectile.category : null;
        BattleProjectileTravelType travel = weapon?.usesIndirectFire == true || category == ProjectileCategory.Shell
            ? BattleProjectileTravelType.BallisticArc
            : category == ProjectileCategory.Laser ? BattleProjectileTravelType.Beam
            : prefab != null ? BattleProjectileTravelType.Direct : BattleProjectileTravelType.Tracer;
        float speed = weapon != null && weapon.tacticalProjectileSpeed > 0f ? weapon.tacticalProjectileSpeed : Mathf.Max(1f, projectile?.launchSpeed ?? 8f);
        float arc = weapon != null && weapon.tacticalArcHeight > 0f ? weapon.tacticalArcHeight : Mathf.Max(.2f, projectile?.flightArcHeight ?? .8f);
        Vector3 scale = weapon != null ? weapon.tacticalProjectileScale : Vector3.one;
        if (scale == Vector3.zero) scale = Vector3.one;
        if (special) scale *= 1.6f;
        return new BattleProjectileVisual(prefab, impact, travel, speed, arc, scale);
    }
}
