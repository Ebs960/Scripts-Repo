public static class BattleDomainResolver
{
    public static BattleDomain Resolve(CombatUnit unit)
    {
        if (unit == null)
            return BattleDomain.Land;

        switch (unit.currentLayer)
        {
            case TileLayer.Underwater: return BattleDomain.Underwater;
            case TileLayer.Atmosphere: return BattleDomain.Air;
            case TileLayer.Orbit: return BattleDomain.Orbit;
        }

        var category = unit.data != null ? unit.data.unitType : CombatCategory.Swordsman;
        if (category == CombatCategory.Spaceship || category == CombatCategory.SpaceCarrier)
            return BattleDomain.Space;
        if (category == CombatCategory.Submarine)
            return BattleDomain.Underwater;
        if (category == CombatCategory.Aircraft || category == CombatCategory.Fighter ||
            category == CombatCategory.Bomber || category == CombatCategory.GroundAttack ||
            category == CombatCategory.Helicopter || category == CombatCategory.SeaPlane)
            return BattleDomain.Air;
        if (CombatUnitData.IsNavalCategory(category))
            return BattleDomain.NavalSurface;
        return BattleDomain.Land;
    }

    public static BattleDomainMask ToMask(BattleDomain domain) => (BattleDomainMask)(1 << (int)domain);
}
