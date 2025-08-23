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

    public static YieldBonusAgg AggregateUnitYieldBonuses(Civilization civ, CombatUnitData unit)
    {
        YieldBonusAgg agg = new YieldBonusAgg();
        if (civ == null || unit == null) return agg;

        // Techs
        if (civ.researchedTechs != null)
        {
            foreach (var tech in civ.researchedTechs)
            {
                if (tech == null || tech.unitYieldBonuses == null) continue;
                foreach (var b in tech.unitYieldBonuses)
                {
                    if (b != null && b.unit == unit)
                    {
                        agg.foodAdd += b.foodAdd; agg.productionAdd += b.productionAdd; agg.goldAdd += b.goldAdd;
                        agg.scienceAdd += b.scienceAdd; agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.productionPct += b.productionPct; agg.goldPct += b.goldPct;
                        agg.sciencePct += b.sciencePct; agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        // Cultures
        if (civ.researchedCultures != null)
        {
            foreach (var culture in civ.researchedCultures)
            {
                if (culture == null || culture.unitYieldBonuses == null) continue;
                foreach (var b in culture.unitYieldBonuses)
                {
                    if (b != null && b.unit == unit)
                    {
                        agg.foodAdd += b.foodAdd; agg.productionAdd += b.productionAdd; agg.goldAdd += b.goldAdd;
                        agg.scienceAdd += b.scienceAdd; agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.productionPct += b.productionPct; agg.goldPct += b.goldPct;
                        agg.sciencePct += b.sciencePct; agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        // Policies
        if (civ.activePolicies != null)
        {
            foreach (var policy in civ.activePolicies)
            {
                if (policy == null || policy.unitYieldBonuses == null) continue;
                foreach (var b in policy.unitYieldBonuses)
                {
                    if (b != null && b.unit == unit)
                    {
                        agg.foodAdd += b.foodAdd; agg.productionAdd += b.productionAdd; agg.goldAdd += b.goldAdd;
                        agg.scienceAdd += b.scienceAdd; agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.productionPct += b.productionPct; agg.goldPct += b.goldPct;
                        agg.sciencePct += b.sciencePct; agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        // Government
        if (civ.currentGovernment != null && civ.currentGovernment.unitYieldBonuses != null)
        {
            foreach (var b in civ.currentGovernment.unitYieldBonuses)
            {
                if (b != null && b.unit == unit)
                {
                    agg.foodAdd += b.foodAdd; agg.productionAdd += b.productionAdd; agg.goldAdd += b.goldAdd;
                    agg.scienceAdd += b.scienceAdd; agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                    agg.foodPct += b.foodPct; agg.productionPct += b.productionPct; agg.goldPct += b.goldPct;
                    agg.sciencePct += b.sciencePct; agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                }
            }
        }

        return agg;
    }

    public static YieldBonusAgg AggregateEquipmentYieldBonuses(Civilization civ, EquipmentData equip)
    {
        YieldBonusAgg agg = new YieldBonusAgg();
        if (civ == null || equip == null) return agg;

        // Techs
        if (civ.researchedTechs != null)
        {
            foreach (var tech in civ.researchedTechs)
            {
                if (tech == null || tech.equipmentYieldBonuses == null) continue;
                foreach (var b in tech.equipmentYieldBonuses)
                {
                    if (b != null && b.equipment == equip)
                    {
                        agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                        agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                        agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        // Cultures
        if (civ.researchedCultures != null)
        {
            foreach (var culture in civ.researchedCultures)
            {
                if (culture == null || culture.equipmentYieldBonuses == null) continue;
                foreach (var b in culture.equipmentYieldBonuses)
                {
                    if (b != null && b.equipment == equip)
                    {
                        agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                        agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                        agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        // Policies
        if (civ.activePolicies != null)
        {
            foreach (var policy in civ.activePolicies)
            {
                if (policy == null || policy.equipmentYieldBonuses == null) continue;
                foreach (var b in policy.equipmentYieldBonuses)
                {
                    if (b != null && b.equipment == equip)
                    {
                        agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                        agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                        agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        // Government
        if (civ.currentGovernment != null && civ.currentGovernment.equipmentYieldBonuses != null)
        {
            foreach (var b in civ.currentGovernment.equipmentYieldBonuses)
            {
                if (b != null && b.equipment == equip)
                {
                    agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                    agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                    agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                    agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                }
            }
        }

        return agg;
    }

    public static (int food, int gold, int science, int culture, int faith, int policy) ComputeUnitPerTurnYield(Civilization civ, CombatUnitData unit, params EquipmentData[] equippedItems)
    {
        if (civ == null || unit == null) return (0,0,0,0,0,0);
        int baseFood = unit.foodPerTurn;
        int baseGold = unit.goldPerTurn;
        int baseSci  = unit.sciencePerTurn;
        int baseCul  = unit.culturePerTurn;
        int baseFai  = unit.faithPerTurn;
        int basePol  = unit.policyPointsPerTurn;

        // Include base equipment yields from all equipped items
        if (equippedItems != null)
        {
            foreach (var eq in equippedItems)
            {
                if (eq == null) continue;
                baseFood += eq.foodPerTurn;
                baseGold += eq.goldPerTurn;
                baseSci  += eq.sciencePerTurn;
                baseCul  += eq.culturePerTurn;
                baseFai  += eq.faithPerTurn;
                basePol  += eq.policyPointsPerTurn;
            }
        }

        var u = AggregateUnitYieldBonuses(civ, unit);
        // Sum equipment-based yield modifiers from bonuses too
        YieldBonusAgg eAgg = new YieldBonusAgg();
        if (equippedItems != null)
        {
            foreach (var eq in equippedItems)
            {
                var e = AggregateEquipmentYieldBonuses(civ, eq);
                eAgg.foodAdd += e.foodAdd; eAgg.goldAdd += e.goldAdd; eAgg.scienceAdd += e.scienceAdd; eAgg.cultureAdd += e.cultureAdd; eAgg.faithAdd += e.faithAdd; eAgg.policyPointsAdd += e.policyPointsAdd;
                eAgg.foodPct += e.foodPct; eAgg.goldPct += e.goldPct; eAgg.sciencePct += e.sciencePct; eAgg.culturePct += e.culturePct; eAgg.faithPct += e.faithPct; eAgg.policyPointsPct += e.policyPointsPct;
            }
        }

        int food = Mathf.RoundToInt((baseFood + u.foodAdd + eAgg.foodAdd) * (1f + u.foodPct + eAgg.foodPct));
        int gold = Mathf.RoundToInt((baseGold + u.goldAdd + eAgg.goldAdd) * (1f + u.goldPct + eAgg.goldPct));
        int sci  = Mathf.RoundToInt((baseSci  + u.scienceAdd + eAgg.scienceAdd) * (1f + u.sciencePct + eAgg.sciencePct));
        int cul  = Mathf.RoundToInt((baseCul  + u.cultureAdd + eAgg.cultureAdd) * (1f + u.culturePct + eAgg.culturePct));
        int fai  = Mathf.RoundToInt((baseFai  + u.faithAdd + eAgg.faithAdd) * (1f + u.faithPct + eAgg.faithPct));
        int pol  = Mathf.RoundToInt((basePol  + u.policyPointsAdd + eAgg.policyPointsAdd) * (1f + u.policyPointsPct + eAgg.policyPointsPct));

        return (food, gold, sci, cul, fai, pol);
    }

    public static YieldBonusAgg AggregateWorkerYieldBonuses(Civilization civ, WorkerUnitData worker)
    {
        YieldBonusAgg agg = new YieldBonusAgg();
        if (civ == null || worker == null) return agg;

        if (civ.researchedTechs != null)
        {
            foreach (var tech in civ.researchedTechs)
            {
                if (tech == null || tech.workerYieldBonuses == null) continue;
                foreach (var b in tech.workerYieldBonuses)
                {
                    if (b != null && b.worker == worker)
                    {
                        agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                        agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                        agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        if (civ.researchedCultures != null)
        {
            foreach (var culture in civ.researchedCultures)
            {
                if (culture == null || culture.workerYieldBonuses == null) continue;
                foreach (var b in culture.workerYieldBonuses)
                {
                    if (b != null && b.worker == worker)
                    {
                        agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                        agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                        agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        if (civ.activePolicies != null)
        {
            foreach (var policy in civ.activePolicies)
            {
                if (policy == null || policy.workerYieldBonuses == null) continue;
                foreach (var b in policy.workerYieldBonuses)
                {
                    if (b != null && b.worker == worker)
                    {
                        agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                        agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                        agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                        agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                    }
                }
            }
        }
        if (civ.currentGovernment != null && civ.currentGovernment.workerYieldBonuses != null)
        {
            foreach (var b in civ.currentGovernment.workerYieldBonuses)
            {
                if (b != null && b.worker == worker)
                {
                    agg.foodAdd += b.foodAdd; agg.goldAdd += b.goldAdd; agg.scienceAdd += b.scienceAdd;
                    agg.cultureAdd += b.cultureAdd; agg.faithAdd += b.faithAdd; agg.policyPointsAdd += b.policyPointsAdd;
                    agg.foodPct += b.foodPct; agg.goldPct += b.goldPct; agg.sciencePct += b.sciencePct;
                    agg.culturePct += b.culturePct; agg.faithPct += b.faithPct; agg.policyPointsPct += b.policyPointsPct;
                }
            }
        }
        return agg;
    }

    public static (int food, int gold, int science, int culture, int faith, int policy) ComputeWorkerPerTurnYield(Civilization civ, WorkerUnitData worker)
    {
        if (civ == null || worker == null) return (0,0,0,0,0,0);
        int baseFood = worker.foodPerTurn;
        int baseGold = worker.goldPerTurn;
        int baseSci  = worker.sciencePerTurn;
        int baseCul  = worker.culturePerTurn;
        int baseFai  = worker.faithPerTurn;
        int basePol  = worker.policyPointsPerTurn;

        var w = AggregateWorkerYieldBonuses(civ, worker);
        int food = Mathf.RoundToInt((baseFood + w.foodAdd) * (1f + w.foodPct));
        int gold = Mathf.RoundToInt((baseGold + w.goldAdd) * (1f + w.goldPct));
        int sci  = Mathf.RoundToInt((baseSci  + w.scienceAdd) * (1f + w.sciencePct));
        int cul  = Mathf.RoundToInt((baseCul  + w.cultureAdd) * (1f + w.culturePct));
        int fai  = Mathf.RoundToInt((baseFai  + w.faithAdd) * (1f + w.faithPct));
        int pol  = Mathf.RoundToInt((basePol  + w.policyPointsAdd) * (1f + w.policyPointsPct));
        return (food, gold, sci, cul, fai, pol);
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
