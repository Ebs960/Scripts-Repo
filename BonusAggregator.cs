using UnityEngine;
using System.Collections.Generic;

public static class BonusAggregator
{
    public struct UnitBonusAgg
    {
        public int attackAdd, defenseAdd, healthAdd, movePointsAdd, rangeAdd, attackPointsAdd, moraleAdd;
        public float attackPct, defensePct, healthPct, movePointsPct, rangePct, attackPointsPct, moralePct;
    }

    public struct WorkerBonusAgg
    {
        public int workPointsAdd, movePointsAdd, healthAdd;
        public float workPointsPct, movePointsPct, healthPct;
    }

    public struct YieldBonusAgg
    {
        public int foodAdd, productionAdd, goldAdd, scienceAdd, cultureAdd, faithAdd, policyPointsAdd;
        public float foodPct, productionPct, goldPct, sciencePct, culturePct, faithPct, policyPointsPct;
    }
    public struct EquipBonusAgg
    {
        public int attackAdd, defenseAdd, healthAdd, movePointsAdd, rangeAdd, attackPointsAdd;
        public float attackPct, defensePct, healthPct, movePointsPct, rangePct, attackPointsPct;
    }

    public static UnitBonusAgg AggregateUnitBonuses(Civilization civ, CombatUnitData unit)
    {
        UnitBonusAgg agg = new UnitBonusAgg();
        if (civ == null || unit == null) return agg;

        // Techs
        if (civ.researchedTechs != null)
        {
            foreach (var tech in civ.researchedTechs)
            {
                if (tech == null || tech.unitBonuses == null) continue;
                foreach (var b in tech.unitBonuses)
                {
                    if (b != null && b.unit == unit)
                    {
                        agg.attackAdd += b.attackAdd; agg.defenseAdd += b.defenseAdd; agg.healthAdd += b.healthAdd;
                        agg.movePointsAdd += b.movePointsAdd; agg.rangeAdd += b.rangeAdd; agg.attackPointsAdd += b.attackPointsAdd; agg.moraleAdd += b.moraleAdd;
                        agg.attackPct += b.attackPct; agg.defensePct += b.defensePct; agg.healthPct += b.healthPct;
                        agg.movePointsPct += b.movePointsPct; agg.rangePct += b.rangePct; agg.attackPointsPct += b.attackPointsPct; agg.moralePct += b.moralePct;
                    }
                }
            }
        }
        // Cultures
        if (civ.researchedCultures != null)
        {
            foreach (var culture in civ.researchedCultures)
            {
                if (culture == null || culture.unitBonuses == null) continue;
                foreach (var b in culture.unitBonuses)
                {
                    if (b != null && b.unit == unit)
                    {
                        agg.attackAdd += b.attackAdd; agg.defenseAdd += b.defenseAdd; agg.healthAdd += b.healthAdd;
                        agg.movePointsAdd += b.movePointsAdd; agg.rangeAdd += b.rangeAdd; agg.attackPointsAdd += b.attackPointsAdd; agg.moraleAdd += b.moraleAdd;
                        agg.attackPct += b.attackPct; agg.defensePct += b.defensePct; agg.healthPct += b.healthPct;
                        agg.movePointsPct += b.movePointsPct; agg.rangePct += b.rangePct; agg.attackPointsPct += b.attackPointsPct; agg.moralePct += b.moralePct;
                    }
                }
            }
        }
        return agg;
    }

    public static WorkerBonusAgg AggregateWorkerBonuses(Civilization civ, WorkerUnitData worker)
    {
        WorkerBonusAgg agg = new WorkerBonusAgg();
        if (civ == null || worker == null) return agg;
        if (civ.researchedTechs != null)
        {
            foreach (var tech in civ.researchedTechs)
            {
                if (tech == null || tech.workerBonuses == null) continue;
                foreach (var b in tech.workerBonuses)
                {
                    if (b != null && b.worker == worker)
                    {
                        agg.workPointsAdd += b.workPointsAdd; agg.movePointsAdd += b.movePointsAdd; agg.healthAdd += b.healthAdd;
                        agg.workPointsPct += b.workPointsPct; agg.movePointsPct += b.movePointsPct; agg.healthPct += b.healthPct;
                    }
                }
            }
        }
        if (civ.researchedCultures != null)
        {
            foreach (var culture in civ.researchedCultures)
            {
                if (culture == null || culture.workerBonuses == null) continue;
                foreach (var b in culture.workerBonuses)
                {
                    if (b != null && b.worker == worker)
                    {
                        agg.workPointsAdd += b.workPointsAdd; agg.movePointsAdd += b.movePointsAdd; agg.healthAdd += b.healthAdd;
                        agg.workPointsPct += b.workPointsPct; agg.movePointsPct += b.movePointsPct; agg.healthPct += b.healthPct;
                    }
                }
            }
        }
        return agg;
    }

    public static YieldBonusAgg AggregateImprovementBonuses(Civilization civ, ImprovementData imp)
    {
        YieldBonusAgg agg = new YieldBonusAgg();
        if (civ == null || imp == null) return agg;
        if (civ.researchedTechs != null)
        {
            foreach (var tech in civ.researchedTechs)
            {
                if (tech == null || tech.improvementBonuses == null) continue;
                foreach (var b in tech.improvementBonuses)
                {
                    if (b != null && b.improvement == imp)
                    {
                        agg.foodAdd += b.foodAdd; agg.productionAdd += b.productionAdd; agg.goldAdd += b.goldAdd;
                        agg.scienceAdd += b.scienceAdd; agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.productionPct += b.productionPct; agg.goldPct += b.goldPct;
                        agg.sciencePct += b.sciencePct; agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        if (civ.researchedCultures != null)
        {
            foreach (var culture in civ.researchedCultures)
            {
                if (culture == null || culture.improvementBonuses == null) continue;
                foreach (var b in culture.improvementBonuses)
                {
                    if (b != null && b.improvement == imp)
                    {
                        agg.foodAdd += b.foodAdd; agg.productionAdd += b.productionAdd; agg.goldAdd += b.goldAdd;
                        agg.scienceAdd += b.scienceAdd; agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.productionPct += b.productionPct; agg.goldPct += b.goldPct;
                        agg.sciencePct += b.sciencePct; agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        return agg;
    }

    public static YieldBonusAgg AggregateBuildingBonuses(Civilization civ, BuildingData building)
    {
        YieldBonusAgg agg = new YieldBonusAgg();
        if (civ == null || building == null) return agg;
        if (civ.researchedTechs != null)
        {
            foreach (var tech in civ.researchedTechs)
            {
                if (tech == null || tech.buildingBonuses == null) continue;
                foreach (var b in tech.buildingBonuses)
                {
                    if (b != null && b.building == building)
                    {
                        agg.foodAdd += b.foodAdd; agg.productionAdd += b.productionAdd; agg.goldAdd += b.goldAdd;
                        agg.scienceAdd += b.scienceAdd; agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.productionPct += b.productionPct; agg.goldPct += b.goldPct;
                        agg.sciencePct += b.sciencePct; agg.culturePct += b.culturePct; agg.faithPct += b.faithPct;
                    }
                }
            }
        }
        if (civ.researchedCultures != null)
        {
            foreach (var culture in civ.researchedCultures)
            {
                if (culture == null || culture.buildingBonuses == null) continue;
                foreach (var b in culture.buildingBonuses)
                {
                    if (b != null && b.building == building)
                    {
                        agg.foodAdd += b.foodAdd; agg.productionAdd += b.productionAdd; agg.goldAdd += b.goldAdd;
                        agg.scienceAdd += b.scienceAdd; agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.productionPct += b.productionPct; agg.goldPct += b.goldPct;
                        agg.sciencePct += b.sciencePct; agg.culturePct += b.culturePct; agg.faithPct += b.faithPct;
                    }
                }
            }
        }
        return agg;
    }
    public static EquipBonusAgg AggregateEquipmentBonuses(Civilization civ, EquipmentData equipment)
    {
        EquipBonusAgg agg = new EquipBonusAgg();
        if (civ == null || equipment == null) return agg;
        if (civ.researchedTechs != null)
        {
            foreach (var tech in civ.researchedTechs)
            {
                if (tech == null || tech.equipmentBonuses == null) continue;
                foreach (var b in tech.equipmentBonuses)
                {
                    if (b != null && b.equipment == equipment)
                    {
                        agg.attackAdd += b.attackAdd; agg.defenseAdd += b.defenseAdd; agg.healthAdd += b.healthAdd;
                        agg.movePointsAdd += b.movePointsAdd; agg.rangeAdd += b.rangeAdd; agg.attackPointsAdd += b.attackPointsAdd;
                        agg.attackPct += b.attackPct; agg.defensePct += b.defensePct; agg.healthPct += b.healthPct;
                        agg.movePointsPct += b.movePointsPct; agg.rangePct += b.rangePct; agg.attackPointsPct += b.attackPointsPct;
                    }
                }
            }
        }
        if (civ.researchedCultures != null)
        {
            foreach (var culture in civ.researchedCultures)
            {
                if (culture == null || culture.equipmentBonuses == null) continue;
                foreach (var b in culture.equipmentBonuses)
                {
                    if (b != null && b.equipment == equipment)
                    {
                        agg.attackAdd += b.attackAdd; agg.defenseAdd += b.defenseAdd; agg.healthAdd += b.healthAdd;
                        agg.movePointsAdd += b.movePointsAdd; agg.rangeAdd += b.rangeAdd; agg.attackPointsAdd += b.attackPointsAdd;
                        agg.attackPct += b.attackPct; agg.defensePct += b.defensePct; agg.healthPct += b.healthPct;
                        agg.movePointsPct += b.movePointsPct; agg.rangePct += b.rangePct; agg.attackPointsPct += b.attackPointsPct;
                    }
                }
            }
        }
        return agg;
    }

    public static YieldBonusAgg AggregateGenericBonuses(Civilization civ, ScriptableObject target)
    {
        YieldBonusAgg agg = new YieldBonusAgg();
        if (civ == null || target == null) return agg;
        if (civ.researchedTechs != null)
        {
            foreach (var tech in civ.researchedTechs)
            {
                if (tech == null || tech.genericYieldBonuses == null) continue;
                foreach (var b in tech.genericYieldBonuses)
                {
                    if (b != null && b.target == target)
                    {
                        agg.foodAdd += b.foodAdd; agg.productionAdd += b.productionAdd; agg.goldAdd += b.goldAdd;
                        agg.scienceAdd += b.scienceAdd; agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd;
                        agg.foodPct += b.foodPct; agg.productionPct += b.productionPct; agg.goldPct += b.goldPct;
                        agg.sciencePct += b.sciencePct; agg.culturePct += b.culturePct; agg.faithPct += b.faithPct;
                    }
                }
            }
        }
        if (civ.researchedCultures != null)
        {
            foreach (var culture in civ.researchedCultures)
            {
                if (culture == null || culture.genericYieldBonuses == null) continue;
                foreach (var b in culture.genericYieldBonuses)
                {
                    if (b != null && b.target == target)
                    {
                        agg.foodAdd += b.foodAdd; agg.productionAdd += b.productionAdd; agg.goldAdd += b.goldAdd;
                        agg.scienceAdd += b.scienceAdd; agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd;
                        agg.foodPct += b.foodPct; agg.productionPct += b.productionPct; agg.goldPct += b.goldPct;
                        agg.sciencePct += b.sciencePct; agg.culturePct += b.culturePct; agg.faithPct += b.faithPct;
                    }
                }
            }
        }
        return agg;
    }
}
