#if UNITY_EDITOR
using System.Text;
using UnityEditor;
using UnityEngine;

public static class CivilizationBalanceAudit
{
    private const float GlobalCombatWarning=.5f, GlobalYieldWarning=.75f;
    [MenuItem("Tools/Balance/Generate Civilization Balance Audit")]
    public static void Generate()
    {
        var report=new StringBuilder("CIVILIZATION BALANCE AUDIT\nCombat fields are fractions (0.10 = +10%); movementBonus is flat.\n\n");
        Scan<CivData>(report, c=>$"yield F/P/G/S/C/R={c.foodModifier}/{c.productionModifier}/{c.goldModifier}/{c.scienceModifier}/{c.cultureModifier}/{c.faithModifier}; combat A/D={c.attackBonus}/{c.defenseBonus}; movement(flat)={c.movementBonus}; targeted={c.unitBonuses?.Length??0}; city={c.cityBonuses?.Length??0}",c=>Mathf.Abs(c.attackBonus)>GlobalCombatWarning||Mathf.Abs(c.defenseBonus)>GlobalCombatWarning||MaxYield(c)>GlobalYieldWarning||Mathf.Abs(c.movementBonus-Mathf.Round(c.movementBonus))>.001f);
        Scan<LeaderData>(report,l=>$"yield F/P/G/S/C/R={l.foodModifier}/{l.productionModifier}/{l.goldModifier}/{l.scienceModifier}/{l.cultureModifier}/{l.faithModifier}; combat global/melee/ranged/city={l.militaryStrengthModifier}/{l.meleeAttackBonus}/{l.rangedAttackBonus}/{l.cityAttackBonus}; targeted={l.unitBonuses?.Length??0}; city={l.cityBonuses?.Length??0}",l=>Mathf.Abs(l.militaryStrengthModifier)>.5f);
        Scan<GovernmentData>(report,g=>$"combat={g.attackBonus}/{g.defenseBonus}; movement(flat)={g.movementBonus}; institutions unrest={g.institutions?.unrestModifier??0}; targeted={g.unitBonuses?.Length??0}",g=>Mathf.Abs(g.attackBonus)>GlobalCombatWarning);
        Scan<PolicyData>(report,p=>$"combat={p.attackBonus}/{p.defenseBonus}; movement(flat)={p.movementBonus}; targeted={p.unitBonuses?.Length??0}",p=>Mathf.Abs(p.attackBonus)>GlobalCombatWarning);
        Scan<BeliefData>(report,b=>$"combat={b.attackBonus}; targeted={b.unitBonuses?.Length??0}; city={b.cityYieldBonuses?.Length??0}",b=>Mathf.Abs(b.attackBonus)>GlobalCombatWarning);
        Scan<LegacyData>(report,l=>$"combat={l.attackBonus+l.attackModifier}/{l.defenseBonus+l.defenseModifier}; movement(flat)={l.movementBonus}; targeted={l.unitBonuses?.Length??0}; city={l.cityBonuses?.Length??0}; institutions unrest={l.institutions?.unrestModifier??0}",l=>Mathf.Abs(l.attackBonus+l.attackModifier)>GlobalCombatWarning||Mathf.Abs(l.defenseBonus+l.defenseModifier)>GlobalCombatWarning);
        Debug.Log(report.ToString());
    }
    static float MaxYield(CivData c)=>Mathf.Max(Mathf.Abs(c.foodModifier),Mathf.Abs(c.productionModifier),Mathf.Abs(c.goldModifier),Mathf.Abs(c.scienceModifier),Mathf.Abs(c.cultureModifier),Mathf.Abs(c.faithModifier));
    static void Scan<T>(StringBuilder b,System.Func<T,string> describe,System.Func<T,bool> suspicious) where T:ScriptableObject
    { foreach(string guid in AssetDatabase.FindAssets("t:"+typeof(T).Name)){var a=AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(guid));if(a!=null)b.Append(suspicious(a)?"[FLAG] ":"[OK] ").Append(a.name).Append(": ").AppendLine(describe(a));} }
}
#endif
